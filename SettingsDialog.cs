// SettingsDialog.cs
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
using System.ComponentModel;
using System.IO.Ports;
using System.Windows.Forms;

namespace Emulator
{
    public partial class SettingsDialog : Form
    {
        private Boolean mOK;

        public SettingsDialog()
        {
            InitializeComponent();
        }

        public Boolean OK
        {
            get { return mOK; }
        }

        public Char S1
        {
            get
            {
                if (radioButton1.Checked) return '1';
                if (radioButton2.Checked) return '2';
                if (radioButton3.Checked) return '3';
                if (radioButton4.Checked) return '4';
                if (radioButton5.Checked) return '5';
                if (radioButton6.Checked) return '6';
                if (radioButton7.Checked) return '7';
                if (radioButton22.Checked) return '8';
                return '\x00';
            }
        }

        public Char S2
        {
            get
            {
                if (radioButton8.Checked) return 'A';
                if (radioButton9.Checked) return 'B';
                if (radioButton10.Checked) return 'C';
                if (radioButton11.Checked) return 'D';
                if (radioButton12.Checked) return 'E';
                if (radioButton13.Checked) return 'F';
                if (radioButton14.Checked) return 'G';
                if (radioButton23.Checked) return 'H';
                return '\x00';
            }
        }

        public Parity Parity
        {
            get
            {
                if (radioButton15.Checked) return Parity.Even;
                if (radioButton16.Checked) return Parity.Mark;
                if (radioButton17.Checked) return Parity.Space;
                if (radioButton18.Checked) return Parity.Odd;
                return Parity.None;
            }
        }

        public Boolean OptSwapDelBS
        {
            get { return checkBox1.Checked; }
            set { checkBox1.Checked = value; }
        }

        public Boolean OptAutoRepeat
        {
            get { return checkBox2.Checked; }
            set { checkBox2.Checked = value; }
        }

        public Boolean OptGreenFilter
        {
            get { return checkBox3.Checked; }
            set { checkBox3.Checked = value; }
        }

        public Boolean OptStretchDisplay
        {
            get { return checkBox4.Checked; }
            set { checkBox4.Checked = value; }
        }

        private void SettingsDialog_Load(object sender, EventArgs e)
        {
            mOK = false;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            mOK = true;
        }
    }
}