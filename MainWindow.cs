// MainWindow.cs
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
using System.Windows.Forms;

namespace Emulator
{
    // Main Terminal Emulator Window [Main UI Thread]

    public partial class MainWindow : Form
    {
        private Terminal mTerminal;
        private Timer mRefreshTimer;

        public MainWindow()
        {
            InitializeComponent();
        }

        // hooking WndProc is needed to differentiate Enter from Keypad Enter
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0100:    // WM_KEYDOWN
                case 0x0101:    // WM_KEYUP
                    if (!mTerminal.KeyEvent(m.Msg, m.WParam, m.LParam)) base.WndProc(ref m);
                    break;
                case 0x0102:    // WM_CHAR
                case 0x0104:    // WM_SYSKEYDOWN
                case 0x0105:    // WM_SYSKEYUP
                case 0x0109:    // WM_UNICHAR
                default:
                    base.WndProc(ref m);
                    break;
            }
        }

        private void MainWindow_Load(Object sender, EventArgs e)
        {
            Int32 dx = this.Width - TerminalImage.Width;
            Int32 dy = this.Height - TerminalImage.Height;
            mTerminal = new Terminal.VT52();
            TerminalImage.Image = mTerminal.Bitmap;
            TerminalImage.Width = mTerminal.Bitmap.Width;
            TerminalImage.Height = mTerminal.Bitmap.Height;
            this.Width = TerminalImage.Width + dx;
            this.Height = TerminalImage.Height + dy;
            this.MinimumSize = new System.Drawing.Size(this.Width, this.Height);
            mRefreshTimer = new Timer();
            mRefreshTimer.Tick += new EventHandler(MainWindow_RefreshTimer);
            mRefreshTimer.Interval = 16; // 62.5Hz (60Hz = 16.667ms)
            mRefreshTimer.Enabled = true;
        }

        private void MainWindow_FormClosing(Object sender, FormClosingEventArgs e)
        {
            mTerminal.Shutdown();
        }

        private void MainWindow_RefreshTimer(Object sender, EventArgs e)
        {
            if (mTerminal.CaptionDirty)
            {
                this.Text = mTerminal.Caption;
                mTerminal.CaptionDirty = false;
            }
            if (mTerminal.BitmapDirty)
            {
                lock (mTerminal.Bitmap) TerminalImage.Refresh();
                mTerminal.BitmapDirty = false;
            }
        }
    }
}