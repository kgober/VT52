// VT52.cs
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
using System.Drawing;
using System.Drawing.Imaging;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;

namespace Emulator
{
    public partial class Terminal
    {
        // VT52 Emulator
        // References:
        // http://bitsavers.trailing-edge.com/pdf/dec/terminal/vt52/EK-VT5X-OP-001_DECscope_Users_Manual_Mar77.pdf
        // http://bitsavers.trailing-edge.com/pdf/dec/terminal/vt52/EK-VT52-MM-002_maint_Jul78.pdf
        // http://bitsavers.trailing-edge.com/pdf/dec/terminal/vt52/MP00035_VT52schem.pdf

        // Future Improvements / To Do
        // key click
        // accurate bell sound
        // copy key (incl. ESC Z report of printer support) (home or pgup)
        // repeat key (alt)
        // local copy (internal UART loopback)
        // accurate behavior for invalid S1/S2 switch combinations
        // accurate keyboard rollover
        // add command line option for serial connection
        // allow re-use of recent network destinations
        // store previous serial port configuration


        // Terminal-MainWindow Interface [Main UI Thread]

        public partial class VT52 : Terminal
        {
            public VT52()
            {
                InitKeyboard();
                InitDisplay();
                InitIO();
                ParseArgs(Program.Args);
            }

            private void ParseArgs(String[] args)
            {
                Int32 ap = 0;
                while (ap < args.Length)
                {
                    String arg = args[ap++];
                    if ((arg != null) && (arg.Length != 0))
                    {
                        Char c = arg[0];
                        if (((c == '-') || (c == '/')) && (arg.Length > 1))
                        {
                            switch (arg[1])
                            {
                                case 'o':
                                case 'O':
                                    arg = arg.Substring(2);
                                    if ((arg.Length == 0) && (ap < args.Length)) arg = args[ap++];
                                    while (arg.Length != 0)
                                    {
                                        if (arg.StartsWith("s+", StringComparison.OrdinalIgnoreCase))
                                        {
                                            mOptSwapDelBS = true;
                                            if (dlgSettings == null) dlgSettings = new SettingsDialog();
                                            dlgSettings.OptSwapDelBS = true;
                                            arg = arg.Substring(2);
                                        }
                                        else if (arg.StartsWith("s-", StringComparison.OrdinalIgnoreCase))
                                        {
                                            mOptSwapDelBS = false;
                                            if (dlgSettings == null) dlgSettings = new SettingsDialog();
                                            dlgSettings.OptSwapDelBS = false;
                                            arg = arg.Substring(2);
                                        }
                                        else if (arg.StartsWith("r+", StringComparison.OrdinalIgnoreCase))
                                        {
                                            mOptAutoRepeat = true;
                                            if (dlgSettings == null) dlgSettings = new SettingsDialog();
                                            dlgSettings.OptAutoRepeat = true;
                                            arg = arg.Substring(2);
                                        }
                                        else if (arg.StartsWith("r-", StringComparison.OrdinalIgnoreCase))
                                        {
                                            mOptAutoRepeat = false;
                                            if (dlgSettings == null) dlgSettings = new SettingsDialog();
                                            dlgSettings.OptAutoRepeat = false;
                                            arg = arg.Substring(2);
                                        }
                                        else if (arg.StartsWith("g+", StringComparison.OrdinalIgnoreCase))
                                        {
                                            mDisplay.GreenFilter = true;
                                            if (dlgSettings == null) dlgSettings = new SettingsDialog();
                                            dlgSettings.OptGreenFilter = true;
                                            arg = arg.Substring(2);
                                        }
                                        else if (arg.StartsWith("g-", StringComparison.OrdinalIgnoreCase))
                                        {
                                            mDisplay.GreenFilter = false;
                                            if (dlgSettings == null) dlgSettings = new SettingsDialog();
                                            dlgSettings.OptGreenFilter = false;
                                            arg = arg.Substring(2);
                                        }
                                    }
                                    break;
                                case 'r':
                                case 'R':
                                    arg = arg.Substring(2);
                                    if ((arg.Length == 0) && (ap < args.Length)) arg = args[ap++];
                                    if (dlgConnection == null) dlgConnection = new ConnectionDialog();
                                    dlgConnection.Set(typeof(IO.RawTCP), arg);
                                    mUART.IO = ConnectRawTCP(dlgConnection.Options);
                                    break;
                                case 't':
                                case 'T':
                                    arg = arg.Substring(2);
                                    if ((arg.Length == 0) && (ap < args.Length)) arg = args[ap++];
                                    if (dlgConnection == null) dlgConnection = new ConnectionDialog();
                                    dlgConnection.Set(typeof(IO.Telnet), arg);
                                    mUART.IO = ConnectTelnet(dlgConnection.Options);
                                    break;
                            }
                        }
                    }
                }
            }
        }


        // Terminal Input (Keyboard & Switches) [Main UI Thread]

        // VT52 Key Mappings:
        //   most ASCII character keys function as labeled on PC
        //   note: VT52 "[]" and "{}" keys differ from PC "[{" and "]}" keys
        //   Linefeed = Insert      PF1 = F1 (also NumLock)
        //   Break = End            PF2 = F2 (also Num/)
        //   Scroll = PgDn          PF3 = F3 (also Num*)
        //   Up = Up (also Num- in Alternate Keypad Mode)
        //   Down = Down (also Shift + Num+ in Alternate Keypad Mode)
        //   Right = Right (also Num+ in Alternate Keypad Mode)
        //   Left = Left (also Shift + NumEnter in Alternate Keypad Mode)
        // Brightness Slider: F11 (decrease) & F12 (increase)
        // Switch S1 (1-7) - Transmit Speed
        //   1 = Off-Line (Transmit at S2 Speed)
        //   2 = Full Duplex with Local Copy (Transmit at S2 Speed)
        //   3 = Full Duplex (Transmit at S2 Speed)
        //   4 = 300 Baud Transmit
        //   5 = 150 Baud Transmit
        //   6 = 75 Baud Transmit
        //   7 = 4800 Baud Transmit
        //   8 = Line Speed (emulator only, not a real VT52 capability)
        // Switch S2 (A-G) - Receive Speed
        //   A = (Receive at S1 Speed) with Local Copy
        //   B = 110 Baud Receive
        //   C = (Receive at S1 Speed)
        //   D = 600 Baud Receive
        //   E = 1200 Baud Receive
        //   F = 2400 Baud Receive
        //   G = 9600 Baud Receive
        //   H = 19200 Baud Receive (emulator only, not a real VT52 capability)
        // invalid combinations: 1A 1C 2A 2C 3A 3C
        // "Local Copy" means local echo (UART tx wired to rx)

        public partial class VT52
        {
            private List<VK> mKeys;                 // keys currently pressed
            private Boolean mShift;                 // Shift is pressed
            private Boolean mCtrl;                  // Ctrl is pressed
            private Boolean mCaps;                  // Caps Lock is enabled
            private volatile Boolean mKeypadMode;   // Alternate Keypad Mode enabled
            private Boolean mOptSwapDelBS;          // swap Delete and Backspace keys
            private Boolean mOptAutoRepeat;         // enable automatic key repeat
            private SettingsDialog dlgSettings;
            private ConnectionDialog dlgConnection;

            private void InitKeyboard()
            {
                mKeys = new List<VK>();
                mCaps = Console.CapsLock;
            }

            public Boolean KeypadMode
            {
                get { return mKeypadMode; }
                set { mKeypadMode = value; }
            }

            public override Boolean KeyEvent(Int32 msgId, IntPtr wParam, IntPtr lParam)
            {
                switch (msgId)
                {
                    case 0x0100:    // WM_KEYDOWN
                        return KeyDown(wParam, lParam);
                    case 0x0101:    // WM_KEYUP
                        return KeyUp(wParam, lParam);
                    default:
                        return false;
                }
            }

