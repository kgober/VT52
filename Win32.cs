// Win32.cs
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
using System.Runtime.InteropServices;

namespace Emulator
{
    [Flags]
    public enum BAUD : int
    {
        _075 = 0x00000001,
        _110 = 0x00000002,
        _134_5 = 0x00000004,
        _150 = 0x00000008,
        _300 = 0x00000010,
        _600 = 0x00000020,
        _1200 = 0x00000040,
        _1800 = 0x00000080,
        _2400 = 0x00000100,
        _4800 = 0x00000200,
        _7200 = 0x00000400,
        _9600 = 0x00000800,
        _14400 = 0x00001000,
        _19200 = 0x00002000,
        _38400 = 0x00004000,
        _56K = 0x00008000,
        _128K = 0x00010000,
        _115200 = 0x00020000,
        _57600 = 0x00040000,
        USER = 0x10000000,
    }

    [Flags]
    public enum DATABITS : ushort
    {
        _5 = 0x0001,
        _6 = 0x0002,
        _7 = 0x0004,
        _8 = 0x0008,
        _16 = 0x0010,
        _16X = 0x0020,
    }

    [Flags]
    public enum KEYEVENTF : int
    {
        EXTENDEDKEY = 0x0001,
        KEYUP = 0x0002,
    }

    public enum MAPVK : uint
    {
        VK_TO_VSC = 0,
        VSC_TO_VK = 1,
        VK_TO_CHAR = 2,
        VSC_TO_VK_EX = 3,
    }

    [Flags]
    public enum MF : uint
    {
        STRING = 0x00000000,
        ENABLED = 0x00000000,
        GRAYED = 0x00000001,
        DISABLED = 0x00000002,
        BITMAP = 0x00000004,
        CHECKED = 0x00000008,
        POPUP = 0x00000010,
        MENUBARBREAK = 0x00000020,
        MENUBREAK = 0x00000040,
        OWNERDRAW = 0x00000100,
        SEPARATOR = 0x00000800,
    }

    [Flags]
    public enum PCF : int
    {
        DTRDSR = 0x0001,
        RTSCTS = 0x0002,
        RLSD = 0x0004,
        PARITY_CHECK = 0x0008,
        XONXOFF = 0x0010,
        SETXCHAR = 0x0020,
        TOTALTIMEOUTS = 0x0040,
        INTTIMEOUTS = 0x0080,
        SPECIALCHARS = 0x0100,
        _16BITMODE = 0x0200,
    }

    [Flags]
    public enum SP : int
    {
        PARITY = 0x0001,
        BAUD = 0x0002,
        DATABITS = 0x0004,
        STOPBITS = 0x0008,
        HANDSHAKING = 0x0010,
        PARITY_CHECK = 0x0020,
        RLSD = 0x0040,
    }

    [Flags]
    public enum SSP : ushort
    {
        STOPBITS_10 = 0x0001,
        STOPBITS_15 = 0x0002,
        STOPBITS_20 = 0x0004,
        STOPBITS_MASK = 0x0007,
        PARITY_NONE = 0x0100,
        PARITY_ODD = 0x0200,
        PARITY_EVEN = 0x0400,
        PARITY_MARK = 0x0800,
        PARITY_SPACE = 0x1000,
        PARITY_MASK = 0x1f00,
    }

