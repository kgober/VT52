// Debug.cs
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
using System.Diagnostics;
using System.Text;

namespace Emulator
{
    class Debug
    {
        private static Object sLock = new Object();

        public static void WriteLine(String output)
        {
#if DEBUG
            StringBuilder buf = new StringBuilder(output.Length + 12);
            buf.Append(Program.Name).Append(": ");
            foreach (Char c in output)
            {
                switch (c)
                {
                    case '\x0': // 0 ^@ NUL
                        buf.Append(@"\0");
                        continue;
                    case '\a':  // 7 ^G BEL
                        buf.Append(@"\a");
                        continue;
                    case '\b':  // 8 ^H BS
                        buf.Append(@"\b");
                        continue;
                    case '\t':  // 9 ^I TAB
                        buf.Append(@"\t");
                        continue;
                    case '\n':  // 10 ^J LF
                        buf.Append(@"\n");
                        continue;
                    case '\f':  // 12 ^L FF
                        buf.Append(@"\f");
                        continue;
                    case '\r':  // 13 ^M CR
                        buf.Append(@"\r");
                        continue;
                    case '\x1b':// 27 ^[ ESC
                        buf.Append(@"\e");
                        continue;
                    case '\\':
                        buf.Append(@"\\");
                        continue;
                    default:
                        if ((c < 32) || (c >= 127))
                        {
                            buf.Append(@"\x").AppendFormat("{0:x2}", (Byte)c);
                            continue;
                        }
                        buf.Append(c);
                        continue;
                }
            }
            lock (sLock) Trace.WriteLine(buf.ToString());
#endif
        }

        public static void WriteLine(String format, params Object[] args)
        {
#if DEBUG
            WriteLine(String.Format(format, args));
#endif
        }
    }
}
