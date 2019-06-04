// Telnet.cs
// Copyright (c) 2016, 2017, 2019 Kenneth Gober
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
using System.Text;
using System.Net.Sockets;

namespace Emulator
{
    // The Telnet class implements the 'top half' of the Telnet protocol.
    // It manages virtual terminal state (including local echo, editing and flow control) and Telnet option state.
    // It sends and receives streams of tokens on the network (via the 'bottom half' TelnetSocket class).

    // Telnet Protocol - Internet Standards:
    //   RFC 854 - Telnet Protocol Specification
    //   RFC 855 - Telnet Option Specifications
    //   RFC 856 - Telnet Binary Transmission
    //   RFC 857 - Telnet Echo Option
    //   RFC 858 - Telnet Suppress Go Ahead Option
    //   RFC 859 - Telnet Status Option
    //   RFC 860 - Telnet Timing Mark Option
    //   RFC 861 - Telnet Extended Options: List Option
    // Telnet Protocol - Proposed Standards:
    //   RFC 885 - Telnet End of Record Option
    //   RFC 1073 - Telnet Window Size Option
    //   RFC 1079 - Telnet Terminal Speed Option
    //   RFC 1091 - Telnet Terminal-Type Option
    //   RFC 1184 - Telnet Linemode Option
    //   RFC 1372 - Telnet Remote Flow Control Option

    // Future Improvements / To Do
    // make it more apparent that term type and speed must be set before option negotiation occurs
    // SetWindowSize may be called any time, make it thread-safe
    // properly handle incoming SYNCH
    // send GA if we are not in SGA mode (maybe)
    // Binary option (specifically its change to CR handling, see RFC 1123 3.2.7)
    // if we WILL SGA, but we don't ECHO, does server think our side is in kludge linemode?
    // what is the correct response to DO TM DO TM?  one WILL TM or two?
    // support RFC 1184 SOFT_TAB mode?
    // support RFC 1184 FORWARDMASK
    // properly handle tabs during line editing (particularly deleting tabs)


    // Telnet Public Interface

    partial class Telnet
    {
        public enum vk : short
        {
            NONE = -1,
            NUL = 0,
            BEL = 7,
            BS = 8,
            HT = 9,
            LF = 10,
            VT = 11,
            FF = 12,
            CR = 13,
            BRK = 256,
            IP = 257,
            AO = 258,
            AYT = 259,
            EC = 260,
            EL = 261,
            EOR = 262,
            EOF = 263,
            SUSP = 264,
            ABORT = 265,
            GA = 512,
            SYNCH = 513,
        }

        public delegate void ReceiveEventHandler(Object sender, IOEventArgs e);
        public event ReceiveEventHandler Receive;

        private TcpClient mTcpClient;
        private TelnetSocket mSocket;
        private Queue<Int16> mRecvQueue = new Queue<Int16>();
        private Boolean mRecvHold;
        private Boolean mRecvFlush;
        private Boolean mAutoFlush;

        private List<String> mTermTypes = new List<String>();
        private Int32 mTermTypeIndex = 0;
        private UInt16 mTermWidth = 0;
        private UInt16 mTermHeight = 0;
        private Int32 mTermTransmit = 0;
        private Int32 mTermReceive = 0;

        public Telnet()
        {
        }

        public Telnet(String destination)
        {
            Connect(destination);
        }

        public Telnet(String host, Int32 port)
        {
            Connect(host, port);
        }

        public Boolean AutoFlush
        {
            get { return mAutoFlush; }
            set { mAutoFlush = value; }
        }

        public Boolean DataAvailable
        {
            get { return (mRecvQueue.Count != 0); }
        }

        public void SetTerminalType(params String[] values)
        {
            foreach (String s in values) mTermTypes.Add(s);
        }

        public void SetTerminalSpeed(Int32 receiveBaud, Int32 transmitBaud)
        {
            mTermReceive = receiveBaud;
            mTermTransmit = transmitBaud;
        }

        public void SetWindowSize(UInt16 width, UInt16 height)
        {
            if ((width == mTermWidth) && (height == mTermHeight)) return;
            mTermWidth = width;
            mTermHeight = height;
            if ((mSocket != null) && (Self(Option.NAWS)))
            {
                Byte[] buf = new Byte[4];
                Int32 n = 0;
                n += BufWrite(buf, n, mTermWidth);
                n += BufWrite(buf, n, mTermHeight);
                mSocket.Write(Token.SB((Byte)Option.NAWS, buf, 0, n));
            }
        }

        public void Connect(String destination)
        {
            String host = destination;
            Int32 port = 23;
            Int32 p = destination.IndexOf(':');
            if (p != -1)
            {
                port = Int32.Parse(destination.Substring(p + 1));
                host = destination.Substring(0, p);
            }
            Connect(host, port);
        }

        public void Connect(String host, Int32 port)
        {
            if (mTcpClient != null) throw new InvalidOperationException("Already connected");
            mTcpClient = new TcpClient(host, port);
            mSocket = new TelnetSocket(mTcpClient.Client);
            mSocket.Receive += new TelnetSocket.ReceiveEventHandler(TelnetSocket_Receive);
            Init();
        }

        public void Close()
        {
            mSocket.Close();
            mSocket = null;
            mTcpClient = null;
        }

        public Int32 Read(Byte[] buffer, Int32 offset, Int32 size)
        {
            Int32 n = 0;
            lock (mRecvQueue)
            {
                if (mRecvFlush)
                {
                    while (mRecvQueue.Count != 0)
                    {
                        while ((mRecvQueue.Count != 0) && (mRecvQueue.Peek() != -1)) mRecvQueue.Dequeue();
                        while ((mRecvQueue.Count != 0) && (mRecvQueue.Peek() == -1))
                        {
                            mRecvQueue.Dequeue();
                            Send_WILL(Option.TIMING_MARK);
                            mSelf[(Int32)Option.TIMING_MARK] = OptionState.OFF;
                        }
                    }
                }
                while ((n < size) && (mRecvQueue.Count != 0) && (!mRecvHold))
                {
                    buffer[offset++] = (Byte)(mRecvQueue.Dequeue());
                    n++;
                    while ((mRecvQueue.Count != 0) && (mRecvQueue.Peek() == -1))
                    {
                        mRecvQueue.Dequeue();
                        Send_WILL(Option.TIMING_MARK);
                        mSelf[(Int32)Option.TIMING_MARK] = OptionState.OFF;
                    }
                }
            }
            return n;
        }