            private Boolean KeyDown(IntPtr wParam, IntPtr lParam)
            {
                Char c;
                VK k = MapKey(wParam, lParam);
                Int32 l = lParam.ToInt32();
#if DEBUG
                Log.WriteLine("KeyDown: wParam={0:X8} lParam={1:X8} vk={2} (0x{3:X2}) num={4}", (Int32)wParam, l, k.ToString(), (Int32)k, Console.NumberLock);
#endif

                // prevent NumLock key from changing NumLock state by pressing it again
                if (k == VK.NUMLOCK)
                {
                    if (((l >> 16) & 0xFF) == 0) return true;
                    Win32.keybd_event(k, 0, KEYEVENTF.EXTENDEDKEY | KEYEVENTF.KEYUP, 0);
                    Win32.keybd_event(k, 0, KEYEVENTF.EXTENDEDKEY, 0);
                }

                // auto-repeat always enabled for F11 & F12
                if (k == VK.F11) { LowerBrightness(); return true; }
                if (k == VK.F12) { RaiseBrightness(); return true; }

                if (!mKeys.Contains(k))
                    mKeys.Add(k);
                else if (((l & 0x40000000) != 0) && (mOptAutoRepeat == false))
                    return true;

                if ((k >= VK.A) && (k <= VK.Z))
                {
                    c = (Char)(k - VK.A + ((mShift || mCaps) ? 'A' : 'a'));
                    Input((mCtrl) ? (Char)(c & 31) : c);
                    return true;
                }
                if ((k >= VK.K0) && (k <= VK.K9))
                {
                    c = (Char)(k - VK.K0 + '0');
                    if (mShift)
                    {
                        switch (c)
                        {
                            case '1': c = '!'; break;
                            case '2': c = '@'; break;
                            case '3': c = '#'; break;
                            case '4': c = '$'; break;
                            case '5': c = '%'; break;
                            case '6': c = '^'; break;
                            case '7': c = '&'; break;
                            case '8': c = '*'; break;
                            case '9': c = '('; break;
                            case '0': c = ')'; break;
                        }
                    }
                    Input((mCtrl) ? (Char)(c & 31) : c);
                    return true;
                }
                if ((k >= VK.NUMPAD0) && (k <= VK.NUMPAD9))
                {
                    c = (Char)(k - VK.NUMPAD0 + '0');
                    if (!mKeypadMode)
                    {
                        Input(c);
                        return true;
                    }
                    switch (c)
                    {
                        case '0': Input("\x001B?p"); break;
                        case '1': Input("\x001B?q"); break;
                        case '2': Input("\x001B?r"); break;
                        case '3': Input("\x001B?s"); break;
                        case '4': Input("\x001B?t"); break;
                        case '5': Input("\x001B?u"); break;
                        case '6': Input("\x001B?v"); break;
                        case '7': Input("\x001B?w"); break;
                        case '8': Input("\x001B?x"); break;
                        case '9': Input("\x001B?y"); break;
                    }
                    return true;
                }

                switch (k)
                {
                    case VK.LSHIFT:
                    case VK.RSHIFT:
                        mShift = true;
                        return true;
                    case VK.LCONTROL:
                    case VK.RCONTROL:
                        mCtrl = true;
                        return true;
                    case VK.CAPITAL:
                        mCaps = !mCaps;
                        return true;
                    case VK.SPACE:
                        Input((mCtrl) ? '\x00' : ' ');
                        return true;
                    case VK.RETURN:
                        Input('\r');
                        return true;
                    case VK.INSERT:
                        Input('\n');
                        return true;
                    case VK.BACK:
                        Input('\b');
                        return true;
                    case VK.TAB:
                        Input('\t');
                        return true;
                    case VK.ESCAPE:
                        Input('\x1B');
                        return true;
                    case VK.DELETE:
                        Input((mCtrl) ? '\x1F' : '\x7F');
                        return true;
                    case VK.COMMA:
                        c = (mShift) ? '<' : ',';
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.PERIOD:
                        c = (mShift) ? '>' : '.';
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.SLASH:
                        c = (mShift) ? '?' : '/';
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.SEMICOLON:
                        c = (mShift) ? ':' : ';';
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.QUOTE:
                        c = (mShift) ? '"' : '\'';
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.MINUS:
                        c = (mShift) ? '_' : '-';
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.EQUAL:
                        c = (mShift) ? '+' : '=';
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.TILDE:
                        c = (mShift) ? '~' : '`';
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.BACKSLASH:
                        c = (mShift) ? '|' : '\\';
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.LBRACKET:
                        c = (mShift) ? '{' : '['; // on a real VT52, this is [ (unshifted) or ] (shifted)
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.RBRACKET:
                        c = (mShift) ? '}' : ']'; // on a real VT52, this is { (unshifted) or } (shifted)
                        Input((mCtrl) ? (Char)(c & 31) : c);
                        return true;
                    case VK.UP:
                        Input("\x001BA");
                        return true;
                    case VK.DOWN:
                        Input("\x001BB");
                        return true;
                    case VK.RIGHT:
                        Input("\x001BC");
                        return true;
                    case VK.LEFT:
                        Input("\x001BD");
                        return true;
                    case VK.NUMLOCK:
                    case VK.F1:
                        Input("\x001BP");
                        return true;
                    case VK.DIVIDE:
                    case VK.F2:
                        Input("\x001BQ");
                        return true;
                    case VK.MULTIPLY:
                    case VK.F3:
                        Input("\x001BR");
                        return true;
                    case VK.SUBTRACT:
                        Input((mKeypadMode) ? "\x001BA" : "-");
                        return true;
                    case VK.ADD:
                        Input((mKeypadMode) ? ((mShift) ? "\x001BB" : "\x001BC") : "+");
                        return true;
                    case VK.ENTER:
                        Input((mKeypadMode) ? ((mShift) ? "\x001BD" : "\x001B?M") : "\r");
                        return true;
                    case VK.DECIMAL:
                        Input((mKeypadMode) ? "\x001B?n" : ".");
                        return true;
                    case VK.END:
                        SetBreakState(true);
                        return true;
                    case VK.NEXT:
                        AllowScroll((mShift) ? 24 : 1);
                        return true;
                    case VK.F5:
                        AskSettings();
                        return true;
                    case VK.F6:
                        AskConnection();
                        return true;
                }
                return false;
            }

            private Boolean KeyUp(IntPtr wParam, IntPtr lParam)
            {
                VK k = MapKey(wParam, lParam);
                Int32 l = (Int32)(lParam.ToInt64() & 0x00000000FFFFFFFF);
#if DEBUG
                Log.WriteLine("KeyUp: wParam={0:X8} lParam={1:X8} vk={2} (0x{3:X2}) num={4}", (Int32)wParam, l, k.ToString(), (Int32)k, Console.NumberLock);
#endif
                if (mKeys.Contains(k)) mKeys.Remove(k);

                if ((k >= VK.A) && (k <= VK.Z)) return true;
                if ((k >= VK.K0) && (k <= VK.K9)) return true;
                if ((k >= VK.NUMPAD0) && (k <= VK.NUMPAD9)) return true;

                switch (k)
                {
                    case VK.LSHIFT:
                        mShift = mKeys.Contains(VK.RSHIFT);
                        return true;
                    case VK.RSHIFT:
                        mShift = mKeys.Contains(VK.LSHIFT);
                        return true;
                    case VK.LCONTROL:
                        mCtrl = mKeys.Contains(VK.RCONTROL);
                        return true;
                    case VK.RCONTROL:
                        mCtrl = mKeys.Contains(VK.LCONTROL);
                        return true;
                    case VK.CAPITAL:
                        mCaps = Console.CapsLock;
                        return true;
                    case VK.SPACE:
                    case VK.RETURN:
                    case VK.INSERT:
                    case VK.BACK:
                    case VK.TAB:
                    case VK.ESCAPE:
                    case VK.DELETE:
                    case VK.COMMA:
                    case VK.PERIOD:
                    case VK.SLASH:
                    case VK.SEMICOLON:
                    case VK.QUOTE:
                    case VK.MINUS:
                    case VK.EQUAL:
                    case VK.TILDE:
                    case VK.BACKSLASH:
                    case VK.LBRACKET:
                    case VK.RBRACKET:
                    case VK.UP:
                    case VK.DOWN:
                    case VK.RIGHT:
                    case VK.LEFT:
                    case VK.NUMLOCK:
                    case VK.F1:
                    case VK.DIVIDE:
                    case VK.F2:
                    case VK.MULTIPLY:
                    case VK.F3:
                    case VK.SUBTRACT:
                    case VK.ADD:
                    case VK.ENTER:
                    case VK.DECIMAL:
                    case VK.NEXT:
                    case VK.F5:
                    case VK.F6:
                    case VK.F11:
                    case VK.F12:
                        return true;
                    case VK.END:
                        SetBreakState(false);
                        return true;
                }
                return false;
            }

