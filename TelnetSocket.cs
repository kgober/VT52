// TelnetSocket.cs
// Copyright (c) 2016, 2017 Kenneth Gober
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Emulator
{
    // The TelnetSocket class implements the 'bottom half' of the Telnet protocol.
    // It accepts tokens from the top half to be converted to bytes and sent to the
    // network (doubling IACs as needed), and converts bytes from the network into
    // tokens (undoubling IACs as needed) to be consumed by the top half.

    // Future Improvements / To Do
    // improve detection/handling of disconnects
    // would copying bytes during tokenization simplify logic?
    // would allowing tokenizer to read/block on incomplete tokens simplify logic?
    // move send-buffer flush out of read loop (enables reads to block)

    class TelnetSocket
    {
        // event raised to indicate a received token is ready to be read by top half
        public delegate void ReceiveEventHandler(Object sender, EventArgs e);
        public event ReceiveEventHandler Receive;

        private Socket mSocket;
        private Thread mThread;
        private volatile Boolean mStopFlag;
        private Byte[] mSendBuf = new Byte[512];
        private Int32 mSendPtr;
        private Byte[] mRecvBuf;
        private Int32 mRecvPtr;
        private Queue<Token> mTokenQueue = new Queue<Token>();

        public TelnetSocket(Socket socket)
        {
            mSocket = socket;
            mThread = new Thread(ThreadProc);
            mThread.Start();
        }

        public Boolean Connected
        {
            get
            {
                if (!mSocket.Connected) return false;
                if ((mSocket.Poll(0, SelectMode.SelectRead)) && (mSocket.Available == 0)) return false;
                return true;
            }
        }

        public void Close()
        {
            mStopFlag = true;
        }
        
        public Token Peek()
        {
            lock (mTokenQueue)
            {
                if (mTokenQueue.Count == 0) return (Connected) ? Token.None : Token.Closed;
                return mTokenQueue.Peek();
            }
        }

        public Token Read()
        {
            lock (mTokenQueue)
            {
                if (mTokenQueue.Count == 0) return (Connected) ? Token.None : Token.Closed;
                return mTokenQueue.Dequeue();
            }
        }

        public void Write(Token token)
        {
            switch (token.Type)
            {
                case TokenType.Data:
                    Send_Data(token.mBuf, token.mPtr, token.mLen);
                    break;
                case TokenType.Command:
                    Send_IAC(token[0]);
                    break;
                case TokenType.DM:
                    Send_IAC(IAC.DM);
                    break;
                case TokenType.NOP:
                    Send_IAC(token[0]);
                    break;
                case TokenType.DO:
                    Send_IAC(IAC.DO, token[0]);
                    break;
                case TokenType.DONT:
                    Send_IAC(IAC.DONT, token[0]);
                    break;
                case TokenType.WILL:
                    Send_IAC(IAC.WILL, token[0]);
                    break;
                case TokenType.WONT:
                    Send_IAC(IAC.WONT, token[0]);
                    break;
                case TokenType.SB:
                    Send_IAC(IAC.SB, token.mBuf[token.mPtr]);
                    Send_Data(token.mBuf, token.mPtr + 1, token.mLen - 1);
                    Send_IAC(IAC.SE);
                    break;
                default:
                    throw new ArgumentException();
            }
        }

        public void Flush()
        {
            Flush(SocketFlags.None);
        }

        public void Flush(SocketFlags flags)
        {
            lock (mSendBuf)
            {
                if (mSendPtr != 0)
                {
                    mSocket.Send(mSendBuf, 0, mSendPtr, flags);
                    mSendPtr = 0;
                }
            }
        }

        // send data bytes, doubling each IAC (255) byte.
        private void Send_Data(Byte[] buffer, Int32 offset, Int32 count)
        {
#if DEBUG
            Log.WriteLine("Send data: {0} ({1:D0} bytes)", Encoding.ASCII.GetString(buffer, offset, count), count);
#endif
            Int32 n = offset + count;
            Int32 p = offset;
            Int32 q = p;
            while (p < n)
            {
                while ((q < n) && (buffer[q] != (Byte)IAC.IAC)) q++;
                if (q == n)
                {
                    _Send(buffer, p, q - p);
                    break;
                }
                _Send(buffer, p, q - p + 1);
                p = q;
                q = p + 1;
            }
        }

        private void Send_IAC(IAC command)
        {
            Send_IAC((Byte)command);
        }

        private void Send_IAC(Byte command)
        {
#if DEBUG
            Log.WriteLine("Send IAC {0} (255 {1:D0})", (IAC)command, command);
#endif
            Byte[] buf = new Byte[2];
            buf[0] = (Byte)IAC.IAC;
            buf[1] = command;
            if (command != (Byte)IAC.DM)
            {
                _Send(buf, 0, 2);
                return;
            }
            lock (mSendBuf)
            {
                Int32 p = mSendPtr;
                if ((p + 2) >= mSendBuf.Length) Flush(SocketFlags.None);
                _Send(buf, 0, 2);
                Flush(SocketFlags.OutOfBand);
            }
        }

        private void Send_IAC(IAC command, Byte option)
        {
#if DEBUG
            Log.WriteLine("Send IAC {0} {1} (255 {2:D0} {3:D0})", command, (Telnet.Option)option, (Byte)command, option);
#endif
            Byte[] buf = new Byte[3];
            buf[0] = (Byte)IAC.IAC;
            buf[1] = (Byte)command;
            buf[2] = option;
            _Send(buf, 0, 3);
        }

        private void _Send(Byte[] buffer, Int32 offset, Int32 count)
        {
            lock (mSendBuf)
            {
                Int32 p = mSendPtr;
                while ((p + count) >= mSendBuf.Length)
                {
                    while (p < mSendBuf.Length) mSendBuf[p++] = buffer[offset++];
                    count -= (p - mSendPtr);
                    mSocket.Send(mSendBuf, 0, p, SocketFlags.None);
                    mSendPtr = p = 0;
                }
                while (count-- > 0) mSendBuf[p++] = buffer[offset++];
                mSendPtr = p;
            }
        }

        // socket receive thread
        private void ThreadProc()
        {
            mSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.OutOfBandInline, true);
            mSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.Expedited, true);
            while (!mStopFlag)
            {
                // receive and process as much data as is available
                Boolean f = false;
                while ((mSocket.Connected) && (mSocket.Available != 0))
                {
                    Int32 n = mSocket.Available;
                    if (n > 512) n = 512; // if there's a lot of data, process it in 512-byte chunks
                    Byte[] buf = new Byte[n]; // always create a new buffer for Tokenize
                    SocketError e;
                    n = mSocket.Receive(buf, 0, n, SocketFlags.None, out e);
                    if (e != SocketError.Success) throw new ApplicationException(String.Concat("Error reading socket: ", e.ToString()));
                    if (n != buf.Length) throw new ApplicationException("Short read from socket");
                    f |= Tokenize(buf); // Tokenize returns true if a complete token was queued
                }
                if ((f) && (Receive != null)) Receive(this, new EventArgs());
                if (!Connected) break;
                Flush(); // don't sleep without first emptying application send buffer
                Thread.Sleep(1);
            }
#if DEBUG
            Log.WriteLine("Disconnected");
#endif
            Flush();
            mSocket.Close();
            lock (mTokenQueue) mTokenQueue.Enqueue(Token.Closed);
            if (Receive != null) Receive(this, new EventArgs());
        }

        // Tokenize - split a buffer into tokens, return true if a token was completed
        //
        //   buffers given to Tokenize should be considered to be 'owned' by Tokenize.
        //   Tokenize does not copy, it *references* byte ranges within buffer.
        //   this means: buffers should not be modified/reused/refilled
        //
        //   IAC commands are individual tokens
        //   note: <IAC SB opt ... IAC SE> is treated as one token
        //   consecutive data bytes may be merged into a single token
        //   received data may be broken into multiple tokens (eg at <CR> or <IAC>)
        private Boolean Tokenize(Byte[] buffer)
        {
            Int32 p = 0;
            Int32 q = 0;
            Boolean f = false;

            // if a previous buffer is pending, it must contain an incomplete token
            // check to see if the token can now be completed
            if (mRecvBuf != null)
            {
                p = mRecvPtr;
                Token t = GetToken(ref p, buffer, ref q);
                if (t.Type == TokenType.Invalid)
                {
                    // token still not complete, combine previous and new buffer
                    Byte[] buf = new Byte[mRecvBuf.Length - mRecvPtr + buffer.Length];
                    q = 0;
                    p = mRecvPtr;
                    while (p < mRecvBuf.Length) buf[q++] = mRecvBuf[p++];
                    p = 0;
                    while (p < buffer.Length) buf[q++] = buffer[p++];
                    mRecvBuf = buf;
                    mRecvPtr = 0;
                    return false;
                }
                mRecvBuf = null;
                mRecvPtr = 0;
                lock (mTokenQueue) mTokenQueue.Enqueue(t);
                f = true;
            }

            // process remaining tokens in buffer
            while (true)
            {
                Int32 oq = q;
                Token t = GetToken(ref p, buffer, ref q);
                if (t.Type == TokenType.Invalid)
                {
                    mRecvBuf = buffer;
                    mRecvPtr = oq;
                    return f;
                }
                if (t.Type == TokenType.None) return f;
                lock (mTokenQueue) mTokenQueue.Enqueue(t);
                f = true;
            }
        }

        private Token GetToken(ref Int32 p, Byte[] buffer, ref Int32 q)
        {
            Int32 op = p; // original value of index into mRecvBuf
            Int32 oq = q; // original value of index into buffer
            Int32 state = 0;
            Int32 n;
            while ((n = PeekTokenByte(p, buffer, q)) != -1)
            {
                IAC b = (IAC)n;
                switch (state)
                {
                    case 0: // start
                        if (b == IAC.IAC) state = 3;
                        else if (n == '\r') state = 2;
                        else state = 1;
                        ReadTokenByte(ref p, buffer, ref q);
                        break;

                    case 1: // data
                        if ((b == IAC.IAC) || (n == '\r')) return new Token(TokenType.Data, buffer, oq, q - oq);
                        ReadTokenByte(ref p, buffer, ref q);
                        break;

                    case 2: // CR
                        if ((n == '\n') || (n == 0))
                        {
                            ReadTokenByte(ref p, buffer, ref q);
                            if ((mRecvBuf != null) && (p == mRecvBuf.Length) && (p != op) && (q != oq))
                            {
                                // recopy if this token was split across 2 buffers
                                n = (p - op) + (q - oq);
                                Byte[] buf = new Byte[n];
                                n = 0;
                                for (Int32 i = op; i < p; i++) buf[n++] = mRecvBuf[i];
                                for (Int32 i = oq; i < q; i++) buf[n++] = buffer[i];
                                return new Token(TokenType.Data, buf, 0, n);
                            }
                            return new Token(TokenType.Data, buffer, oq, q - oq);
                        }
                        if (q == oq) return new Token(TokenType.Data, mRecvBuf, op, p - op);
                        return new Token(TokenType.Data, buffer, oq, q - oq);

                    case 3: // IAC
                        if (b == IAC.IAC)
                        {
                            Token t = (q == oq) ? new Token(TokenType.Data, mRecvBuf, op, 1) : new Token(TokenType.Data, buffer, oq, 1);
                            ReadTokenByte(ref p, buffer, ref q);
                            return t;
                        }
                        if ((b == IAC.GA) || (b == IAC.BRK) || (b == IAC.IP) || (b == IAC.AO) || (b == IAC.AYT) || (b == IAC.EC) || (b == IAC.EL) || (b == IAC.EOR) || (b == IAC.EOF) || (b == IAC.SUSP) || (b == IAC.ABORT))
                        {
                            ReadTokenByte(ref p, buffer, ref q);
                            return new Token(TokenType.Command, buffer, q - 1, 1);
                        }
                        if (b == IAC.DM)
                        {
                            ReadTokenByte(ref p, buffer, ref q);
                            if (q == oq) return new Token(TokenType.DM, mRecvBuf, p - 1, 1);
                            return new Token(TokenType.DM, buffer, q - 1, 1);
                        }
                        ReadTokenByte(ref p, buffer, ref q);
                        if (b == IAC.DO) state = 4;
                        else if (b == IAC.DONT) state = 5;
                        else if (b == IAC.WILL) state = 6;
                        else if (b == IAC.WONT) state = 7;
                        else if (b == IAC.SB) state = 8;
                        else if (q == oq) return new Token(TokenType.NOP, mRecvBuf, p - 1, 1);
                        else return new Token(TokenType.NOP, buffer, q - 1, 1);
                        break;

                    case 4: // IAC DO
                        ReadTokenByte(ref p, buffer, ref q);
                        return new Token(TokenType.DO, buffer, q - 1, 1);

                    case 5: // IAC DONT
                        ReadTokenByte(ref p, buffer, ref q);
                        return new Token(TokenType.DONT, buffer, q - 1, 1);

                    case 6: // IAC WILL
                        ReadTokenByte(ref p, buffer, ref q);
                        return new Token(TokenType.WILL, buffer, q - 1, 1);

                    case 7: // IAC WONT
                        ReadTokenByte(ref p, buffer, ref q);
                        return new Token(TokenType.WONT, buffer, q - 1, 1);

                    case 8: // IAC SB
                        ReadTokenByte(ref p, buffer, ref q);
                        state = 9;
                        break;

                    case 9: // IAC SB OPT ...
                        if (b == IAC.IAC) state = 10;
                        ReadTokenByte(ref p, buffer, ref q);
                        break;

                    case 10: // IAC SB OPT ... IAC
                        ReadTokenByte(ref p, buffer, ref q);
                        if (b == IAC.SE)
                        {
                            if ((mRecvBuf != null) && (p == mRecvBuf.Length) && (p != op) && (q != oq))
                            {
                                // recopy if this token was split across 2 buffers
                                n = (p - op) + (q - oq);
                                Byte[] buf = new Byte[n];
                                n = 0;
                                for (Int32 i = op; i < p; i++) buf[n++] = mRecvBuf[i];
                                for (Int32 i = oq; i < q; i++) buf[n++] = buffer[i];
                                return new Token(TokenType.SB, buf, 2, n - 4);
                            }
                            return new Token(TokenType.SB, buffer, oq + 2, q - oq - 4);
                        }
                        if (b == IAC.IAC) state = 11;
                        else state = 9;
                        break;

                    case 11: // IAC SB OPT ... IAC IAC ...
                        if (b == IAC.IAC) state = 12;
                        ReadTokenByte(ref p, buffer, ref q);
                        break;

                    case 12: // IAC SB OPT ... IAC IAC ... IAC
                        ReadTokenByte(ref p, buffer, ref q);
                        if (b == IAC.SE)
                        {
                            if ((mRecvBuf != null) && (p == mRecvBuf.Length) && (p != op) && (q != oq))
                            {
                                // recopy if this token was split across 2 buffers (undoubling IACs)
                                n = (p - op) + (q - oq);
                                Byte[] buf = new Byte[n];
                                n = 0;
                                for (Int32 i = op; i < p; i++) buf[n++] = mRecvBuf[i];
                                for (Int32 i = oq; i < q; i++) buf[n++] = buffer[i];
                                for (Int32 i = 0; i < n; i++)
                                {
                                    if (buf[i] != (Byte)IAC.IAC) continue;
                                    if (buf[i] == buf[i + 1])
                                    {
                                        n--;
                                        for (Int32 j = i; j < n; j++) buf[j] = buf[j + 1];
                                    }
                                }
                                return new Token(TokenType.SB, buf, 2, n - 4);
                            }
                            // undouble embedded IACs
                            n = q;
                            for (Int32 i = oq; i < n; i++)
                            {
                                if (buffer[i] != (Byte)IAC.IAC) continue;
                                if (buffer[i] == buffer[i + 1])
                                {
                                    n--;
                                    for (Int32 j = i; j < n; j++) buffer[j] = buffer[j + 1];
                                }
                            }
                            return new Token(TokenType.SB, buffer, oq + 2, n - oq - 4);
                        }
                        state = 11;
                        break;
                }
            }
            if (state == 0) return new Token(TokenType.None, null, 0, 0);
            if (state == 1) return new Token(TokenType.Data, buffer, oq, q - oq);
            return new Token(TokenType.Invalid, null, 0, 0);
        }

        private Int32 PeekTokenByte(Int32 p, Byte[] buffer, Int32 q)
        {
            if ((mRecvBuf != null) && (p < mRecvBuf.Length)) return mRecvBuf[p];
            if (q < buffer.Length) return buffer[q];
            return -1;
        }

        private Int32 ReadTokenByte(ref Int32 p, Byte[] buffer, ref Int32 q)
        {
            if ((mRecvBuf != null) && (p < mRecvBuf.Length)) return mRecvBuf[p++];
            if (q < buffer.Length) return buffer[q++];
            return -1;
        }
    }

    enum IAC : byte
    {
        IAC = 255,
        DONT = 254,
        DO = 253,
        WONT = 252,
        WILL = 251,
        SB = 250,
        GA = 249,
        EL = 248,
        EC = 247,
        AYT = 246,
        AO = 245,
        IP = 244,
        BRK = 243,
        DM = 242,
        NOP = 241,
        SE = 240,
        EOR = 239,
        ABORT = 238,
        SUSP = 237,
        EOF = 236,
    }

    enum TokenType
    {
        Closed = -2,    // connection closed (and buffer empty)
        Invalid = -1,   // recv buffer incomplete (try again when more received)
        None = 0,       // recv buffer empty
        Data = 1,       // data bytes, buffer contains data
        Command = 2,    // IAC (cmd), buffer contains cmd byte
        NOP = 3,        // IAC (NOP), buffer contains cmd byte (NOP or unrecognized cmd)
        DO = 4,         // IAC DO (opt), buffer contains opt
        DONT = 5,       // IAC DONT (opt), buffer contains opt
        WILL = 6,       // IAC WILL (opt), buffer contains opt
        WONT = 7,       // IAC WONT (opt), buffer contains opt
        DM = 8,         // IAC (DM), buffer contains cmd byte (DM)
        SB = 9,         // IAC SB (opt ...) IAC SE, buffer contains opt ...
    }

    struct Token
    {
        public static Token Closed
        {
            get { return new Token(TokenType.Closed, null, 0, 0); }
        }

        public static Token Invalid
        {
            get { return new Token(TokenType.Invalid, null, 0, 0); }
        }

        public static Token None
        {
            get { return new Token(TokenType.None, null, 0, 0); }
        }

        public static Token DM
        {
            get { return new Token(TokenType.DM, new Byte[] { (Byte)IAC.DM }, 0, 1); }
        }

        public static Token Data(Byte[] buffer, Int32 offset, Int32 count)
        {
            return new Token(TokenType.Data, buffer, offset, count);
        }

        public static Token Data(Byte data)
        {
            Byte[] buf = new Byte[1];
            buf[0] = data;
            return new Token(TokenType.Data, buf, 0, 1);
        }

        public static Token Command(IAC command)
        {
            Byte[] buf = new Byte[1];
            buf[0] = (Byte)command;
            return new Token(TokenType.Command, buf, 0, 1);
        }

        public static Token NOP(IAC command)
        {
            Byte[] buf = new Byte[1];
            buf[0] = (Byte)command;
            return new Token(TokenType.NOP, buf, 0, 1);
        }

        public static Token DO(Telnet.Option option)
        {
            return Token.DO((Byte)option);
        }

        public static Token DO(Byte option)
        {
            Byte[] buf = new Byte[1];
            buf[0] = option;
            return new Token(TokenType.DO, buf, 0, 1);
        }

        public static Token DONT(Telnet.Option option)
        {
            return Token.DONT((Byte)option);
        }

        public static Token DONT(Byte option)
        {
            Byte[] buf = new Byte[1];
            buf[0] = option;
            return new Token(TokenType.DONT, buf, 0, 1);
        }

        public static Token WILL(Telnet.Option option)
        {
            return Token.WILL((Byte)option);
        }

        public static Token WILL(Byte option)
        {
            Byte[] buf = new Byte[1];
            buf[0] = option;
            return new Token(TokenType.WILL, buf, 0, 1);
        }

        public static Token WONT(Telnet.Option option)
        {
            return Token.WONT((Byte)option);
        }

        public static Token WONT(Byte option)
        {
            Byte[] buf = new Byte[1];
            buf[0] = option;
            return new Token(TokenType.WONT, buf, 0, 1);
        }

        public static Token SB(Byte option, Byte[] data, Int32 offset, Int32 count)
        {
            Byte[] buf = new Byte[count + 1];
            Int32 n = 0;
            buf[n++] = option;
            for (Int32 i = 0; i < count; i++) buf[n++] = data[offset + i];
            return new Token(TokenType.SB, buf, 0, n);
        }

        public static Token SB(Byte option, Byte command, String data)
        {
            Int32 n = 2;
            if (command == (Byte)IAC.IAC) n++;
            n += Encoding.ASCII.GetByteCount(data);
            Byte[] buf = new Byte[n];
            n = 0;
            buf[n++] = option;
            buf[n++] = command;
            if (command == (Byte)IAC.IAC) buf[n++] = command;
            n += Encoding.ASCII.GetBytes(data, 0, data.Length, buf, n);
            return new Token(TokenType.SB, buf, 0, n);
        }

        private TokenType mType;
        internal Byte[] mBuf;
        internal Int32 mPtr;
        internal Int32 mLen;
        private Object mLock;

        public Token(TokenType type, Byte[] buf, Int32 ptr, Int32 len)
        {
            mType = type;
            mBuf = buf;
            mPtr = ptr;
            mLen = len;
            mLock = new Object();
        }

        public Byte this[Int32 index]
        {
            get
            {
                lock (mLock)
                {
                    if ((index < 0) || (index >= mLen)) throw new ArgumentOutOfRangeException("index");
                    return mBuf[mPtr + index];
                }
            }
        }

        public TokenType Type
        {
            get { return mType; }
        }

        public Int32 Length
        {
            get { lock (mLock) return mLen; }
        }

        public Int32 ReadByte()
        {
            lock (mLock)
            {
                if (mLen == 0) return -1;
                mLen--;
                return mBuf[mPtr++];
            }
        }
    }
}
