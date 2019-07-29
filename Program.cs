// Program.cs
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
using System.Windows.Forms;

namespace Emulator
{
    // Program Structure:  MainWindow <-> Terminal <-> IO
    //
    // MainWindow sends keyboard input to Terminal and displays Terminal output bitmap.
    // Terminal performs terminal emulation (including font, escape sequences, and UART).
    // IO connects emulated terminal UART to serial port, network socket, etc.

    static class Program
    {
        private static String[] mArgs;
        private static MainWindow mWindow;
        private static String mName;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(String[] args)
        {
            mArgs = args;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(mWindow = new MainWindow());
        }

        public static String[] Args
        {
            get { return mArgs; }
        }

        public static MainWindow Window
        {
            get { return mWindow; }
        }

        public static String Name
        {
            get { return mName; }
            set { mName = value; }
        }
    }
}