            private VK MapKey(IntPtr wParam, IntPtr lParam)
            {
                VK k = (VK)wParam;
                Int32 l = (Int32)(lParam.ToInt64() & 0x00000000FFFFFFFF);
                switch (k)
                {
                    case VK.SHIFT:
                        return (VK)Win32.MapVirtualKey((UInt32)((l & 0x00FF0000) >> 16), MAPVK.VSC_TO_VK_EX);
                    case VK.CONTROL:
                        return ((l & 0x01000000) == 0) ? VK.LCONTROL : VK.RCONTROL;
                    case VK.ALT:
                        return ((l & 0x01000000) == 0) ? VK.LALT : VK.RALT;
                    case VK.RETURN:
                        return ((l & 0x01000000) == 0) ? VK.RETURN : VK.ENTER;
                    case VK.BACK:
                        return (mOptSwapDelBS) ? VK.DELETE : VK.BACK;
                    case VK.DELETE:
                        return (mOptSwapDelBS) ? VK.BACK : VK.DELETE;
                    default:
                        return k;
                }
            }

            private void Input(String s)
            {
                if (s == null) return;
                for (Int32 i = 0; i < s.Length; i++) Send((Byte)s[i]);
            }

            private void Input(Char c)
            {
                Send((Byte)c);
            }

            public void AskSettings()
            {
                if (dlgSettings == null) dlgSettings = new SettingsDialog();
                dlgSettings.ShowDialog();
                if (!dlgSettings.OK) return;

                if (dlgSettings.OptSwapDelBS != mOptSwapDelBS)
                {
                    if (mKeys.Contains(VK.BACK)) mKeys.Remove(VK.BACK);
                    if (mKeys.Contains(VK.DELETE)) mKeys.Remove(VK.DELETE);
                    mOptSwapDelBS = dlgSettings.OptSwapDelBS;
                }
                mOptAutoRepeat = dlgSettings.OptAutoRepeat;
                mDisplay.GreenFilter = dlgSettings.OptGreenFilter;

                Int32 t = -1;
                switch (dlgSettings.S1)
                {
                    case '4': t = 300; break;
                    case '5': t = 150; break;
                    case '6': t = 75; break;
                    case '7': t = 4800; break;
                    case '8': t = 0; break;
                }
                Int32 r = -1;
                switch (dlgSettings.S2)
                {
                    case 'A': r = t; break;
                    case 'B': r = 110; break;
                    case 'C': r = t; break;
                    case 'D': r = 600; break;
                    case 'E': r = 1200; break;
                    case 'F': r = 2400; break;
                    case 'G': r = 9600; break;
                    case 'H': r = 19200; break;
                }
                switch (dlgSettings.S1)
                {
                    case '1': t = r; break;
                    case '2': t = r; break;
                    case '3': t = r; break;
                }
                if (t == -1) return;
                if (r == -1) return;
                SetTransmitSpeed(t);
                SetReceiveSpeed(r);
                SetTransmitParity(dlgSettings.Parity);
            }

            public void AskConnection()
            {
                if (dlgConnection == null) dlgConnection = new ConnectionDialog();
                dlgConnection.ShowDialog();
                if (!dlgConnection.OK) return;
                if (dlgConnection.IOAdapter == typeof(IO.Loopback))
                {
                    mUART.IO = ConnectLoopback(dlgConnection.Options);
                }
                else if (dlgConnection.IOAdapter == typeof(IO.Serial))
                {
                    mUART.IO = ConnectSerial(dlgConnection.Options);
                }
                else if (dlgConnection.IOAdapter == typeof(IO.Telnet))
                {
                    mUART.IO = ConnectTelnet(dlgConnection.Options);
                }
                else if (dlgConnection.IOAdapter == typeof(IO.RawTCP))
                {
                    mUART.IO = ConnectRawTCP(dlgConnection.Options);
                }
            }

            private IO ConnectLoopback(String options)
            {
                if (mUART.IO is IO.Loopback) return mUART.IO;
                try
                {
                    IO X = new IO.Loopback(options);
                    String s = String.Concat("VT52 - ", X.ConnectionString);
                    if (String.Compare(s, mCaption) != 0)
                    {
                        mCaption = s;
                        mCaptionDirty = true;
                    }
                    return X;
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message);
                    return mUART.IO;
                }
            }

            private IO ConnectSerial(String options)
            {
                if ((mUART.IO is IO.Serial) && (String.Compare(mUART.IO.Options, options) == 0)) return mUART.IO;
                try
                {
                    IO X = new IO.Serial(options);
                    String s = String.Concat("VT52 - ", X.ConnectionString);
                    if (String.Compare(s, mCaption) != 0)
                    {
                        mCaption = s;
                        mCaptionDirty = true;
                    }
                    return X;
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message);
                    return mUART.IO;
                }
            }

