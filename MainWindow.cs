// MainWindow.cs
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
    // Main Terminal Emulator Window [Main UI Thread]

    public partial class MainWindow : Form
    {
        private Terminal mTerminal;
        private Timer mRefreshTimer;

        public MainWindow()
        {
            Program.Name = "VT52";
            this.Text = Program.Name;
            InitializeComponent();
        }

        public Boolean FixedAspectRatio
        {
            get { return (TerminalImage.SizeMode != PictureBoxSizeMode.StretchImage); }
            set { TerminalImage.SizeMode = (value) ? PictureBoxSizeMode.Zoom : PictureBoxSizeMode.StretchImage; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            IntPtr hMenu = Win32.GetSystemMenu(this.Handle, false);
            Win32.AppendMenu(hMenu, MF.SEPARATOR, (UIntPtr)0);
            Win32.AppendMenu(hMenu, MF.STRING, (UIntPtr)5, "Settings\tF5");
            Win32.AppendMenu(hMenu, MF.STRING, (UIntPtr)6, "Connection\tF6");
            Win32.AppendMenu(hMenu, MF.STRING, (UIntPtr)11, "Brightness -\tF11");
            Win32.AppendMenu(hMenu, MF.STRING, (UIntPtr)12, "Brightness +\tF12");
            Win32.AppendMenu(hMenu, MF.SEPARATOR, (UIntPtr)0);
            Win32.AppendMenu(hMenu, MF.STRING, (UIntPtr)99, String.Concat("About ", Program.Name));
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0100:    // WM_KEYDOWN
                case 0x0101:    // WM_KEYUP
                    if (!mTerminal.KeyEvent(m.Msg, m.WParam, m.LParam)) base.WndProc(ref m);
                    break;
                case 0x0112:    // WM_SYSCOMMAND
                    if (!mTerminal.MenuEvent(m.Msg, m.WParam, m.LParam)) base.WndProc(ref m);
                    break;
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

        private void MainWindow_MouseDown(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("MouseDown: Button={0} XY={1:D0},{2:D0} Loc={3:D0},{4:D0}", e.Button.ToString(), e.X, e.Y, e.Location.X, e.Location.Y);
        }

        private void MainWindow_MouseUp(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("MouseUp: Button={0} XY={1:D0},{2:D0} Loc={3:D0},{4:D0}", e.Button.ToString(), e.X, e.Y, e.Location.X, e.Location.Y);
            if (e.Button == MouseButtons.Right)
            {
                String text = Clipboard.GetText();
                if ((text == null) || (text.Length == 0)) return;
                mTerminal.Paste(text);
            }
        }

        private void TerminalImage_MouseDown(object sender, MouseEventArgs e)
        {
            Int32 px = e.X + (sender as PictureBox).Location.X;
            Int32 py = e.Y + (sender as PictureBox).Location.Y;
            this.OnMouseDown(new MouseEventArgs(e.Button, e.Clicks, px, py, e.Delta));
        }

        private void TerminalImage_MouseUp(object sender, MouseEventArgs e)
        {
            Int32 px = e.X + (sender as PictureBox).Location.X;
            Int32 py = e.Y + (sender as PictureBox).Location.Y;
            this.OnMouseUp(new MouseEventArgs(e.Button, e.Clicks, px, py, e.Delta));
        }
    }
}