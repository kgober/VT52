// IO.cs
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
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;

namespace Emulator
{
    // IO - connect emulated VT52 UART to the outside world

    // Future Improvements / To Do
    // don't read bytes from telnet unless ready to display (allows AO to work)
    // serial port settings (baud rate, parity, flow control, etc.)
    // serial port buffering (allow high speed input to be displayed at a lower rate)
    // allow telnet options to be selectively enabled/disabled
    // simulate connection to a terminal server
    // SSH support
    // LAT support?

    public enum IOEventType
    {
        Data = 1,
        Break = 2,
        Flush = 3,
        Disconnect = 4,
    }

    public class IOEventArgs
    {
        private IOEventType mType;
        private Byte mValue;

        internal IOEventArgs(IOEventType type, Byte value)
        {
            mType = type;
            mValue = value;
        }

        public IOEventType Type
        {
            get { return mType; }
        }

        public Byte Value
        {
            get { return mValue; }
        }
    }

    public abstract class IO
    {
        // EventHandler - function prototype for IOEvent event handlers
        public delegate void EventHandler(Object sender, IOEventArgs e);

        // IOEvent - raised for I/O events
        public abstract event EventHandler IOEvent;

        // ConnectionString - a string that describes the current connection
        public abstract String ConnectionString { get; }

        // DelaySend - true if Terminal should send only after UART delay
        public abstract Boolean DelaySend { get; }

        // DelayRecv - true if Terminal should receive only after UART delay
        public abstract Boolean DelayRecv { get; }

        // Send - called by Terminal to send one byte of data
        public abstract void Send(Byte data);

        // SetBreak - called by Terminal to set Break state
        public abstract void SetBreak(Boolean asserted);

        // SetDTR - called by Terminal to set DTR state
        public abstract void SetDTR(Boolean asserted);

        // SetRTS - called by Terminal to set RTS state
        public abstract void SetRTS(Boolean asserted);

        // Close - called by Terminal to shut down interface
        public abstract void Close();

        
        // Loopback - connect terminal to a simulated loopback plug
        public class Loopback : IO
        {
            private Boolean mBreak;

            public Loopback()
            {
            }

            public override event EventHandler IOEvent;

            public override String ConnectionString
            {
                get { return "Local"; }
            }

            // loopback doesn't finish sending until UART delay elapses
            public override Boolean DelaySend
            {
                get { return true; }
            }

            // loopback is finished receiving bits as soon as sending ends
            public override Boolean DelayRecv
            {
                get { return false; }
            }

            public override void Send(Byte data)
            {
                if (mBreak) return;
                EventHandler h = IOEvent;
                if (h != null) h(this, new IOEventArgs(IOEventType.Data, data));
            }

            public override void SetBreak(Boolean asserted)
            {
                if (asserted != mBreak)
                {
                    EventHandler h = IOEvent;
                    if (h != null) h(this, new IOEventArgs(IOEventType.Break, (asserted) ? (Byte)1 : (Byte)0));
                }
                mBreak = asserted;
            }

            public override void SetDTR(Boolean asserted)
            {
            }

            public override void SetRTS(Boolean asserted)
            {
            }

            public override void Close()
            {
                IOEvent = null;
            }
        }


        // Serial - connect terminal to a serial (COM) port
        public class Serial : IO
        {
            private String mPortName;
            private SerialPort mPort;

            public Serial(String portName)
            {
                mPortName = portName;
                mPort = new SerialPort(portName);
                mPort.BaudRate = 19200;
                mPort.DataBits = 8;
                mPort.Parity = Parity.None;
                mPort.StopBits = StopBits.One;
                mPort.DtrEnable = true;
                mPort.RtsEnable = true;
                mPort.Handshake = Handshake.None;
                mPort.ReceivedBytesThreshold = 1;
                mPort.DataReceived += DataReceived;
                mPort.Open();
            }

            public String PortName
            {
                get { return mPortName; }
            }

            public Int32 BaudRate
            {
                get { return mPort.BaudRate; }
            }