    public enum VK : byte
    {
        BACK = 0x08,
        TAB = 0x09,
        RETURN = 0x0D,
        SHIFT = 0x10,
        CONTROL = 0x11,
        MENU = 0x12,
        ALT = 0x12,
        CAPITAL = 0x14,
        ESCAPE = 0x1B,
        SPACE = 0x20,
        NEXT = 0x22,
        END = 0x23,
        LEFT = 0x25,
        UP = 0x26,
        RIGHT = 0x27,
        DOWN = 0x28,
        INSERT = 0x2D,
        DELETE = 0x2E,
        K0 = 0x30,
        K1 = 0x31,
        K2 = 0x32,
        K3 = 0x33,
        K4 = 0x34,
        K5 = 0x35,
        K6 = 0x36,
        K7 = 0x37,
        K8 = 0x38,
        K9 = 0x39,
        A = 0x41,
        B = 0x42,
        C = 0x43,
        D = 0x44,
        E = 0x45,
        F = 0x46,
        G = 0x47,
        H = 0x48,
        I = 0x49,
        J = 0x4A,
        K = 0x4B,
        L = 0x4C,
        M = 0x4D,
        N = 0x4E,
        O = 0x4F,
        P = 0x50,
        Q = 0x51,
        R = 0x52,
        S = 0x53,
        T = 0x54,
        U = 0x55,
        V = 0x56,
        W = 0x57,
        X = 0x58,
        Y = 0x59,
        Z = 0x5A,
        NUMPAD0 = 0x60,
        NUMPAD1 = 0x61,
        NUMPAD2 = 0x62,
        NUMPAD3 = 0x63,
        NUMPAD4 = 0x64,
        NUMPAD5 = 0x65,
        NUMPAD6 = 0x66,
        NUMPAD7 = 0x67,
        NUMPAD8 = 0x68,
        NUMPAD9 = 0x69,
        MULTIPLY = 0x6A,
        ADD = 0x6B,
        SUBTRACT = 0x6D,
        DECIMAL = 0x6E,
        DIVIDE = 0x6F,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        F13 = 0x7C,
        F14 = 0x7D,
        F15 = 0x7E,
        F16 = 0x7F,
        F17 = 0x80,
        F18 = 0x81,
        F19 = 0x82,
        F20 = 0x83,
        F21 = 0x84,
        F22 = 0x85,
        F23 = 0x86,
        F24 = 0x87,
        ENTER = 0x8D,   // Unassigned in Windows
        NUMLOCK = 0x90,
        SCROLL = 0x91,
        LSHIFT = 0xA0,
        RSHIFT = 0xA1,
        LCONTROL = 0xA2,
        RCONTROL = 0xA3,
        LMENU = 0xA4,
        LALT = 0xA4,
        RMENU = 0xA5,
        RALT = 0xA5,
        OEM_1 = 0xBA,
        SEMICOLON = 0xBA,
        OEM_PLUS = 0xBB,
        EQUAL = 0xBB,
        OEM_COMMA = 0xBC,
        COMMA = 0xBC,
        OEM_MINUS = 0xBD,
        MINUS = 0xBD,
        OEM_PERIOD = 0xBE,
        PERIOD = 0XBE,
        OEM_2 = 0xBF,
        SLASH = 0xBF,
        OEM_3 = 0xC0,
        TILDE = 0xC0,
        OEM_4 = 0xDB,
        LBRACKET = 0xDB,
        OEM_5 = 0xDC,
        BACKSLASH = 0xDC,
        OEM_6 = 0xDD,
        RBRACKET = 0xDD,
        OEM_7 = 0xDE,
        QUOTE = 0xDE,
    }

    class Win32
    {
        [DllImport("user32.dll")]
        public static extern Boolean AppendMenu(
            IntPtr hMenu,
            MF uFlags,
            UIntPtr uIDNewItem);

        [DllImport("user32.dll")]
        public static extern Boolean AppendMenu(
            IntPtr hMenu,
            MF uFlags,
            UIntPtr uIDNewItem,
            String lpNewItem);

        [DllImport("user32.dll")]
        public static extern IntPtr GetSystemMenu(
            IntPtr hWnd,
            Boolean bRevert);

        [DllImport("user32.dll")]
        public static extern void keybd_event(
            VK bVk,
            Byte bScan,
            KEYEVENTF dwFlags,
            Int32 dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern UInt32 MapVirtualKey(
            UInt32 uCode,
            MAPVK uMapType);
    }
}