        public Int32 ReadByte()
        {
            lock (mRecvQueue)
            {
                if (mRecvFlush)
                {
                    while (mRecvQueue.Count != 0)
                    {
                        while ((mRecvQueue.Count != 0) && (mRecvQueue.Peek() != -1)) mRecvQueue.Dequeue();
                        while ((mRecvQueue.Count != 0) && (mRecvQueue.Peek() == -1))
                        {
                            mRecvQueue.Dequeue();
                            Send_WILL(Option.TIMING_MARK);
                            mSelf[(Int32)Option.TIMING_MARK] = OptionState.OFF;
                        }
                    }
                }
                if ((mRecvQueue.Count == 0) || (mRecvHold)) return -1;
                Int32 n = (Byte)(mRecvQueue.Dequeue());
                while ((mRecvQueue.Count != 0) && (mRecvQueue.Peek() == -1))
                {
                    mRecvQueue.Dequeue();
                    Send_WILL(Option.TIMING_MARK);
                    mSelf[(Int32)Option.TIMING_MARK] = OptionState.OFF;
                }
                return n;
            }
        }

        public void Write(Byte[] buffer, Int32 offset, Int32 size)
        {
            for (Int32 i = 0; i < size; i++) Send(buffer[offset + i]);
            if (mAutoFlush) Flush();
        }

        public void Write(Byte value)
        {
            Send(value);
            if (mAutoFlush) Flush();
        }

        public void Write(vk value)
        {
            Send(value);
            if (mAutoFlush) Flush();
        }

        public void Break()
        {
            SendBreak();
            if (mAutoFlush) Flush();
        }

        public void Flush()
        {
            mSocket.Flush();
        }
    }


    // Virtual Terminal State

    partial class Telnet
    {
        private enum Mode : byte
        {
            NVT = 0,
            Char,
            Edit
        }

        private struct KeyAction
        {
            public SLCfn Function;      // function of this key
            public Boolean FlushInput;  // whether key should discard input pending in line buffer
            public Boolean FlushOutput; // whether key should discard output pending in receive buffer
        }

        private Mode mMode;
        private Byte[] mLineBuf = new Byte[1024];
        private Int32 mLinePtr;
        private Int32 mLineEnd;
        private Int32 mLineOffset;
        private Boolean mLineInsert;
        private Boolean mLiteralNext;
        private KeyAction[] mKeyMap = new KeyAction[128];
        private LinemodeMode mLinemodeMode;
        private Boolean mOptTrapSig;
        private Boolean mOptEchoLiteral;
        private Boolean mOptFlowEnable;
        private Boolean mOptFlowRestartAny;

        private void Init()
        {
            mMode = Mode.NVT;
            mLinePtr = 0;
            mLineEnd = 0;
            mLineOffset = 0;
            mLineInsert = true;
            mRecvHold = false;
            for (Int32 i = 0; i < mKeyMap.Length; i++) mKeyMap[i].Function = SLCfn.NONE;
            mKeyMap['H' - '@'].Function = SLCfn.EC;
            mKeyMap['Q' - '@'].Function = SLCfn.XON;
            mKeyMap['S' - '@'].Function = SLCfn.XOFF;
            mKeyMap['U' - '@'].Function = SLCfn.EL;

            InitOptions();
        }

        private void SendBreak()
        {
            if (mMode != Mode.Char) FlushLine();
            mSocket.Write(Token.Command(IAC.BRK));
        }

        private void Send(vk data)
        {
            if ((data >= 0) && (data < vk.BRK))
            {
                Send((Byte)data);
                return;
            }
            switch (data)
            {
                case vk.BRK:
                    FlushLine();
                    mSocket.Write(Token.Command(IAC.BRK));
                    break;
                case vk.IP:
                    FlushLine();
                    mSocket.Write(Token.Command(IAC.IP));
                    break;
                case vk.AO:
                    FlushLine();
                    mSocket.Write(Token.Command(IAC.AO));
                    break;
                case vk.AYT:
                    FlushLine();
                    mSocket.Write(Token.Command(IAC.AYT));
                    break;
                case vk.EC:
                    FlushLine();
                    mSocket.Write(Token.Command(IAC.EC));
                    break;
                case vk.EL:
                    FlushLine();
                    mSocket.Write(Token.Command(IAC.EL));
                    break;
                case vk.EOR:
                    if (Self(Option.END_OF_RECORD))
                    {
                        FlushLine();
                        mSocket.Write(Token.Command(IAC.EOR));
                    }
                    break;
                case vk.EOF:
                    if (Self(Option.LINEMODE))
                    {
                        FlushLine();
                        mSocket.Write(Token.Command(IAC.EOF));
                    }
                    break;
                case vk.SUSP:
                    if (Self(Option.LINEMODE))
                    {
                        FlushLine();
                        mSocket.Write(Token.Command(IAC.SUSP));
                    }
                    break;
                case vk.ABORT:
                    if (Self(Option.LINEMODE))
                    {
                        FlushLine();
                        mSocket.Write(Token.Command(IAC.ABORT));
                    }
                    break;
                case vk.GA:
                    if (!Self(Option.SUPPRESS_GO_AHEAD))
                    {
                        FlushLine();
                        mSocket.Write(Token.Command(IAC.GA));
                    }
                    break;
                case vk.SYNCH:
                    FlushLine();
                    mSocket.Write(Token.Command(IAC.DM));
                    break;
            }
        }

