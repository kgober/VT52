// Terminal.cs
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
using System.Drawing;

namespace Emulator
{
    abstract public partial class Terminal
    {
        // Bitmap - contains image of emulated terminal screen
        public abstract Bitmap Bitmap { get; }

        // BitmapDirty - Terminal sets to true on update, MainWindow sets to false on refresh
        public abstract Boolean BitmapDirty { get; set; }

        // Caption - contains desired title bar caption for MainWindow
        public abstract String Caption { get; }

        // CaptionDirty - Terminal sets to true on update, MainWindow sets to false on refresh
        public abstract Boolean CaptionDirty { get; set; }

        // KeyEvent - called by MainWindow (on UI thread) for keyboard events
        //   Returns true if event was handled
        public abstract Boolean KeyEvent(Int32 msgId, IntPtr wParam, IntPtr lParam);

        // MenuEvent - called by MainWindow (on UI thread) for menu events
        //   Returns true if event was handled
        public abstract Boolean MenuEvent(Int32 msgId, IntPtr wParam, IntPtr lParam);

        // Paste - called by MainWindow (on UI thread) to paste from clipboard
        public abstract void Paste(String text);

        // Shutdown - called by MainWindow (on UI thread) on shutdown
        public abstract void Shutdown();
    }
}
