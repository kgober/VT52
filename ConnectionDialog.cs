// ConnectionDialog.cs
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
using System.Windows.Forms;

namespace Emulator
{
    // Future Improvements / To Do
    // serial port settings (baud rate, parity, flow control, etc.)
    // telnet options
    // use registry or config file for telnet defaults
    // LRU dropdown list for telnet targets?

    public partial class ConnectionDialog : Form
    {
        private Boolean mOK;

        public ConnectionDialog()
        {
            InitializeComponent();
        }

        public Boolean OK
        {
            get { return mOK; }
        }

        public Type IOAdapter
        {
            get
            {
                if (!mOK) return null;
                if (radioButton1.Checked) return typeof(IO.Loopback);
                if (radioButton2.Checked) return typeof(IO.Serial);
                if (radioButton3.Checked) return typeof(IO.Telnet);
                return null;
            }
        }

        public String Option
        {
            get
            {
                if (!mOK) return null;
                if (radioButton1.Checked) return null;
                if (radioButton2.Checked) return comboBox1.Text;
                if (radioButton3.Checked) return textBox1.Text;
                return null;
            }
        }

        public void Set(Type ioType, String option)
        {
            if (ioType == typeof(IO.Loopback))
            {
                radioButton2.Checked = false;
                radioButton3.Checked = false;
                radioButton1.Checked = true;
            }
            else if (ioType == typeof(IO.Serial))
            {
                radioButton1.Checked = false;
                radioButton3.Checked = false;
                radioButton2.Checked = true;
                comboBox1.Text = option;
            }
            else if (ioType == typeof(IO.Telnet))
            {
                radioButton1.Checked = false;
                radioButton2.Checked = false;
                radioButton3.Checked = true;
                textBox1.Text = option;
            }
        }

        private void ConnectionDialog_Load(Object sender, EventArgs e)
        {
            mOK = false;

            String s = comboBox1.Text;
            String[] S = SerialPort.GetPortNames();
            Array.Sort<String>(S);
            comboBox1.Items.Clear();
            comboBox1.Items.AddRange(S);
            if ((s != null) && (s.Length != 0))
            {
                Boolean f = false;
                for (Int32 i = 0; i < S.Length; i++)
                {
                    if (String.Compare(s, S[i], StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        comboBox1.SelectedIndex = i;
                        f = true;
                        break;
                    }
                }
                if (!f) comboBox1.Text = s;
            }
            else
            {
                comboBox1.SelectedIndex = 0;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            mOK = true;
        }
    }
}