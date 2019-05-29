// RawTCP.cs
// Copyright (c) 2017, 2019 Kenneth Gober
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
using System.Threading;

namespace Emulator
{
    // The RawTCP class implements a raw TCP connection.  ASCII bytes may be sent
    // but there is no support for break or any flow control (other than ^S/^Q).


    // RawTCP Public Interface

    partial class RawTCP
    {
        public delegate void ReceiveEventHandler(Object sender, IOEventArgs e);
        public event ReceiveEventHandler Receive;

        private TcpClient mTcpClient;
        private Socket mSocket;
        private Queue<Byte> mRecvQueue = new Queue<Byte>();
        private Boolean mAutoFlush;

        public RawTCP()
        {
        }

        public RawTCP(String destination)
        {
            Connect(destination);
        }

        public RawTCP(String host, Int32 port)
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

        public void Connect(String destination)
        {
            String host = destination;
            Int32 port = -1;
            Int32 p = destination.IndexOf(':');
            if (p != -1)
            {
                port = Int32.Parse(destination.Substring(p + 1));
                host = destination.Substring(0, p);
            }
            if (p == -1) throw new ArgumentException("destination");
            Connect(host, port);
        }

        public void Connect(String host, Int32 port)
        {
            if (mTcpClient != null) throw new InvalidOperationException("Already connected");
            mTcpClient = new TcpClient(host, port);
            mSocket = mTcpClient.Client;
            Start();
        }

        public void Close()
        {
            Stop();
            mTcpClient = null;
        }

        public Int32 Read(Byte[] buffer, Int32 offset, Int32 size)
        {
            Int32 count = 0;
            lock (mRecvQueue)
            {
                while (size-- > 0)
                {
                    if (mRecvQueue.Count == 0) break;
                    buffer[offset++] = mRecvQueue.Dequeue();
                    count++;
                }
                return count;
            }
        }

        public Int32 ReadByte()
        {
            lock (mRecvQueue)
            {
                if (mRecvQueue.Count == 0) return -1;
                return mRecvQueue.Dequeue();
            }
        }

        public void Write(Byte[] buffer, Int32 offset, Int32 size)
        {
            mSocket.Send(buffer, offset, size, SocketFlags.None);
        }

        public void Write(Byte value)
        {
            Byte[] buf = new Byte[1];
            buf[0] = value;
            mSocket.Send(buf, 0, 1, SocketFlags.None);
        }

        public void Flush()
        {
        }
    }


    // Worker Threads

    partial class RawTCP
    {
        private Thread mThread;
        private volatile Boolean mStopFlag;

        private void Start()
        {
            mThread = new Thread(ThreadProc);
            mThread.Start();
        }

        private void Stop()
        {
            mStopFlag = true;
        }

        private Boolean Connected
        {
            get
            {
                if (!mSocket.Connected) return false;
                if ((mSocket.Poll(0, SelectMode.SelectRead)) && (mSocket.Available == 0)) return false;
                return true;
            }
        }

        private void ThreadProc()
        {
            Byte[] buf = new Byte[512];
            while (!mStopFlag)
            {
                Boolean f = false;
                while ((mSocket.Connected) && (mSocket.Available != 0))
                {
                    Int32 n = mSocket.Available;
                    if (n > buf.Length) n = buf.Length;
                    SocketError e;
                    n = mSocket.Receive(buf, 0, n, SocketFlags.None, out e);
                    if (e != SocketError.Success) throw new ApplicationException(String.Concat("Error reading socket: ", e.ToString()));
                    if (n > 0)
                    {
                        lock (mRecvQueue) for (Int32 i = 0; i < n; i++) mRecvQueue.Enqueue(buf[i]);
                        f = true;
                    }
                }
                if ((f) && (Receive != null)) Receive(this, new IOEventArgs(IOEventType.Data, 0));
                if (!Connected) break;
                Flush();
                Thread.Sleep(1);
            }
            Debug.WriteLine("Disconnected");
            Flush();
            mSocket.Close();
        }
    }
}