        private void Send(Byte data)
        {
            // flow control
            if (mRecvHold)
            {
                if ((mKeyMap[data].Function == SLCfn.XON) && (!mLiteralNext))
                {
                    lock (mRecvQueue) mRecvHold = false;
                    return;
                }
                if (mOptFlowRestartAny)
                {
                    lock (mRecvQueue) mRecvHold = false;
                }
            }
            else if ((mOptFlowEnable) && (mKeyMap[data].Function == SLCfn.XOFF) && (!mLiteralNext))
            {
                lock (mRecvQueue) mRecvHold = true;
                return;
            }

            // signal trapping
            if ((mOptTrapSig) && (!mLiteralNext))
            {
                if (DoTrap(data)) return;
            }

            // raw mode
            if (mMode == Mode.Char)
            {
                mSocket.Write(Token.Data(data));
                if (data == 13) mSocket.Write(Token.Data(0));
                if (!Dest(Option.ECHO)) DoEcho(data);
                if (mLiteralNext) mLiteralNext = false;
                return;
            }

            // edit mode
            if ((data >= 32) && (data < 127) || (mLiteralNext)) // printable character
            {
                if ((mLineInsert) && (mLineEnd == mLineBuf.Length))
                {
                    // TODO: grow buffer
                    // for now, ignore input
                    lock (mRecvQueue) mRecvQueue.Enqueue(7);
                    if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
                    return;
                }
                if (mLineInsert)
                {
                    for (Int32 i = mLineEnd; i > mLinePtr; i--) mLineBuf[i] = mLineBuf[i - 1];
                    mLineEnd++;
                }
                mLineBuf[mLinePtr++] = data;
                if (!Dest(Option.ECHO))
                {
                    lock (mRecvQueue) mRecvQueue.Enqueue(data);
                    if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
                }
                if (mLiteralNext) mLiteralNext = false;
            }
            else if (data == 13)
            {
                if (mLineEnd > 0) mSocket.Write(Token.Data(mLineBuf, 0, mLineEnd));
                mLineEnd = 0;
                mLinePtr = 0;
                mLineOffset = 0;
                mLineBuf[0] = 13;
                mLineBuf[1] = 10;
                mSocket.Write(Token.Data(mLineBuf, 0, 2));
                if (!Dest(Option.ECHO))
                {
                    lock (mRecvQueue)
                    {
                        mRecvQueue.Enqueue(13);
                        mRecvQueue.Enqueue(10);
                    }
                    if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
                }
            }
            else if (mKeyMap[data].Function == SLCfn.EC) // EC - erase character
            {
                if (mLinePtr > 0)
                {
                    mLinePtr--;
                    if (mLineEnd > 0) mLineEnd--;
                    if (mLineEnd > mLinePtr)
                        for (Int32 i = mLinePtr; i < mLineEnd; i++) mLineBuf[i] = mLineBuf[i + 1];
                    if (!Dest(Option.ECHO))
                    {
                        lock (mRecvQueue)
                        {
                            mRecvQueue.Enqueue(8);
                            for (Int32 i = mLinePtr; i < mLineEnd; i++) mRecvQueue.Enqueue(mLineBuf[i]);
                            mRecvQueue.Enqueue(32);
                            for (Int32 i = mLinePtr; i < mLineEnd; i++) mRecvQueue.Enqueue(8);
                            mRecvQueue.Enqueue(8);
                        }
                        if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
                    }
                }
            }
            else if (mKeyMap[data].Function == SLCfn.EL) // EL - erase line
            {
                if (mLineEnd > 0)
                {
                    if (!Dest(Option.ECHO))
                    {
                        lock (mRecvQueue)
                        {
                            for (Int32 i = 0; i < mLineEnd; i++) mRecvQueue.Enqueue(8);
                            mRecvQueue.Enqueue(27);
                            mRecvQueue.Enqueue((Int16)'K');
                        }
                        if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
                    }
                    mLineEnd = 0;
                    mLinePtr = 0;
                }
            }
            // EW - erase word
            // RP - reprint line
            else if (mKeyMap[data].Function == SLCfn.LNEXT) // LNEXT - literal next
            {
                mLiteralNext = true;
            }
            else if ((mKeyMap[data].Function == SLCfn.FORW1) // FORW1 - forwarding character
             || (mKeyMap[data].Function == SLCfn.FORW2)) // FORW2 - forwarding character
            {
                if ((mLineInsert) && (mLineEnd == mLineBuf.Length))
                {
                    mSocket.Write(Token.Data(mLineBuf, 0, mLineEnd));
                    mLineOffset += mLineEnd;
                    mLineEnd = 0;
                    mLinePtr = 0;
                }
                if (mLineInsert)
                {
                    for (Int32 i = mLineEnd; i > mLinePtr; i--) mLineBuf[i] = mLineBuf[i - 1];
                    mLineEnd++;
                }
                mLineBuf[mLinePtr++] = data;
                if (!Dest(Option.ECHO))
                {
                    lock (mRecvQueue) mRecvQueue.Enqueue(data);
                    if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
                }
                mSocket.Write(Token.Data(mLineBuf, 0, mLineEnd));
                mLineOffset += mLineEnd;
                mLineEnd = 0;
                mLinePtr = 0;
            }
            // MCL - move cursor one character left
            // MCR - move cursor one character right
            // MCWL - move cursor one word left
            // MCWR - move cursor one word right
            // MCBOL - move cursor to beginning of line
            // MCEOL - move cursor to end of line
            // INSRT - enter insert mode
            // OVER - enter overstrike mode
            // ECR - erase character to right
            // EWR - erase word to right
            // EBOL - erase to beginning of line
            // EEOL - erase to end of line
        }