            public override event EventHandler IOEvent;

            public override String ConnectionString
            {
                get { return String.Format("{0} {1:D0}-{2:D0}-{3}-{4:D0}", mPortName, mPort.BaudRate, 8, 'N', 1); }
            }

            // serial port must be preloaded before sending begins (don't double UART latency)
            public override Boolean DelaySend
            {
                get { return false; }
            }

            // serial port doesn't finish receiving until last bit (don't double UART latency)
            public override Boolean DelayRecv
            {
                get { return false; }
            }

            public override void Send(Byte data)
            {
                //if (mPort.BreakState) return; // is this needed or will hardware handle it?
                mPort.Write(new Byte[] { data }, 0, 1);
            }

            public override void SetBreak(Boolean asserted)
            {
                mPort.BreakState = asserted;
            }

            public override void SetDTR(Boolean asserted)
            {
                mPort.DtrEnable = asserted;
            }

            public override void SetRTS(Boolean asserted)
            {
                mPort.RtsEnable = asserted;
            }

            public override void Close()
            {
                IOEvent = null;
                new Thread(ShutdownThread).Start();
            }

            private void ShutdownThread()
            {
                mPort.Close();
            }

            private void DataReceived(Object sender, SerialDataReceivedEventArgs e)
            {
                while (mPort.BytesToRead != 0)
                {
                    Int32 b = mPort.ReadByte();
                    if (b == -1) break;
                    EventHandler h = IOEvent;
                    if (h != null) h(this, new IOEventArgs(IOEventType.Data, (Byte)b));
                }
            }
        }


        // Telnet - connect terminal to a host using the telnet protocol
        public class Telnet : IO
        {
            private String mDestination;
            private Emulator.Telnet mTelnet;
            private Boolean mBreak;

            public Telnet(String destination)
            {
                mDestination = destination;
                mTelnet = new Emulator.Telnet(destination);
                mTelnet.Receive += Receive;
            }

            public override event EventHandler IOEvent;

            public override String ConnectionString
            {
                get { return mDestination; }
            }

            // telnet can't begin send until it gets last bit from Terminal UART
            public override Boolean DelaySend
            {
                get { return true; }
            }

            // telnet doesn't finish receiving until UART delay elapses
            public override Boolean DelayRecv
            {
                get { return true; }
            }

            public override void Send(Byte data)
            {
                if (mBreak) return;
                mTelnet.Write((Byte)(data & 0x7F)); // strip parity bit
            }

            public override void SetBreak(Boolean asserted)
            {
                if ((mBreak == false) && (asserted == true)) mTelnet.Break();
                mBreak = asserted;
            }

            public override void SetDTR(Boolean asserted)
            {
            }

            public override void SetRTS(Boolean asserted)
            {
            }

            public override void Close()
            {
                IOEvent = null;
                new Thread(ShutdownThread).Start();
            }

            private void ShutdownThread()
            {
                mTelnet.Close();
            }

            private void Receive(Object sender, IOEventArgs e)
            {
                if (e.Type == IOEventType.Data)
                {
                    Int32 n;
                    while ((n = mTelnet.ReadByte()) != -1)
                    {
                        EventHandler h = IOEvent;
                        if (h != null) h(this, new IOEventArgs(IOEventType.Data, (Byte)n));
                    }
                }
                else if (e.Type == IOEventType.Break)
                {
                    EventHandler h = IOEvent;
                    if (h != null)
                    {
                        h(this, new IOEventArgs(IOEventType.Break, 1));
                        h(this, new IOEventArgs(IOEventType.Break, 0));
                    }
                }
                else if (e.Type == IOEventType.Flush)
                {
                    EventHandler h = IOEvent;
                    if (h != null) h(this, new IOEventArgs(IOEventType.Flush, 0));
                }
                else if (e.Type == IOEventType.Disconnect)
                {
                    EventHandler h = IOEvent;
                    if (h != null) h(this, new IOEventArgs(IOEventType.Disconnect, 0));
                }
            }
        }
    }
}