            private IO ConnectTelnet(String options)
            {
                if ((mUART.IO is IO.Telnet) && (String.Compare(mUART.IO.Options, options) == 0)) return mUART.IO;
                try
                {
                    IO X = new IO.Telnet(options);
                    String s = String.Concat("VT52 - ", X.ConnectionString);
                    if (String.Compare(s, mCaption) != 0)
                    {
                        mCaption = s;
                        mCaptionDirty = true;
                    }
                    return X;
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message);
                    return mUART.IO;
                }
            }

            private IO ConnectRawTCP(String options)
            {
                if ((mUART.IO is IO.RawTCP) && (String.Compare(mUART.IO.Options, options) == 0)) return mUART.IO;
                try
                {
                    IO X = new IO.RawTCP(options);
                    String s = String.Concat("VT52 - ", X.ConnectionString);
                    if (String.Compare(s, mCaption) != 0)
                    {
                        mCaption = s;
                        mCaptionDirty = true;
                    }
                    return X;
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message);
                    return mUART.IO;
                }
            }
        }


        // Terminal Output (Display & Bell)

        public partial class VT52
        {
            private Display mDisplay;
            private Queue<Byte> mSilo;              // buffered bytes received from UART
            private Int32 mHoldCount;               // number of scrolls until Hold Screen pauses (0 = pausing)
            private Int32 mEsc;                     // processing state for ESC sequences
            private Boolean mGraphicsMode;          // Graphics Mode enabled

            // called by main UI thread via constructor
            private void InitDisplay()
            {
                mDisplay = new Display(this);
                mSilo = new Queue<Byte>(13);
                mHoldCount = -1;
            }

            // called by main UI thread
            public override Bitmap Bitmap
            {
                get { return mDisplay.Bitmap; }
            }

            // called by main UI thread
            public override Boolean BitmapDirty
            {
                get { return mDisplay.BitmapDirty; }
                set { mDisplay.BitmapDirty = value; }
            }

            // called by main UI thread via KeyDown() or system menu
            public void LowerBrightness()
            {
                mDisplay.ChangeBrightness(-5);
            }

            // called by main UI thread via KeyDown() or system menu
            public void RaiseBrightness()
            {
                mDisplay.ChangeBrightness(5);
            }

            // called by main UI thread via KeyDown()
            private void AllowScroll(Int32 lines)
            {
                lock (mSilo)
                {
                    if (mHoldCount == 0)
                    {
                        mHoldCount += lines;
                        while ((mHoldCount != 0) && (mSilo.Count != 0)) Output(mSilo.Dequeue());
                        if (mHoldCount != 0) Send(0x11); // XON
                    }
                }
            }

            // called by worker thread
            private void Recv(Byte c)
            {
#if DEBUG
                Log.WriteLine("Recv: {0} ({1:D0}/0x{1:X2})", (Char)c, c);
#endif
                // if Hold Screen is pausing, divert received chars to silo
                lock (mSilo)
                {
                    if (mHoldCount == 0)
                    {
                        if (mSilo.Count == 13)
                        {
                            // prevent silo overflow by allowing 1 scroll
                            mHoldCount++;
                            while ((mHoldCount != 0) && (mSilo.Count != 0)) Output(mSilo.Dequeue());
                            if ((mHoldCount != 0) && (c != 0x0A)) Send(0x11); // XON
                        }
                        if (mHoldCount == 0)
                        {
                            mSilo.Enqueue(c);
                            return;
                        }
                    }
                }
                Output(c);
            }

            private void Output(Byte c)
            {
                Int32 nx, ny;
                if ((c >= 32) && (c < 127))
                {
                    switch (mEsc)
                    {
                        case 0: // regular ASCII characters (non-control, non-escaped)
                            if ((c >= 94) && (mGraphicsMode))
                            {
                                Int32 n = c - 95;
                                if (n < 0) n = 0;
                                c = (Byte)n;
                            }
                            mDisplay.Char = (Byte)c;
                            mDisplay.MoveCursorRel(1, 0);
                            return;
                        case 1: // ESC - Escape Sequence
                            break;
                        case 2: // ESC Y <row> - Direct Cursor Addressing
                            mEsc = c;
                            return;
                        default: // ESC Y <row> <col> - Direct Cursor Addressing
                            nx = c - 32;
                            if (nx >= Display.COLS) nx = Display.COLS - 1;
                            ny = mEsc - 32;
                            if (ny >= Display.ROWS) ny = mDisplay.CursorY;
                            mEsc = 0;
                            mDisplay.MoveCursorAbs(nx, ny);
                            return;
                    }
                }

                switch ((Char)c)
                {
                    case '\r': // CR - Carriage Return
                        mDisplay.MoveCursorAbs(0, mDisplay.CursorY);
                        return;
                    case '\n': // LF - Line Feed
                        ny = mDisplay.CursorY + 1;
                        if (ny >= Display.ROWS)
                        {
                            if (mHoldCount > 0) mHoldCount--;
                            if (mHoldCount == 0)
                            {
                                mSilo.Enqueue(c);
                                Send(0x13); // XOFF
                                return;
                            }
                            ScrollUp();
                            ny = Display.ROWS - 1;
                        }
                        mDisplay.MoveCursorAbs(mDisplay.CursorX, ny);
                        return;
                    case '\b': // BS - Backspace
                        mDisplay.MoveCursorRel(-1, 0);
                        return;
                    case '\t': // HT - Horizontal Tab
                        if (mDisplay.CursorX >= 72)
                            mDisplay.MoveCursorRel(1, 0);
                        else
                            mDisplay.MoveCursorRel(8 - (mDisplay.CursorX % 8), 0);
                        return;
                    case '\a': // BEL - Ring the Bell
                        mDisplay.Beep();
                        return;
                    case '\x1B': // ESC - Escape Sequence
                        mEsc = 1;
                        return;
                    case 'A': // ESC A - Cursor Up
                        mEsc = 0;
                        mDisplay.MoveCursorRel(0, -1);
                        return;
                    case 'B': // ESC B - Cursor Down
                        mEsc = 0;
                        mDisplay.MoveCursorRel(0, 1);
                        return;
                    case 'C': // ESC C - Cursor Right
                        mEsc = 0;
                        mDisplay.MoveCursorRel(1, 0);
                        return;
                    case 'D': // ESC D - Cursor Left
                        mEsc = 0;
                        mDisplay.MoveCursorRel(-1, 0);
                        return;
                    case 'F': // ESC F - Enter Graphics Mode
                        mEsc = 0;
                        mGraphicsMode = true;
                        return;
                    case 'G': // ESC G - Exit Graphics Mode
                        mEsc = 0;
                        mGraphicsMode = false;
                        return;
                    case 'H': // ESC H - Cursor Home
                        mEsc = 0;
                        mDisplay.MoveCursorAbs(0, 0);
                        return;
                    case 'I': // ESC I - Reverse Line Feed
                        mEsc = 0;
                        ny = mDisplay.CursorY - 1;
                        if (ny < 0)
                        {
                            ScrollDown();
                            ny = 0;
                        }
                        mDisplay.MoveCursorAbs(mDisplay.CursorX, ny);
                        return;
                    case 'J': // ESC J - Erase to End-of-Screen
                        mEsc = 0;
                        for (Int32 x = mDisplay.CursorX; x < Display.COLS; x++) mDisplay[x, mDisplay.CursorY] = 32;
                        for (Int32 y = mDisplay.CursorY + 1; y < Display.ROWS; y++)
                        {
                            for (Int32 x = 0; x < Display.COLS; x++) mDisplay[x, y] = 32;
                        }
                        return;
                    case 'K': // ESC K - Erase to End-of-Line
                        mEsc = 0;
                        for (Int32 x = mDisplay.CursorX; x < Display.COLS; x++) mDisplay[x, mDisplay.CursorY] = 32;
                        return;
                    case 'Y': // ESC Y - Direct Cursor Addressing
                        mEsc = 2;
                        return;
                    case 'Z': // ESC Z - Identify Terminal Type
                        mEsc = 0;
                        Send(0x1B);
                        Send((Byte)'/');
                        Send((Byte)'K');
                        return;
                    case '[': // ESC [ - Enter Hold-Screen Mode
                        mEsc = 0;
                        mHoldCount = 1;
                        return;
                    case '\\': // ESC \ - Exit Hold-Screen Mode
                        mEsc = 0;
                        mHoldCount = -1;
                        return;
                    case '=': // ESC = - Enter Alternate-Keypad Mode
                        mEsc = 0;
                        KeypadMode = true;
                        return;
                    case '>': // ESC > - Exit Alternate-Keypad Mode
                        mEsc = 0;
                        KeypadMode = false;
                        return;
                    default:
                        if ((c >= 32) && (c < 127)) mEsc = 0; // ignore unrecognized escape sequences
                        return;
                }
            }

            private void ScrollUp()
            {
                for (Int32 y = 0; y < Display.ROWS - 1; y++)
                {
                    for (Int32 x = 0; x < Display.COLS; x++)
                    {
                        mDisplay[x, y] = mDisplay[x, y + 1];
                    }
                }
                for (Int32 x = 0; x < Display.COLS; x++) mDisplay[x, Display.ROWS - 1] = 32;
            }

            private void ScrollDown()
            {
                for (Int32 y = Display.ROWS - 1; y > 0; y--)
                {
                    for (Int32 x = 0; x < Display.COLS; x++)
                    {
                        mDisplay[x, y] = mDisplay[x, y - 1];
                    }
                }
                for (Int32 x = 0; x < Display.COLS; x++) mDisplay[x, 0] = 32;
            }

            // 80x24 character cells, each cell is 10 raster lines tall and 9 dots wide.
            // top raster line is blank, next 8 are for character, last is cursor line.
            // To simulate the raster, a 1 pixel gap is left between each raster line.
            // P4 phosphor (white).

            private class Display
            {
                public const Int32 ROWS = 24;
                public const Int32 COLS = 80;
                private const Int32 PIXELS_PER_ROW = 20;
                private const Int32 PIXELS_PER_COL = 9;

                private VT52 mVT52;                     // for calling parent's methods
                private UInt32[] mPixMap;               // pixels
                private GCHandle mPixMapHandle;         // handle for pinned pixels
                private Bitmap mBitmap;                 // bitmap interface
                private volatile Boolean mBitmapDirty;  // true if bitmap has changed
                private Byte[] mChars;                  // characters on screen
                private Int32 mX, mY;                   // cursor position
                private Timer mCursorTimer;             // cursor blink timer
                private Boolean mCursorVisible;         // whether cursor is currently visible
                private Int32 mBrightness;              // brightness (0-100)
                private UInt32 mOffColor;               // pixel 'off' color
                private UInt32 mOnColor;                // pixel 'on' color
                private Boolean mOptGreenFilter;        // simulate green CRT filter

                public Display(VT52 parent)
                {
                    mVT52 = parent;
                    Int32 x = COLS * PIXELS_PER_COL;
                    Int32 y = ROWS * PIXELS_PER_ROW;
                    mPixMap = new UInt32[x * y];
                    mPixMapHandle = GCHandle.Alloc(mPixMap, GCHandleType.Pinned);
                    mBitmap = new Bitmap(x, y, x * sizeof(Int32), PixelFormat.Format32bppPArgb, mPixMapHandle.AddrOfPinnedObject());
                    mBitmapDirty = true;
                    mChars = new Byte[COLS * ROWS];
                    mBrightness = 85;   // 85% is the maximum brightness without blue being oversaturated
                    mOffColor = Color(0);
                    mOnColor = Color(mBrightness);
                    mCursorVisible = false;
                    mCursorTimer = new Timer(CursorTimer_Callback, this, 0, 250); // 4 transitions per second (2 Hz blink rate)
                }

                public Bitmap Bitmap
                {
                    get { return mBitmap; }
                }

                public Boolean BitmapDirty
                {
                    get { return mBitmapDirty; }
                    set { mBitmapDirty = value; }
                }

                public Boolean GreenFilter
                {
                    get
                    {
                        return mOptGreenFilter;
                    }
                    set
                    {
                        if (mOptGreenFilter != value)
                        {
                            mOptGreenFilter = value;
                            ChangeBrightness(0);
                        }
                    }
                }

                public Int32 CursorX
                {
                    get { return mX; }
                }

                public Int32 CursorY
                {
                    get { return mY; }
                }

                public Byte Char
                {
                    get { return this[mX, mY]; }
                    set { this[mX, mY] = value; }
                }

                public Byte this[Int32 x, Int32 y]
                {
                    get
                    {
                        if ((x < 0) || (x >= COLS)) throw new ArgumentOutOfRangeException("x");
                        if ((y < 0) || (y >= ROWS)) throw new ArgumentOutOfRangeException("y");
                        return mChars[y * COLS + x];
                    }
                    set
                    {
                        if ((x < 0) || (x >= COLS)) throw new ArgumentOutOfRangeException("x");
                        if ((y < 0) || (y >= ROWS)) throw new ArgumentOutOfRangeException("y");
                        Int32 p = y * COLS + x;
                        if (mChars[p] == value) return;
                        mChars[p] = value;
                        p = value * 8;
                        if (p >= CharGen.Length) return;
                        lock (mBitmap)
                        {
                            x *= PIXELS_PER_COL;
                            y *= PIXELS_PER_ROW;
                            Int32 q = y * COLS * PIXELS_PER_COL + x + 1;
                            for (Int32 dy = 0; dy < 8; dy++)
                            {
                                Byte b = CharGen[p++];
                                Byte m = 64;
                                for (Int32 dx = 0; dx < 7; dx++)
                                {
                                    mPixMap[q + dx] = ((b & m) == 0) ? mOffColor : mOnColor;
                                    m >>= 1;
                                }
                                q += COLS * PIXELS_PER_COL * 2;
                            }
                            mBitmapDirty = true;
                        }
                    }
                }

                public void MoveCursorRel(Int32 dx, Int32 dy)
                {
                    Int32 x = mX + dx;
                    if (x < 0) x = 0; else if (x >= COLS) x = COLS - 1;
                    Int32 y = mY + dy;
                    if (y < 0) y = 0; else if (y >= ROWS) y = ROWS - 1;
                    if ((x != mX) || (y != mY)) MoveCursorAbs(x, y);
                }

                public void MoveCursorAbs(Int32 x, Int32 y)
                {
                    if ((x < 0) || (x >= COLS)) throw new ArgumentOutOfRangeException("x");
                    if ((y < 0) || (y >= ROWS)) throw new ArgumentOutOfRangeException("y");
                    lock (mBitmap)
                    {
                        if (mCursorVisible)
                        {
                            DrawCursor(mOffColor);
                        }
                        mX = x;
                        mY = y;
                        if (mCursorVisible)
                        {
                            DrawCursor(mOnColor);
                            mBitmapDirty = true;
                        }
                    }
                }

                // this needs to be rewritten to preserve the desired tint even when brightening from zero
                public void ChangeBrightness(Int32 delta)
                {
                    mBrightness += delta;
                    if (mBrightness < 5) mBrightness = 5;
                    else if (mBrightness > 100) mBrightness = 100;
                    UInt32 old = mOnColor;
                    mOnColor = Color(mBrightness);
                    ReplacePixels(old, mOnColor);
                }

                public void Beep()
                {
                    SystemSounds.Beep.Play();
                }

                // P4 phosphor colors (CIE chromaticity coordinates: x=0.275 y=0.290)
                private UInt32 Color(Int32 brightness)
                {
                    if ((brightness < 0) || (brightness > 100)) throw new ArgumentOutOfRangeException("brightness");
                    UInt32 c;
                    switch (brightness)
                    {
                        case 100: c = 0xFFE6FFFF; break;
                        case 95: c = 0xFFDAF5FF; break;
                        case 90: c = 0xFFCFE8FF; break;
                        case 85: c = 0xFFC4DCFF; break;
                        case 80: c = 0xFFB8CFF1; break;
                        case 75: c = 0xFFADC2E2; break;
                        case 70: c = 0xFFA2B6D3; break;
                        case 65: c = 0xFF96A9C5; break;
                        case 60: c = 0xFF8A9CB6; break;
                        case 55: c = 0xFF7F8FA7; break;
                        case 50: c = 0xFF738298; break;
                        case 45: c = 0xFF677488; break;
                        case 40: c = 0xFF5B6779; break;
                        case 35: c = 0xFF4F5A69; break;
                        case 30: c = 0xFF434C5A; break;
                        case 25: c = 0xFF363E4A; break;
                        case 20: c = 0xFF2A3039; break;
                        case 15: c = 0xFF1D2229; break;
                        case 10: c = 0xFF0f1318; break;
                        case 5: c = 0xFF040506; break;
                        case 0: c = 0xFF000000; break;
                        default: c = 0; break;
                    }
                    if (mOptGreenFilter)
                    {
                        // green filter passes 100% green, 6.25% blue, 6.25% red
                        UInt32 b = (c & 0x000000FF) >> 4;
                        c >>= 8;
                        UInt32 g = (c & 0x000000FF);
                        c >>= 8;
                        UInt32 r = (c & 0x000000FF) >> 4;
                        c &= 0xFFFFFF00;
                        c |= r;
                        c <<= 8;
                        c |= g;
                        c <<= 8;
                        c |= b;
                    }
                    return c;
                }

                private void ReplacePixels(UInt32 oldColor, UInt32 newColor)
                {
                    if (oldColor == newColor) return;
                    lock (mBitmap)
                    {
                        for (Int32 i = 0; i < mPixMap.Length; i++) if (mPixMap[i] == oldColor) mPixMap[i] = newColor;
                        mBitmapDirty = true;
                    }
                }

                private void CursorTimer_Callback(Object state)
                {
                    lock (mBitmap)
                    {
                        mCursorVisible = !mCursorVisible;
                        DrawCursor(mCursorVisible ? mOnColor : mOffColor);
                        mBitmapDirty = true;
                    }
                }

                private void DrawCursor(UInt32 color)
                {
                    Int32 x = mX * PIXELS_PER_COL;
                    Int32 y = mY * PIXELS_PER_ROW + 16;
                    Int32 p = y * COLS * PIXELS_PER_COL + x;
                    for (Int32 dx = 0; dx < 9; dx++) mPixMap[p + dx] = color;
                }

                // VT-52 Character Generator ROM
                // ROM size: 8Kb (1024x8) (only 7 output bits wired)
                // A2-A0 = char scan line 0..7 (8 scan lines per char)
                // A9-A3 = char number (ASCII code)
                // D6-D0 = char scan line pixels (MSB=first pixel)
                // ASCII codes below 32 contain Graphics Mode chars
                static private readonly Byte[] CharGen = {
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x30, 0x40, 0x41, 0x31, 0x07, 0x09, 0x07,
                    0x7f, 0x7f, 0x7f, 0x7f, 0x7f, 0x7f, 0x7f, 0x7f,
                    0x00, 0x21, 0x61, 0x22, 0x22, 0x74, 0x04, 0x08,
                    0x00, 0x71, 0x09, 0x32, 0x0a, 0x74, 0x04, 0x08,
                    0x00, 0x79, 0x41, 0x72, 0x0a, 0x74, 0x04, 0x08,
                    0x00, 0x79, 0x09, 0x12, 0x22, 0x44, 0x04, 0x08,
                    0x00, 0x18, 0x24, 0x18, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x08, 0x08, 0x7f, 0x08, 0x08, 0x7f,
                    0x00, 0x00, 0x04, 0x02, 0x7f, 0x02, 0x04, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x49,
                    0x00, 0x00, 0x08, 0x00, 0x7f, 0x00, 0x08, 0x00,
                    0x00, 0x08, 0x08, 0x49, 0x2a, 0x1c, 0x08, 0x00,
                    0x7f, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x7f, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x7f, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x7f, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x7f, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x7f, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7f, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7f,
                    0x00, 0x00, 0x00, 0x30, 0x48, 0x48, 0x48, 0x30,
                    0x00, 0x00, 0x00, 0x20, 0x60, 0x20, 0x20, 0x70,
                    0x00, 0x00, 0x00, 0x70, 0x08, 0x30, 0x40, 0x78,
                    0x00, 0x00, 0x00, 0x70, 0x08, 0x30, 0x08, 0x70,
                    0x00, 0x00, 0x00, 0x10, 0x30, 0x50, 0x78, 0x10,
                    0x00, 0x00, 0x00, 0x78, 0x40, 0x70, 0x08, 0x70,
                    0x00, 0x00, 0x00, 0x38, 0x40, 0x70, 0x48, 0x30,
                    0x00, 0x00, 0x00, 0x78, 0x08, 0x10, 0x20, 0x40,
                    0x00, 0x00, 0x00, 0x30, 0x48, 0x30, 0x48, 0x30,
                    0x00, 0x00, 0x00, 0x30, 0x48, 0x38, 0x08, 0x70,
                    0x00, 0x3f, 0x7a, 0x7a, 0x3a, 0x0a, 0x0a, 0x0a,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x08, 0x08, 0x08, 0x08, 0x08, 0x00, 0x08,
                    0x00, 0x14, 0x14, 0x14, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x14, 0x14, 0x7f, 0x14, 0x7f, 0x14, 0x14,
                    0x00, 0x08, 0x3e, 0x48, 0x3e, 0x09, 0x3e, 0x08,
                    0x00, 0x61, 0x62, 0x04, 0x08, 0x10, 0x23, 0x43,
                    0x00, 0x1c, 0x22, 0x14, 0x08, 0x15, 0x22, 0x1d,
                    0x00, 0x0c, 0x08, 0x10, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x04, 0x08, 0x10, 0x10, 0x10, 0x08, 0x04,
                    0x00, 0x10, 0x08, 0x04, 0x04, 0x04, 0x08, 0x10,
                    0x00, 0x08, 0x49, 0x2a, 0x1c, 0x2a, 0x49, 0x08,
                    0x00, 0x00, 0x08, 0x08, 0x7f, 0x08, 0x08, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x18, 0x10, 0x20,
                    0x00, 0x00, 0x00, 0x00, 0x7f, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x18, 0x18,
                    0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40,
                    0x00, 0x1c, 0x22, 0x45, 0x49, 0x51, 0x22, 0x1c,
                    0x00, 0x08, 0x18, 0x28, 0x08, 0x08, 0x08, 0x3e,
                    0x00, 0x3c, 0x42, 0x01, 0x0e, 0x30, 0x40, 0x7f,
                    0x00, 0x7f, 0x02, 0x04, 0x0e, 0x01, 0x41, 0x3e,
                    0x00, 0x04, 0x0c, 0x14, 0x24, 0x7f, 0x04, 0x04,
                    0x00, 0x7f, 0x40, 0x5e, 0x61, 0x01, 0x41, 0x3e,
                    0x00, 0x1e, 0x21, 0x40, 0x5e, 0x61, 0x21, 0x1e,
                    0x00, 0x7f, 0x01, 0x02, 0x04, 0x08, 0x10, 0x20,
                    0x00, 0x3e, 0x41, 0x41, 0x3e, 0x41, 0x41, 0x3e,
                    0x00, 0x3c, 0x42, 0x43, 0x3d, 0x01, 0x42, 0x3c,
                    0x00, 0x00, 0x18, 0x18, 0x00, 0x00, 0x18, 0x18,
                    0x00, 0x00, 0x18, 0x18, 0x00, 0x18, 0x10, 0x20,
                    0x00, 0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02,
                    0x00, 0x00, 0x00, 0x7f, 0x00, 0x7f, 0x00, 0x00,
                    0x00, 0x20, 0x10, 0x08, 0x04, 0x08, 0x10, 0x20,
                    0x00, 0x3e, 0x41, 0x01, 0x0e, 0x08, 0x00, 0x08,
                    0x00, 0x3e, 0x41, 0x45, 0x49, 0x4e, 0x40, 0x3e,
                    0x00, 0x08, 0x14, 0x22, 0x41, 0x7f, 0x41, 0x41,
                    0x00, 0x7e, 0x21, 0x21, 0x3e, 0x21, 0x21, 0x7e,
                    0x00, 0x1e, 0x21, 0x40, 0x40, 0x40, 0x21, 0x1e,
                    0x00, 0x7e, 0x21, 0x21, 0x21, 0x21, 0x21, 0x7e,
                    0x00, 0x7f, 0x40, 0x40, 0x78, 0x40, 0x40, 0x7f,
                    0x00, 0x7f, 0x40, 0x40, 0x78, 0x40, 0x40, 0x40,
                    0x00, 0x3e, 0x41, 0x40, 0x47, 0x41, 0x41, 0x3e,
                    0x00, 0x41, 0x41, 0x41, 0x7f, 0x41, 0x41, 0x41,
                    0x00, 0x3e, 0x08, 0x08, 0x08, 0x08, 0x08, 0x3e,
                    0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x41, 0x3e,
                    0x00, 0x41, 0x46, 0x58, 0x60, 0x58, 0x46, 0x41,
                    0x00, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x7f,
                    0x00, 0x41, 0x63, 0x55, 0x49, 0x41, 0x41, 0x41,
                    0x00, 0x41, 0x61, 0x51, 0x49, 0x45, 0x43, 0x41,
                    0x00, 0x3e, 0x41, 0x41, 0x41, 0x41, 0x41, 0x3e,
                    0x00, 0x7e, 0x41, 0x41, 0x7e, 0x40, 0x40, 0x40,
                    0x00, 0x3e, 0x41, 0x41, 0x41, 0x45, 0x42, 0x3d,
                    0x00, 0x7e, 0x41, 0x41, 0x7e, 0x44, 0x42, 0x41,
                    0x00, 0x3e, 0x41, 0x40, 0x3e, 0x01, 0x41, 0x3e,
                    0x00, 0x7f, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08,
                    0x00, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x3e,
                    0x00, 0x41, 0x41, 0x22, 0x22, 0x14, 0x14, 0x08,
                    0x00, 0x41, 0x41, 0x41, 0x49, 0x49, 0x55, 0x22,
                    0x00, 0x41, 0x22, 0x14, 0x08, 0x14, 0x22, 0x41,
                    0x00, 0x41, 0x22, 0x14, 0x08, 0x08, 0x08, 0x08,
                    0x00, 0x7f, 0x02, 0x04, 0x08, 0x10, 0x20, 0x7f,
                    0x00, 0x3c, 0x30, 0x30, 0x30, 0x30, 0x30, 0x3c,
                    0x00, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01,
                    0x00, 0x1e, 0x06, 0x06, 0x06, 0x06, 0x06, 0x1e,
                    0x00, 0x08, 0x14, 0x22, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7f,
                    0x00, 0x18, 0x08, 0x04, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x3e, 0x01, 0x3f, 0x41, 0x3f,
                    0x00, 0x40, 0x40, 0x5e, 0x61, 0x41, 0x61, 0x5e,
                    0x00, 0x00, 0x00, 0x3e, 0x41, 0x40, 0x40, 0x3f,
                    0x00, 0x01, 0x01, 0x3d, 0x43, 0x41, 0x43, 0x3d,
                    0x00, 0x00, 0x00, 0x3c, 0x42, 0x7f, 0x40, 0x3e,
                    0x00, 0x0e, 0x11, 0x7c, 0x10, 0x10, 0x10, 0x10,
                    0x00, 0x00, 0x00, 0x1d, 0x22, 0x1e, 0x42, 0x3c,
                    0x00, 0x40, 0x40, 0x7e, 0x41, 0x41, 0x41, 0x41,
                    0x00, 0x08, 0x00, 0x18, 0x08, 0x08, 0x08, 0x3e,
                    0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x41, 0x3e,
                    0x00, 0x40, 0x40, 0x44, 0x48, 0x50, 0x44, 0x41,
                    0x00, 0x18, 0x08, 0x08, 0x08, 0x08, 0x08, 0x1c,
                    0x00, 0x00, 0x00, 0x76, 0x49, 0x49, 0x49, 0x49,
                    0x00, 0x00, 0x00, 0x5e, 0x61, 0x41, 0x41, 0x41,
                    0x00, 0x00, 0x00, 0x3e, 0x41, 0x41, 0x41, 0x3e,
                    0x00, 0x00, 0x00, 0x5e, 0x61, 0x7e, 0x40, 0x40,
                    0x00, 0x00, 0x00, 0x3d, 0x43, 0x3f, 0x01, 0x01,
                    0x00, 0x00, 0x00, 0x4e, 0x31, 0x20, 0x20, 0x20,
                    0x00, 0x00, 0x00, 0x3e, 0x40, 0x3e, 0x01, 0x7e,
                    0x00, 0x10, 0x10, 0x7c, 0x10, 0x10, 0x12, 0x0c,
                    0x00, 0x00, 0x00, 0x42, 0x42, 0x42, 0x42, 0x3d,
                    0x00, 0x00, 0x00, 0x41, 0x41, 0x22, 0x14, 0x08,
                    0x00, 0x00, 0x00, 0x41, 0x41, 0x49, 0x55, 0x22,
                    0x00, 0x00, 0x00, 0x42, 0x24, 0x18, 0x24, 0x42,
                    0x00, 0x00, 0x00, 0x41, 0x22, 0x14, 0x08, 0x70,
                    0x00, 0x00, 0x00, 0x7f, 0x02, 0x1c, 0x20, 0x7f,
                    0x00, 0x07, 0x08, 0x08, 0x70, 0x08, 0x08, 0x07,
                    0x00, 0x08, 0x08, 0x08, 0x00, 0x08, 0x08, 0x08,
                    0x00, 0x70, 0x08, 0x08, 0x07, 0x08, 0x08, 0x70,
                    0x00, 0x11, 0x2a, 0x44, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                };
            }
        }


        // Terminal-I/O Interface and UART Timing

        public partial class VT52
        {
            private UART mUART;                     // UART emulator
            private String mCaption;                // desired window title bar caption
            private volatile Boolean mCaptionDirty; // true if caption has changed

            // called by main UI thread via constructor
            private void InitIO()
            {
                mUART = new UART(this);
                mUART.IO = new IO.Loopback(null);
                mCaption = String.Concat("VT52 - ", mUART.IO.ConnectionString);
                mCaptionDirty = true;
            }

            public override String Caption
            {
                get { return mCaption; }
            }

            public override Boolean CaptionDirty
            {
                get { return mCaptionDirty; }
                set { mCaptionDirty = value; }
            }

            public override void Shutdown()
            {
                mUART.IO.Close();
            }

            private void SetBreakState(Boolean asserted)
            {
                mUART.IO.SetBreak(asserted);
            }

            private void SetTransmitSpeed(Int32 baudRate)
            {
                mUART.SetTransmitSpeed(baudRate);
            }

            private void SetReceiveSpeed(Int32 baudRate)
            {
                mUART.SetReceiveSpeed(baudRate);
            }

            private void SetTransmitParity(System.IO.Ports.Parity parity)
            {
                mUART.SetTransmitParity(parity);
            }

            private void Send(Byte data)
            {
                mUART.Send(data);
            }

            private class UART
            {
                private VT52 mVT52;             // for calling parent methods
                private Queue<Byte> mSendQueue; // bytes waiting to be fully sent by UART
                private Timer mSendTimer;       // UART byte transmit timer
                private Boolean mSendBusy;      // UART is transmitting bits
                private Int32 mSendSpeed;       // UART transmit baud rate
                private Double mSendRate;       // UART byte transmit rate
                private Int32 mSendPeriod;      // time (ms) for UART to send one byte
                private DateTime mSendClock;    // UART transmit clock
                private Int32 mSendCount;       // bytes transmitted since clock
                private Queue<Byte> mRecvQueue; // bytes waiting to be fully received by UART
                private Timer mRecvTimer;       // UART byte receive timer
                private Boolean mRecvBusy;      // UART is receiving bits
                private Int32 mRecvSpeed;       // UART receive baud rate
                private Double mRecvRate;       // UART byte receive rate
                private Int32 mRecvPeriod;      // time (ms) for UART to receive one byte
                private DateTime mRecvClock;    // UART receive clock
                private Int32 mRecvCount;       // bytes received since clock
                private Boolean mRecvBreak;     // receive break state
                private System.IO.Ports.Parity mParity;
                private IO mIO;                 // I/O interface

                public UART(VT52 parent)
                {
                    mVT52 = parent;
                    mSendQueue = new Queue<Byte>();
                    mSendTimer = new Timer(SendTimer_Callback, this, Timeout.Infinite, Timeout.Infinite);
                    SetTransmitSpeed(9600);
                    mRecvQueue = new Queue<Byte>();
                    mRecvTimer = new Timer(RecvTimer_Callback, this, Timeout.Infinite, Timeout.Infinite);
                    SetReceiveSpeed(9600);
                    SetTransmitParity(System.IO.Ports.Parity.Space);
                }

                public IO IO
                {
                    get
                    {
                        return mIO;
                    }
                    set
                    {
                        if (mIO == value) return;
                        if (mIO != null) mIO.Close();
                        mIO = value;
                        if (mIO != null) mIO.IOEvent += IOEvent;
                    }
                }

                public Int32 TransmitSpeed
                {
                    get { return mSendSpeed; }
                    set { SetTransmitSpeed(value); }
                }

                public Int32 ReceiveSpeed
                {
                    get { return mRecvSpeed; }
                    set { SetReceiveSpeed(value); }
                }

                public void SetTransmitSpeed(Int32 baudRate)
                {
                    lock (mSendQueue)
                    {
                        switch (baudRate)
                        {
                            case 0:
                                mSendSpeed = 0;
                                break;
                            case 19200:
                                mSendSpeed = 19200;
                                mSendRate = 1920;
                                mSendPeriod = 1;
                                break;
                            case 9600:
                                mSendSpeed = 9600;
                                mSendRate = 960;
                                mSendPeriod = 1;
                                break;
                            case 4800:
                                mSendSpeed = 4800;
                                mSendRate = 480;
                                mSendPeriod = 2;
                                break;
                            case 2400:
                                mSendSpeed = 2400;
                                mSendRate = 240;
                                mSendPeriod = 4;
                                break;
                            case 1200:
                                mSendSpeed = 1200;
                                mSendRate = 120;
                                mSendPeriod = 8;
                                break;
                            case 600:
                                mSendSpeed = 600;
                                mSendRate = 60;
                                mSendPeriod = 16;
                                break;
                            case 300:
                                mSendSpeed = 300;
                                mSendRate = 30;
                                mSendPeriod = 33;
                                break;
                            case 150:
                                mSendSpeed = 150;
                                mSendRate = 15;
                                mSendPeriod = 66;
                                break;
                            case 110:
                                mSendSpeed = 110;
                                mSendRate = 10;
                                mSendPeriod = 100;
                                break;
                            case 75:
                                mSendSpeed = 75;
                                mSendRate = 7.5;
                                mSendPeriod = 133;
                                break;
                            default:
                                throw new ArgumentException("baudRate");
                        }
                        if (mSendBusy)
                        {
                            mSendClock = DateTime.UtcNow;
                            mSendCount = 0;
                        }
                    }
                }

                public void SetReceiveSpeed(Int32 baudRate)
                {
                    lock (mRecvQueue)
                    {
                        switch (baudRate)
                        {
                            case 0:
                                mRecvSpeed = 0;
                                break;
                            case 19200:
                                mRecvSpeed = 19200;
                                mRecvRate = 1920;
                                mRecvPeriod = 1;
                                break;
                            case 9600:
                                mRecvSpeed = 9600;
                                mRecvRate = 960;
                                mRecvPeriod = 1;
                                break;
                            case 4800:
                                mRecvSpeed = 4800;
                                mRecvRate = 480;
                                mRecvPeriod = 2;
                                break;
                            case 2400:
                                mRecvSpeed = 2400;
                                mRecvRate = 240;
                                mRecvPeriod = 4;
                                break;
                            case 1200:
                                mRecvSpeed = 1200;
                                mRecvRate = 120;
                                mRecvPeriod = 8;
                                break;
                            case 600:
                                mRecvSpeed = 600;
                                mRecvRate = 60;
                                mRecvPeriod = 16;
                                break;
                            case 300:
                                mRecvSpeed = 300;
                                mRecvRate = 30;
                                mRecvPeriod = 33;
                                break;
                            case 150:
                                mRecvSpeed = 150;
                                mRecvRate = 15;
                                mRecvPeriod = 66;
                                break;
                            case 110:
                                mRecvSpeed = 110;
                                mRecvRate = 10;
                                mRecvPeriod = 100;
                                break;
                            case 75:
                                mRecvSpeed = 75;
                                mRecvRate = 7.5;
                                mRecvPeriod = 133;
                                break;
                            default:
                                throw new ArgumentException("baudRate");
                        }
                        if (mRecvBusy)
                        {
                            mRecvClock = DateTime.UtcNow;
                            mRecvCount = 0;
                        }
                    }
                }

                public void SetTransmitParity(System.IO.Ports.Parity parity)
                {
                    mParity = parity;
                }

                private Int32 NybbleParity(Int32 data)
                {
                    switch (data & 0x0F)
                    {
                        case 0x00: return 0;
                        case 0x01: return 1;
                        case 0x02: return 1;
                        case 0x03: return 0;
                        case 0x04: return 1;
                        case 0x05: return 0;
                        case 0x06: return 0;
                        case 0x07: return 1;
                        case 0x08: return 1;
                        case 0x09: return 0;
                        case 0x0A: return 0;
                        case 0x0B: return 1;
                        case 0x0C: return 0;
                        case 0x0D: return 1;
                        case 0x0E: return 1;
                        case 0x0F: return 0;
                        default: throw new ArgumentOutOfRangeException();
                    }
                }

                public void Send(Byte data)
                {
                    switch (mParity)
                    {
                        case System.IO.Ports.Parity.None:
                        case System.IO.Ports.Parity.Space:
                            break;

                        case System.IO.Ports.Parity.Mark:
                            data |= 0x80;
                            break;

                        case System.IO.Ports.Parity.Odd:
                            if ((NybbleParity(data >> 4) + NybbleParity(data)) != 1) data |= 0x80;
                            break;

                        case System.IO.Ports.Parity.Even:
                            if ((NybbleParity(data >> 4) + NybbleParity(data)) == 1) data |= 0x80;
                            break;
                    }

                    lock (mSendQueue)
                    {
                        if (mSendSpeed == 0)
                        {
                            mIO.Send(data);
                            return;
                        }
                        if ((!mSendBusy) && (!mIO.DelaySend)) mIO.Send(data);
                        else mSendQueue.Enqueue(data);
                        if (!mSendBusy)
                        {
                            mSendBusy = true;
                            mSendClock = DateTime.UtcNow;
                            mSendCount = 0;
                            mSendTimer.Change(0, mSendPeriod);
                        }
                    }
                }

                private void IOEvent(Object sender, IOEventArgs e)
                {
#if DEBUG
                    Log.WriteLine("IOEvent: {0} {1} (0x{2:X2})", e.Type, (Char)e.Value, e.Value);
#endif
                    switch (e.Type)
                    {
                        case IOEventType.Data:
                            Byte data = (Byte)(e.Value & 0x7F); // ignore received parity
                            lock (mRecvQueue)
                            {
                                if (mRecvSpeed == 0)
                                {
                                    mVT52.Recv(data);
                                    return;
                                }
                                if ((!mRecvBusy) && (!mIO.DelayRecv)) mVT52.Recv(data);
                                else mRecvQueue.Enqueue(data);
                                if (!mRecvBusy)
                                {
                                    mRecvBusy = true;
                                    mRecvClock = DateTime.UtcNow;
                                    mRecvCount = 0;
                                    mRecvTimer.Change(0, mRecvPeriod);
                                }
                            }
                            break;
                        case IOEventType.Break:
                            lock (mRecvQueue) mRecvBreak = (e.Value != 0);
                            break;
                        case IOEventType.Flush:
                            lock (mRecvQueue) mRecvQueue.Clear();
                            break;
                        case IOEventType.Disconnect:
                            lock (mRecvQueue) mRecvQueue.Clear();
                            IO = new IO.Loopback(null);
                            mVT52.mCaption = String.Concat("VT52 - ", IO.ConnectionString);
                            mVT52.mCaptionDirty = true;
                            break;
                    }
                }

                private void SendTimer_Callback(Object state)
                {
                    lock (mSendQueue)
                    {
                        TimeSpan t = DateTime.UtcNow.Subtract(mSendClock);
                        Int32 due = (Int32)(t.TotalSeconds * mSendRate + 0.5) - mSendCount;
#if DEBUG
                        Log.WriteLine("SendTimer_Callback: due={0:D0} ct={1:D0}", due, mSendQueue.Count);
#endif
                        if (due <= 0) return;
                        while ((due-- > 0) && (mSendQueue.Count != 0))
                        {
                            mSendCount++;
                            mIO.Send(mSendQueue.Dequeue());
                        }
                        if (mSendQueue.Count == 0)
                        {
                            mSendTimer.Change(Timeout.Infinite, Timeout.Infinite);
                            mSendBusy = false;
                        }
                        else if (t.Minutes != 0)
                        {
                            mSendClock = DateTime.UtcNow;
                            mSendCount = 0;
                        }
                    }
                }

                private void RecvTimer_Callback(Object state)
                {
                    lock (mRecvQueue)
                    {
                        TimeSpan t = DateTime.UtcNow.Subtract(mRecvClock);
                        Int32 due = (Int32)(t.TotalSeconds * mRecvRate + 0.5) - mRecvCount;
#if DEBUG
                        Log.WriteLine("RecvTimer_Callback: due={0:D0} ct={1:D0}", due, mRecvQueue.Count);
#endif
                        if (due <= 0) return;
                        while ((due-- > 0) && (mRecvQueue.Count != 0))
                        {
                            mRecvCount++;
                            mVT52.Recv(mRecvQueue.Dequeue());
                        }
                        if (mRecvQueue.Count == 0)
                        {
                            mRecvTimer.Change(Timeout.Infinite, Timeout.Infinite);
                            mRecvBusy = false;
                        }
                        else if (t.Minutes != 0)
                        {
                            mRecvClock = DateTime.UtcNow;
                            mRecvCount = 0;
                        }
                    }
                }
            }
        }
    }
}