        // DoTrap returns true if char is locally processed (i.e. should not be sent to host)
        private Boolean DoTrap(Byte data)
        {
            switch (mKeyMap[data].Function)
            {
                case SLCfn.NONE: return false;
                case SLCfn.SYNCH: mSocket.Write(Token.DM); break;
                case SLCfn.BRK: mSocket.Write(Token.Command(IAC.BRK)); break;
                case SLCfn.IP: mSocket.Write(Token.Command(IAC.IP)); break;
                case SLCfn.AO: mSocket.Write(Token.Command(IAC.AO)); break;
                case SLCfn.AYT: mSocket.Write(Token.Command(IAC.AYT)); break;
                case SLCfn.EOR: mSocket.Write(Token.Command(IAC.EOR)); break;
                case SLCfn.ABORT: mSocket.Write(Token.Command(IAC.ABORT)); break;
                case SLCfn.EOF: mSocket.Write(Token.Command(IAC.EOF)); break;
                case SLCfn.SUSP: mSocket.Write(Token.Command(IAC.SUSP)); break;
                case SLCfn.LNEXT: mLiteralNext = true; return true;
                default: return false;
            }
            if (mKeyMap[data].FlushInput)
            {
                if (mLineEnd > 0)
                {
                    mSocket.Write(Token.Data(mLineBuf, 0, mLineEnd));
                    mLineOffset += mLineEnd;
                    mLineEnd = 0;
                    mLinePtr = 0;
                }
                if (mKeyMap[data].Function != SLCfn.SYNCH) mSocket.Write(Token.DM);
            }
            if (mKeyMap[data].FlushOutput)
            {
                mSocket.Write(Token.DO(Option.TIMING_MARK));
                lock (mRecvQueue)
                {
                    mRecvFlush = true;
                    while (mRecvQueue.Count != 0)
                    {
                        while ((mRecvQueue.Count != 0) && (mRecvQueue.Peek() != -1)) mRecvQueue.Dequeue();
                        while ((mRecvQueue.Count != 0) && (mRecvQueue.Peek() == -1))
                        {
                            mRecvQueue.Dequeue();
                            Send_WILL(Option.TIMING_MARK);
                            mSelf[(Int32)Option.TIMING_MARK] = OptionState.OFF;
                        }
                    }
                }
                if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Flush, 0));
            }
            if (!Dest(Option.ECHO)) DoEcho(data);
            return true;
        }

        private void DoEcho(Byte data)
        {
            if (mOptEchoLiteral)
            {
                lock (mRecvQueue) mRecvQueue.Enqueue(data);
                if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
                return;
            }

            if ((data >= 32) && (data < 127))
            {
                lock (mRecvQueue) mRecvQueue.Enqueue(data);
                if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
            }
            else if ((data == 8) || (data == 9) || (data == 10) || (data == 13))
            {
                lock (mRecvQueue) mRecvQueue.Enqueue(data);
                if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
            }
            else if (data < 32)
            {
                lock (mRecvQueue)
                {
                    mRecvQueue.Enqueue((Int16)'^');
                    mRecvQueue.Enqueue((Int16)(data + 64));
                }
                if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
            }
            else if (data == 127)
            {
                lock (mRecvQueue)
                {
                    mRecvQueue.Enqueue((Int16)'^');
                    mRecvQueue.Enqueue((Int16)'?');
                }
                if (Receive != null) Receive(this, new IOEventArgs(IOEventType.Data, 0));
            }
        }

        private void FlushLine()
        {
            if (mLineEnd > 0) mSocket.Write(Token.Data(mLineBuf, 0, mLineEnd));
            mLineEnd = 0;
            mLinePtr = 0;
        }

        private Boolean Recv_Data(Token t)
        {
            Debug.WriteLine("Recv data: {0} ({1:D0} bytes)", Encoding.ASCII.GetString(t.mBuf, t.mPtr, t.mLen), t.mLen);
            lock (mRecvQueue)
            {
                if (mRecvFlush) return false;
                for (Int32 i = 0; i < t.mLen; i++)
                {
                    Byte b = t.mBuf[t.mPtr + i];
                    mRecvQueue.Enqueue(b);
                    if (mMode != Mode.Edit) continue;
                    if ((b >= 32) && (b < 127)) mLineOffset++;
                    else if (b == 13) mLineOffset = 0;
                    else if (b == 8) mLineOffset--;
                    if (mLineOffset < 0) mLineOffset = 0;
                }
            }
            return true;
        }

        private Boolean Recv_Command(Token t)
        {
            IAC c = (IAC)t[0];
            Debug.WriteLine("Recv IAC {0} ({1:D0})", c, t[0]);
            if ((c == IAC.BRK) && (Receive != null)) Receive(this, new IOEventArgs(IOEventType.Break, 0));
            if ((c == IAC.EOR) && (!Dest(Option.END_OF_RECORD))) c = IAC.NOP;
            // we aren't capable of receiving commands, so ignore them
            return false;
            //vk k = vk.NONE;
            //switch (c)
            //{
            //    case IAC.GA: k = vk.GA; break;
            //    case IAC.BRK: k = vk.BRK; break;
            //    case IAC.IP: k = vk.IP; break;
            //    case IAC.AO: k = vk.AO; break;
            //    case IAC.AYT: k = vk.AYT; break;
            //    case IAC.EC: k = vk.EC; break;
            //    case IAC.EL: k = vk.EL; break;
            //    case IAC.EOR: k = vk.EOR; break;
            //    case IAC.EOF: k = vk.EOF; break;
            //    case IAC.SUSP: k = vk.SUSP; break;
            //    case IAC.ABORT: k = vk.ABORT; break;
            //}
            //if (k != vk.NONE) lock (mRecvQueue) mRecvQueue.Enqueue(k);
            //return (k != vk.NONE);
        }
    }


    // Option Negotiation

    partial class Telnet
    {
        internal enum Option : byte
        {
            TRANSMIT_BINARY = 0,
            ECHO = 1,
            SUPPRESS_GO_AHEAD = 3,
            STATUS = 5,
            TIMING_MARK = 6,
            TERMINAL_TYPE = 24,
            END_OF_RECORD = 25,
            NAWS = 31,
            TERMINAL_SPEED = 32,
            TOGGLE_FLOW_CONTROL = 33,
            LINEMODE = 34,
            X_DISPLAY_LOCATION = 35,
            ENVIRON = 36,
            NEW_ENVIRON = 39,
            EXOPL = 255,
        }

        private enum OptionState
        {
            OFF = 0,
            OFF_WILL = 1,
            OFF_DO = 2,
            ON_DONT = 3,
            ON_WONT = 4,
            ON = 5,
        }

        [Flags]
        private enum LinemodeMode : byte
        {
            EDIT = 0x01,
            TRAPSIG = 0x02,
            MODE_ACK = 0x04,
            SOFT_TAB = 0x08,
            LIT_ECHO = 0x10,
        }

        private enum SLCfn : byte
        {
            SYNCH = 1,
            BRK = 2,
            IP = 3,
            AO = 4,
            AYT = 5,
            EOR = 6,
            ABORT = 7,
            EOF = 8,
            SUSP = 9,
            EC = 10,
            EL = 11,
            EW = 12,
            RP = 13,
            LNEXT = 14,
            XON = 15,
            XOFF = 16,
            FORW1 = 17,
            FORW2 = 18,
            MCL = 19,
            MCR = 20,
            MCWL = 21,
            MCWR = 22,
            MCBOL = 23,
            MCEOL = 24,
            INSRT = 25,
            OVER = 26,
            ECR = 27,
            EWR = 28,
            EBOL = 29,
            EEOL = 30,
            NONE = 255,
        }

        [Flags]
        private enum SLCmod : byte
        {
            NOSUPPORT = 0,
            CANTCHANGE = 1,
            VALUE = 2,
            LEVELBITS = 3,
            DEFAULT = 3,
            FLUSHOUT = 32,
            FLUSHIN = 64,
            ACK = 128,
        }

        private struct SLC
        {
            public SLCfn fn;
            public SLCmod mod;
            public Byte ch;

            public SLC(SLCfn function, SLCmod modifier, Byte character)
            {
                fn = function;
                mod = modifier;
                ch = character;
            }
        }

        private OptionState[] mSelf = new OptionState[64];
        private OptionState[] mDest = new OptionState[64];
        private SLC[] mSLC = new SLC[31];

        private void InitOptions()
        {
            for (Int32 i = 0; i < mSelf.Length; i++) mSelf[i] = OptionState.OFF;
            for (Int32 i = 0; i < mDest.Length; i++) mDest[i] = OptionState.OFF;
            for (Int32 i = 0; i < mSLC.Length; i++) mSLC[i] = new SLC(SLCfn.NONE, SLCmod.NOSUPPORT, 0);
            mSLC[(Int32)SLCfn.EC] = new SLC(SLCfn.EC, SLCmod.VALUE, 8);
            mSLC[(Int32)SLCfn.XON] = new SLC(SLCfn.XON, SLCmod.VALUE, 17);
            mSLC[(Int32)SLCfn.XOFF] = new SLC(SLCfn.XOFF, SLCmod.VALUE, 19);
            mLinemodeMode = 0;

            // would like character-at-a-time mode
            //Send_DO(Option.ECHO);
            //Send_DO(Option.SUPPRESS_GO_AHEAD);

            // some hosts think GA's are user input to be echoed
            //Send_WILL(Option.SUPPRESS_GO_AHEAD);
        }

        private Boolean Self(Option option)
        {
            return (mSelf[(Int32)option] >= OptionState.ON_DONT);
        }

        private Boolean Dest(Option option)
        {
            return (mDest[(Int32)option] >= OptionState.ON_DONT);
        }

        private Boolean Recv_Option(Token t)
        {
            Byte o = t[0];
            Option opt = (Option)o;
            if (t.Type == TokenType.DO)
            {
                Debug.WriteLine("Recv IAC DO {0} ({1:D0})", opt, o);
                Recv_DO(opt);
                switch (opt)
                {
                    case Option.TIMING_MARK: // DO TIMING-MARK (6)
                        mSelf[o] = OptionState.OFF;
                        lock (mRecvQueue)
                        {
                            if (mRecvQueue.Count != 0)
                            {
                                mRecvQueue.Enqueue(-1);
                                break;
                            }
                        }
                        Send_WILL(Option.TIMING_MARK);
                        mSelf[o] = OptionState.OFF;
                        break;
                    case Option.NAWS: // DO NAWS (31)
                        if (mSelf[o] == OptionState.OFF_DO) Send_WILL(opt);
                        if (mSelf[o] == OptionState.ON)
                        {
                            Byte[] buf = new Byte[4];
                            Int32 n = 0;
                            n += BufWrite(buf, n, mTermWidth);
                            n += BufWrite(buf, n, mTermHeight);
                            mSocket.Write(Token.SB(o, buf, 0, n));
                        }
                        break;
                    case Option.TERMINAL_SPEED: // DO TERMINAL-SPEED (32)
                        if (mSelf[o] == OptionState.OFF_DO)
                        {
                            if ((mTermTransmit > 0) && (mTermReceive > 0))
                            {
                                Send_WILL(opt);
                            }
                            else
                            {
                                Send_WONT(opt);
                            }
                        }
                        break;
                    case Option.TOGGLE_FLOW_CONTROL: // DO TOGGLE-FLOW-CONTROL (33)
                        if (mSelf[o] == OptionState.OFF_DO)
                        {
                            Send_WILL(opt);
                            mOptFlowEnable = true;
                        }
                        break;
                    case Option.LINEMODE: // DO LINEMODE (34)
                        if (mSelf[o] == OptionState.OFF_DO)
                        {
                            Send_WILL(opt);
                            mMode = Mode.Char;
                            mOptTrapSig = false;
                            mOptEchoLiteral = false;
                            for (Int32 i = 0; i < mKeyMap.Length; i++) mKeyMap[i].FlushInput = false;
                            for (Int32 i = 0; i < mSLC.Length; i++) mSLC[i].mod = SLCmod.NOSUPPORT;
                            Byte[] buf = new Byte[4];
                            buf[0] = 3; // SLC
                            buf[1] = 0;
                            buf[2] = (Byte)SLCmod.DEFAULT;
                            buf[3] = 0;
                            mSocket.Write(Token.SB(o, buf, 0, 4));
                        }
                        break;
                    case Option.TRANSMIT_BINARY: // DO TRANSMIT-BINARY (0)
                    case Option.SUPPRESS_GO_AHEAD: // DO SUPPRESS-GO-AHEAD (3)
                    case Option.STATUS: // DO STATUS (5)
                    case Option.TERMINAL_TYPE: // DO TERMINAL-TYPE (24)
                    case Option.END_OF_RECORD: // DO END-OF-RECORD (25)
                        if (mSelf[o] == OptionState.OFF_DO) Send_WILL(opt);
                        break;
                    default:
                        if (mSelf[o] == OptionState.OFF_DO) Send_WONT(opt);
                        break;
                }
                return false;
            }
            if (t.Type == TokenType.DONT)
            {
                Debug.WriteLine("Recv IAC DONT {0} ({1:D0})", opt, o);
                Recv_DONT(opt);
                switch (opt)
                {
                    case Option.TOGGLE_FLOW_CONTROL: // DONT TOGGLE-FLOW-CONTROL (33)
                        if (mSelf[o] == OptionState.ON_DONT)
                        {
                            Send_WONT(opt);
                            mOptFlowEnable = false;
                        }
                        break;
                    default:
                        if (mSelf[o] == OptionState.ON_DONT) Send_WONT(opt);
                        break;
                }
                return false;
            }
            if (t.Type == TokenType.WILL)
            {
                Debug.WriteLine("Recv IAC WILL {0} ({1:D0})", opt, o);
                Recv_WILL(opt);
                switch (opt)
                {
                    case Option.SUPPRESS_GO_AHEAD: // WILL SUPPRESS-GO-AHEAD (3)
                        if (mDest[o] == OptionState.OFF_WILL) Send_DO(opt);
                        if (!Self(Option.LINEMODE))
                        {
                            FlushLine();
                            mMode = Mode.Char;
                        }
                        break;
                    case Option.TIMING_MARK: // WILL TIMING-MARK (6)
                        mDest[o] = OptionState.OFF;
                        lock (mRecvQueue) mRecvFlush = false;
                        break;
                    case Option.TRANSMIT_BINARY: // WILL TRANSMIT-BINARY (0)
                    case Option.ECHO: // WILL ECHO (1)
                    case Option.STATUS: // WILL STATUS (5)
                    case Option.END_OF_RECORD: // WILL END-OF-RECORD (25)
                        if (mDest[o] == OptionState.OFF_WILL) Send_DO(opt);
                        break;
                    default:
                        if (mDest[o] == OptionState.OFF_WILL) Send_DONT(opt);
                        break;
                }
                return false;
            }
            if (t.Type == TokenType.WONT)
            {
                Debug.WriteLine("Recv IAC WONT {0} ({1:D0})", opt, o);
                Recv_WONT(opt);
                switch (opt)
                {
                    case Option.SUPPRESS_GO_AHEAD: // WONT SUPPRESS-GO-AHEAD (3)
                        if (mDest[o] == OptionState.ON_WONT) Send_DONT(opt);
                        if (!Self(Option.LINEMODE)) mMode = Mode.NVT;
                        break;
                    case Option.TIMING_MARK: // WONT TIMING-MARK (6)
                        mDest[o] = OptionState.OFF;
                        lock (mRecvQueue) mRecvFlush = false;
                        break;
                    default:
                        if (mDest[o] == OptionState.ON_WONT) Send_DONT(opt);
                        break;
                }
                return false;
            }
            throw new ApplicationException("invalid option");
        }

        private Boolean Recv_Suboption(Token t)
        {
            Byte o = t[0];
            Option opt = (Option)o;
            Debug.WriteLine("Recv IAC SB {0} ({1:D0})", opt, o);
            switch (opt)
            {
                case Option.STATUS: // 5
                    if (!Self(Option.STATUS)) break;
                    if (t[1] == 1)  // SEND
                    {
                        Debug.WriteLine("Sub-Option STATUS SEND");
                        Byte[] buf = new Byte[(mSelf.Length + mDest.Length) * 2 + 1];
                        Int32 n = 0;
                        buf[n++] = 0;
                        Byte i = 0;
                        while ((i < mSelf.Length) || (i < mDest.Length))
                        {
                            if ((i < mSelf.Length) && Self((Option)i))
                            {
                                n += BufWrite(buf, n, (Byte)IAC.WILL);
                                n += BufWrite(buf, n, i);
                            }
                            if ((i < mDest.Length) && Dest((Option)i))
                            {
                                n += BufWrite(buf, n, (Byte)IAC.DO);
                                n += BufWrite(buf, n, i);
                            }
                        }
                        mSocket.Write(Token.SB(o, buf, 0, n));
                    }
                    break;

                case Option.TERMINAL_TYPE: // 24
                    if (!Self(Option.TERMINAL_TYPE)) break;
                    if (t[1] == 1)  // SEND
                    {
                        Debug.WriteLine("Sub-Option TERMINAL-TYPE SEND");
                        if (mTermTypes.Count == 0)
                        {
                            mSocket.Write(Token.SB(o, 0, "UNKNOWN"));
                        }
                        else if (mTermTypeIndex == mTermTypes.Count)
                        {
                            // send last entry twice to signal end-of-list reached
                            mSocket.Write(Token.SB(o, 0, mTermTypes[mTermTypeIndex - 1]));
                            mTermTypeIndex = 0;
                        }
                        else
                        {
                            mSocket.Write(Token.SB(o, 0, mTermTypes[mTermTypeIndex++]));
                        }
                    }
                    break;

                case Option.TERMINAL_SPEED: // 32
                    if (!Self(Option.TERMINAL_SPEED)) break;
                    if (t[1] == 1)  // SEND
                    {
                        Debug.WriteLine("Sub-Option TERMINAL-SPEED SEND");
                        mSocket.Write(Token.SB(o, 0, String.Format("{0:D0},{1:D0}", mTermReceive, mTermTransmit)));
                    }
                    break;

                case Option.TOGGLE_FLOW_CONTROL:
                    if (!Self(Option.TOGGLE_FLOW_CONTROL)) break;
                    if (t[1] == 0) // OFF
                    {
                        Debug.WriteLine("Sub-Option TOGGLE-FLOW-CONTROL OFF");
                        mOptFlowEnable = false;
                    }
                    else if (t[1] == 1) // ON
                    {
                        Debug.WriteLine("Sub-Option TOGGLE-FLOW-CONTROL ON");
                        mOptFlowEnable = true;
                    }
                    else if (t[1] == 2) // RESTART-ANY
                    {
                        Debug.WriteLine("Sub-Option TOGGLE-FLOW-CONTROL RESTART-ANY");
                        mOptFlowRestartAny = true;
                    }
                    else if (t[1] == 3) // RESTART-XON
                    {
                        Debug.WriteLine("Sub-Option TOGGLE-FLOW-CONTROL RESTART-XON");
                        mOptFlowRestartAny = false;
                    }
                    break;

                case Option.LINEMODE: // 34
                    if (!Self(Option.LINEMODE)) break;
                    if (t[1] == 1)  // MODE mask
                    {
                        LinemodeMode mask = (LinemodeMode)t[2];
                        Debug.WriteLine("Sub-Option LINEMODE MODE {0} ({1:D0})", mask, t[2]);
                        // mask & 0x04 = MODE_ACK
                        if ((mask & LinemodeMode.MODE_ACK) != 0)
                        {
                            // MODE_ACK on - always ignored by client
                            break;
                        }
                        else if (mask == mLinemodeMode)
                        {
                            // MODE_ACK off - ignore if new mode is already in effect
                            break;
                        }
                        else if ((mask & LinemodeMode.SOFT_TAB) != 0)
                        {
                            // MODE_ACK off - SOFT_TAB not supported
                            mask &= ~LinemodeMode.SOFT_TAB;
                            mLinemodeMode = mask;
                            Byte[] buf = new Byte[2];
                            buf[0] = 1;
                            buf[1] = (byte)mask;
                            mSocket.Write(Token.SB(o, buf, 0, 2));
                        }
                        else
                        {
                            // MODE_ACK off - set new mode and send MODE_ACK
                            mLinemodeMode = mask;
                            mask |= LinemodeMode.MODE_ACK;
                            Byte[] buf = new Byte[2];
                            buf[0] = 1;
                            buf[1] = (byte)mask;
                            mSocket.Write(Token.SB(o, buf, 0, 2));
                        }

                        // mask & 0x01 = EDIT
                        if ((mLinemodeMode & LinemodeMode.EDIT) != 0)
                        {
                            // EDIT on - client performs input editing (line mode)
                            mMode = Mode.Edit;
                        }
                        else
                        {
                            // EDIT off - client sends all keyboard input without processing (character mode)
                            FlushLine();
                            mMode = Mode.Char;
                        }

                        // mask & 0x02 = TRAPSIG
                        if ((mLinemodeMode & LinemodeMode.TRAPSIG) != 0)
                        {
                            // TRAPSIG on - client sends signals as telnet IAC commands
                            mOptTrapSig = true;
                        }
                        else
                        {
                            // TRAPSIG off - client sends signals as ASCII codes
                            mOptTrapSig = false;
                        }

                        // mask & 0x10 = LIT_ECHO
                        if ((mLinemodeMode & LinemodeMode.LIT_ECHO) != 0)
                        {
                            // LIT_ECHO on - client echos control characters literally
                            mOptEchoLiteral = true;
                        }
                        else
                        {
                            // LIT_ECHO off - client may echo control characters as a printable sequence
                            mOptEchoLiteral = false;
                        }
                    }
                    else if ((t[1] == (Byte)IAC.DO) && (t[2] == 2)) // DO FORWARDMASK mask0 ... mask31
                    {
                        Debug.WriteLine("Sub-Option LINEMODE DO FORWARDMASK");
                        Int32 p = 2;
                        while (p < t.Length)
                        {
                            Byte b = t[p++];
                            Byte m = 128;
                            for (Int32 i = 0; i < 8; i++)
                            {
                                Debug.WriteLine("FORWARDMASK {0:D0} = {1}", (p - 2) * 8 + i, ((b & m) == 0) ? "OFF" : "ON");
                                m >>= 1;
                            }
                        }
                        Byte[] buf = new Byte[2];
                        buf[0] = (Byte)IAC.WONT;
                        buf[1] = 2;
                        mSocket.Write(Token.SB(o, buf, 0, 2));
                    }
                    else if ((t[1] == (Byte)IAC.DONT) && (t[2] == 2)) // DONT FORWARDMASK
                    {
                        Debug.WriteLine("Sub-Option LINEMODE DONT FORWARDMASK");
                    }
                    else if (t[1] == 3) // SLC
                    {
                        Debug.WriteLine("Sub-Option LINEMODE SLC");
                        Int32 p = 2;
                        while (p < t.Length)
                        {
                            Debug.WriteLine("SLC {0} {1} {2:D0} ({3:D0} {4:D0} {2:D0})", (SLCfn)t[p], (SLCmod)t[p + 1], t[p + 2], t[p], t[p + 1]);
                            p += 3;
                        }
                        Byte[] buf = new Byte[t.Length - 1];
                        buf[0] = 3; // SLC
                        Int32 n = 1;
                        p = 2;
                        while (p < t.Length)
                        {
                            Int32 i = t[p];
                            SLC slc = new SLC((SLCfn)i, (SLCmod)t[p + 1], t[p + 2]);
                            p += 3;
                            if (i >= mSLC.Length) continue;
                            if ((slc.fn == mSLC[i].fn) && ((slc.mod & ~SLCmod.ACK) == mSLC[i].mod) && (slc.ch == mSLC[i].ch))
                            {
                                // ignore SLC command if it's the same as current settings
                                continue;
                            }
                            if ((slc.fn == mSLC[i].fn) && ((slc.mod & SLCmod.LEVELBITS) == (mSLC[i].mod & SLCmod.LEVELBITS)) && (slc.ch != mSLC[i].ch) && ((slc.mod & SLCmod.ACK) != 0))
                            {
                                // accept without reply if same level, different value, and ACK set
                            }
                            else if ((slc.mod & SLCmod.LEVELBITS) == SLCmod.DEFAULT)
                            {
                                // propose something if host doesn't
                                slc.mod &= ~SLCmod.LEVELBITS;
                                switch (slc.fn)
                                {
                                    case SLCfn.EC:
                                        slc.mod |= SLCmod.VALUE;
                                        slc.ch = 8;
                                        break;
                                    case SLCfn.XON:
                                        slc.mod |= SLCmod.VALUE;
                                        slc.ch = 17;
                                        break;
                                    case SLCfn.XOFF:
                                        slc.mod |= SLCmod.VALUE;
                                        slc.ch = 19;
                                        break;
                                    default:
                                        slc.mod |= SLCmod.NOSUPPORT;
                                        slc.ch = 255;
                                        break;
                                }
                                Debug.WriteLine("SLC Reply {0} {1} {2:D0} ({3:D0} {4:D0} {2:D0})", slc.fn, slc.mod, slc.ch, (Byte)slc.fn, (Byte)slc.mod);
                                n += BufWrite(buf, n, (Byte)slc.fn);
                                n += BufWrite(buf, n, (Byte)slc.mod);
                                n += BufWrite(buf, n, slc.ch);
                                mSLC[i] = slc;
                                continue;
                            }
                            else
                            {
                                // send ACK if we accept proposed settings
                                Debug.WriteLine("SLC Reply {0} {1} {2:D0} ({3:D0} {4:D0} {2:D0})", slc.fn, slc.mod | SLCmod.ACK, slc.ch, (Byte)slc.fn, (Byte)(slc.mod | SLCmod.ACK));
                                n += BufWrite(buf, n, (Byte)slc.fn);
                                n += BufWrite(buf, n, (Byte)(slc.mod | SLCmod.ACK));
                                n += BufWrite(buf, n, slc.ch);
                            }
                            if ((mSLC[i].fn != SLCfn.NONE) && ((mSLC[i].mod & SLCmod.LEVELBITS) != SLCmod.NOSUPPORT))
                            {
                                mKeyMap[mSLC[i].ch].Function = SLCfn.NONE;
                                mKeyMap[mSLC[i].ch].FlushInput = false;
                                mKeyMap[mSLC[i].ch].FlushOutput = false;
                            }
                            mSLC[i] = slc;
                            if ((slc.mod & SLCmod.LEVELBITS) != SLCmod.NOSUPPORT)
                            {
                                mKeyMap[slc.ch].Function = slc.fn;
                                mKeyMap[slc.ch].FlushInput = ((slc.mod & SLCmod.FLUSHIN) != 0);
                                mKeyMap[slc.ch].FlushOutput = ((slc.mod & SLCmod.FLUSHOUT) != 0);
                            }
                        }
                        if (n > 1) mSocket.Write(Token.SB(o, buf, 0, n));
                    }
                    break;
            }
            return false;
        }

        private void Recv_WILL(Option option)
        {
            Byte o = (Byte)option;
            switch (mDest[o])
            {
                case OptionState.OFF:
                    mDest[o] = OptionState.OFF_WILL;
                    break;
                case OptionState.OFF_DO:
                case OptionState.ON_DONT:
                case OptionState.ON:
                    mDest[o] = OptionState.ON;
                    break;
                default:
                    throw new ApplicationException("option state error");
            }
        }

        private void Recv_WONT(Option option)
        {
            Byte o = (Byte)option;
            switch (mDest[o])
            {
                case OptionState.ON:
                    mDest[o] = OptionState.ON_WONT;
                    break;
                case OptionState.OFF:
                case OptionState.OFF_DO:
                case OptionState.ON_DONT:
                    mDest[o] = OptionState.OFF;
                    break;
                default:
                    throw new ApplicationException("option state error");
            }
        }

        private void Recv_DO(Option option)
        {
            Byte o = (Byte)option;
            switch (mSelf[o])
            {
                case OptionState.OFF:
                    mSelf[o] = OptionState.OFF_DO;
                    break;
                case OptionState.OFF_WILL:
                case OptionState.ON_WONT:
                case OptionState.ON:
                    mSelf[o] = OptionState.ON;
                    break;
                default:
                    throw new ApplicationException("option state error");
            }
        }

        private void Recv_DONT(Option option)
        {
            Byte o = (Byte)option;
            switch (mSelf[o])
            {
                case OptionState.ON:
                    mSelf[o] = OptionState.ON_DONT;
                    break;
                case OptionState.OFF:
                case OptionState.OFF_WILL:
                case OptionState.ON_WONT:
                    mSelf[o] = OptionState.OFF;
                    break;
                default:
                    throw new ApplicationException("option state error");
            }
        }

        private void Send_WILL(Option option)
        {
            Byte o = (Byte)option;
            switch (mSelf[o])
            {
                case OptionState.OFF:
                    mSocket.Write(Token.WILL(o));
                    mSelf[o] = OptionState.OFF_WILL;
                    break;
                case OptionState.OFF_DO:
                case OptionState.ON_DONT:
                    mSocket.Write(Token.WILL(o));
                    mSelf[o] = OptionState.ON;
                    break;
                default:
                    throw new ApplicationException("option state error");
            }
        }

        private void Send_WONT(Option option)
        {
            Byte o = (Byte)option;
            switch (mSelf[o])
            {
                case OptionState.ON:
                    mSocket.Write(Token.WONT(o));
                    mSelf[o] = OptionState.ON_WONT;
                    break;
                case OptionState.OFF_DO:
                case OptionState.ON_DONT:
                    mSocket.Write(Token.WONT(o));
                    mSelf[o] = OptionState.OFF;
                    break;
                default:
                    throw new ApplicationException("option state error");
            }
        }

        private void Send_DO(Option option)
        {
            Byte o = (Byte)option;
            switch (mDest[o])
            {
                case OptionState.OFF:
                    mSocket.Write(Token.DO(o));
                    mDest[o] = OptionState.OFF_DO;
                    break;
                case OptionState.OFF_WILL:
                case OptionState.ON_WONT:
                    mSocket.Write(Token.DO(o));
                    mDest[o] = OptionState.ON;
                    break;
                default:
                    throw new ApplicationException("option state error");
            }
        }

        private void Send_DONT(Option option)
        {
            Byte o = (Byte)option;
            switch (mDest[o])
            {
                case OptionState.ON:
                    mSocket.Write(Token.DONT(o));
                    mDest[o] = OptionState.ON_DONT;
                    break;
                case OptionState.OFF_WILL:
                case OptionState.ON_WONT:
                    mSocket.Write(Token.DONT(o));
                    mDest[o] = OptionState.OFF;
                    break;
                default:
                    throw new ApplicationException("option state error");
            }
        }

        private Int32 BufWrite(Byte[] buffer, Int32 offset, Byte data)
        {
            buffer[offset++] = data;
            return 1;
        }

        private Int32 BufWrite(Byte[] buffer, Int32 offset, UInt16 data)
        {
            buffer[offset++] = (Byte)((data >> 8) & 0xFF);
            buffer[offset++] = (Byte)(data & 0xFF);
            return 2;
        }

        private Int32 BufWrite(Byte[] buffer, Int32 offset, String data)
        {
            Byte[] dbuf = Encoding.ASCII.GetBytes(data);
            Int32 n = offset;
            for (Int32 i = 0; i < dbuf.Length; i++) n += BufWrite(buffer, n, dbuf[i]);
            return n - offset;
        }
    }


    // Interface to bottom half

    partial class Telnet
    {

        private void TelnetSocket_Receive(object sender, EventArgs e)
        {
            TelnetSocket socket = sender as TelnetSocket;
            Boolean f = false;
            Token t;
            while (((t = socket.Read()).Type != TokenType.None) && (t.Type != TokenType.Closed))
            {
                //StringBuilder buf = new StringBuilder();
                //buf.AppendFormat("Recv token {0} (mPtr={1:D0} mLen={2:D0})", t.Type, t.mPtr, t.mLen);
                //if (t.mBuf != null) for (Int32 i = 0; i < t.mLen; i++) buf.AppendFormat(" {0:D0}", t.mBuf[t.mPtr + i]);
                //Debug.WriteLine(buf.ToString());
                switch (t.Type)
                {
                    case TokenType.Data:
                        f |= Recv_Data(t);
                        break;
                    case TokenType.Command:
                        f |= Recv_Command(t);
                        break;
                    case TokenType.DO:
                    case TokenType.DONT:
                    case TokenType.WILL:
                    case TokenType.WONT:
                        f |= Recv_Option(t);
                        break;
                    case TokenType.SB:
                        f |= Recv_Suboption(t);
                        break;
                    case TokenType.DM:
                    case TokenType.NOP:
                        break;
                }
            }
            if ((f) && (Receive != null)) Receive(this, new IOEventArgs(IOEventType.Data, 0));
            if ((!socket.Connected || t.Type == TokenType.Closed) && (Receive != null)) Receive(this, new IOEventArgs(IOEventType.Disconnect, 0));
        }
    }


    // code parking lot

    partial class Telnet
    {
        private Int32 mWriteState;

        private vk xRead()
        {
            while (true)
            {
                Token t = mSocket.Peek();
                switch (t.Type)
                {
                    case TokenType.None:
                        return vk.NONE;

                    case TokenType.Data:
                        vk c = (vk)(t.ReadByte());
                        if (t.Length == 0) mSocket.Read();
                        return c;

                    case TokenType.Command:
                        mSocket.Read();
                        switch ((IAC)t[0])
                        {
                            case IAC.BRK: return vk.BRK;
                            case IAC.IP: return vk.IP;
                            case IAC.AO: return vk.AO;
                            case IAC.AYT: return vk.AYT;
                            case IAC.EC: return vk.EC;
                            case IAC.EL: return vk.EL;
                            case IAC.EOR: return vk.EOR;
                            case IAC.ABORT: return vk.ABORT;
                            case IAC.SUSP: return vk.SUSP;
                            case IAC.EOF: return vk.EOF;
                        }
                        break;

                    //case TokenType.TM:
                    //    mSocket.Read();
                    //    Send_WILL(Option.TIMING_MARK);
                    //    mSelf[(Int32)Option.TIMING_MARK] = OptionState.OFF;
                    //    break;
                }
            }
        }

        public void xWrite(Byte data)
        {
            Byte[] B = new Byte[3];
            Int32 n = 0;
            switch (mWriteState)
            {
                case 0: // normal output
                    B[n++] = data;
                    if (data == (Byte)IAC.IAC) B[n++] = data;
                    else if (data == (Byte)'\r') mWriteState = 1;
                    break;
                case 1: // CR
                    if (data != (Byte)'\n') B[n++] = 0;
                    B[n++] = data;
                    if (data == (Byte)IAC.IAC) B[n++] = data;
                    else if (data != (Byte)'\r') mWriteState = 0;
                    break;
            }
            if (n != 0)
            {
                //mSocket.Write(B, 0, n);
                //if (mAutoFlush) mSocket.Flush();
            }
            if (!Dest(Option.ECHO))
            {
                //B[0] = data;
                //mSocket.QueueTokenForRead(new Token(TokenType.Data, B, 0, 1));
            }
        }

        private void xWrite(vk code)
        {
            //if ((vkCode == vkEOR) && (!Self(Option.END_OF_RECORD))) throw new InvalidOperationException("EOR mode not enabled");
            //Byte[] B = new Byte[2];
            //B[0] = (Byte)IAC.IAC;
            //switch (vkCode)
            //{
            //    case vkBRK: B[1] = (Byte)IAC.BRK; break;
            //    case vkIP: B[1] = (Byte)IAC.IP; break;
            //    case vkAO: B[1] = (Byte)IAC.AO; break;
            //    case vkAYT: B[1] = (Byte)IAC.AYT; break;
            //    case vkEC: B[1] = (Byte)IAC.EC; break;
            //    case vkEL: B[1] = (Byte)IAC.EL; break;
            //    case vkEOR: B[1] = (Byte)IAC.EOR; break;
            //    case vkABORT: B[1] = (Byte)IAC.ABORT; break;
            //    case vkSUSP: B[1] = (Byte)IAC.SUSP; break;
            //    case vkEOF: B[1] = (Byte)IAC.EOF; break;
            //    default: throw new ArgumentException("vkCode");
            //}
            //Debug.WriteLine("Terminal sends: IAC {0} ({1:D0})", (IAC)B[1], B[1]);
            //_Send(B, 0, B.Length);
            //_Flush();
        }

    }
}
