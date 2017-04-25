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
using System.Collections.Generic;
using System.IO.Ports;
using System.Reflection;
using System.Windows.Forms;

namespace Emulator
{
    // Future Improvements / To Do
    // telnet options
    // use registry or config file for telnet defaults
    // LRU dropdown list for telnet targets?

    public partial class ConnectionDialog : Form
    {
        private Boolean mOK;
        private Dictionary<String, SerialPort> mSerialPorts = new Dictionary<String, SerialPort>(StringComparer.OrdinalIgnoreCase);

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

        public String Options
        {
            get
            {
                if (!mOK) return null;
                if (radioButton1.Checked) return null;
                if (radioButton2.Checked) return String.Concat(comboBox1.Text, "|", comboBox2.Text, "|", comboBox3.Text, "|", comboBox4.Text, "|", comboBox5.Text, "|", checkBox1.Checked ? "RTSCTS" : "");
                if (radioButton3.Checked) return String.Concat(textBox1.Text, "|");
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
            if ((S == null) || (S.Length == 0)) return;
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

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            groupBox1.Visible = radioButton1.Checked;
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            groupBox2.Visible = radioButton2.Checked;
            if (radioButton2.Checked) UpdateSerialPortUI();
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            groupBox3.Visible = radioButton3.Checked;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (radioButton2.Checked) UpdateSerialPortUI();
        }

        private void UpdateSerialPortUI()
        {
            SerialPort P = GetSerialPort(comboBox1.Text);
            Boolean wasOpen = P.IsOpen;
            PCF dwProvCapabilities = 0;
            SP dwSettableParams = 0;
            BAUD dwSettableBaud = 0;
            DATABITS wSettableData = 0;
            SSP wSettableStopParity = 0;
            try
            {
                if (!wasOpen) P.Open();
                Object o = P.BaseStream.GetType().GetField("commProp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(P.BaseStream);
                dwProvCapabilities = (PCF)(o.GetType().GetField("dwProvCapabilities", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(o));
                dwSettableParams = (SP)(o.GetType().GetField("dwSettableParams", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(o));
                dwSettableBaud = (BAUD)(o.GetType().GetField("dwSettableBaud", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(o));
                wSettableData = (DATABITS)(o.GetType().GetField("wSettableData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(o));
                wSettableStopParity = (SSP)(o.GetType().GetField("wSettableStopParity", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(o));
            }
            catch (Exception ex)
            {
#if DEBUG
                String buf = "Exception\r\n";
                while (ex != null)
                {
                    buf = String.Concat(buf, "\r\n", ex.Message, " [", ex.Source, "]\r\n", ex.StackTrace);
                    ex = ex.InnerException;
                }
                MessageBox.Show(buf, "UpdateSerialPortUI()");
#endif
            }
            if ((!wasOpen) && (P.IsOpen)) P.Close();
            if (((dwSettableParams & SP.BAUD) == 0) || (dwSettableBaud == 0))
            {
                label1.Enabled = false;
                comboBox2.Enabled = false;
            }
            else
            {
                label1.Enabled = true;
                comboBox2.Enabled = true;
                String s = comboBox2.Text;
                if ((s == null) || (s.Length == 0)) s = "9600";
                comboBox2.Items.Clear();
                if ((dwSettableBaud & BAUD._075) != 0) comboBox2.Items.Add("75");
                if ((dwSettableBaud & BAUD._110) != 0) comboBox2.Items.Add("110");
                if ((dwSettableBaud & BAUD._134_5) != 0) comboBox2.Items.Add("134.5");
                if ((dwSettableBaud & BAUD._150) != 0) comboBox2.Items.Add("150");
                if ((dwSettableBaud & BAUD._300) != 0) comboBox2.Items.Add("300");
                if ((dwSettableBaud & BAUD._600) != 0) comboBox2.Items.Add("600");
                if ((dwSettableBaud & BAUD._1200) != 0) comboBox2.Items.Add("1200");
                if ((dwSettableBaud & BAUD._1800) != 0) comboBox2.Items.Add("1800");
                if ((dwSettableBaud & BAUD._2400) != 0) comboBox2.Items.Add("2400");
                if ((dwSettableBaud & BAUD._4800) != 0) comboBox2.Items.Add("4800");
                if ((dwSettableBaud & BAUD._7200) != 0) comboBox2.Items.Add("7200");
                if ((dwSettableBaud & BAUD._9600) != 0) comboBox2.Items.Add("9600");
                if ((dwSettableBaud & BAUD._14400) != 0) comboBox2.Items.Add("14400");
                if ((dwSettableBaud & BAUD._19200) != 0) comboBox2.Items.Add("19200");
                if ((dwSettableBaud & BAUD._38400) != 0) comboBox2.Items.Add("38400");
                if ((dwSettableBaud & BAUD._56K) != 0) comboBox2.Items.Add("56000");
                if ((dwSettableBaud & BAUD._57600) != 0) comboBox2.Items.Add("57600");
                if ((dwSettableBaud & BAUD._115200) != 0) comboBox2.Items.Add("115200");
                if ((dwSettableBaud & BAUD._128K) != 0) comboBox2.Items.Add("128000");
                comboBox2.DropDownStyle = ((dwSettableBaud & BAUD.USER) != 0) ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
                if ((s != null) && (s.Length != 0))
                {
                    Boolean f = false;
                    for (Int32 i = 0; i < comboBox2.Items.Count; i++)
                    {
                        if (String.Compare(s, comboBox2.Items[i] as String, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            comboBox2.SelectedIndex = i;
                            f = true;
                            break;
                        }
                    }
                    if (!f)
                    {
                        if ((dwSettableBaud & BAUD.USER) != 0) comboBox2.Text = s;
                        else comboBox2.SelectedIndex = 0;
                    }
                }
                else
                {
                    comboBox2.SelectedIndex = 0;
                }
            }
            if (((dwSettableParams & SP.DATABITS) == 0) || (wSettableData == 0))
            {
                label2.Enabled = false;
                comboBox3.Enabled = false;
            }
            else
            {
                label2.Enabled = true;
                comboBox3.Enabled = true;
                String s = comboBox3.Text;
                if ((s == null) || (s.Length == 0)) s = "8";
                comboBox3.Items.Clear();
                if ((wSettableData & DATABITS._5) != 0) comboBox3.Items.Add("5");
                if ((wSettableData & DATABITS._6) != 0) comboBox3.Items.Add("6");
                if ((wSettableData & DATABITS._7) != 0) comboBox3.Items.Add("7");
                if ((wSettableData & DATABITS._8) != 0) comboBox3.Items.Add("8");
                if ((wSettableData & DATABITS._16) != 0) comboBox3.Items.Add("16");
                if ((s != null) && (s.Length != 0))
                {
                    Boolean f = false;
                    for (Int32 i = 0; i < comboBox3.Items.Count; i++)
                    {
                        if (String.Compare(s, comboBox3.Items[i] as String, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            comboBox3.SelectedIndex = i;
                            f = true;
                            break;
                        }
                    }
                    if (!f) comboBox3.SelectedIndex = 0;
                }
                else
                {
                    comboBox3.SelectedIndex = 0;
                }
            }
            if (((dwSettableParams & SP.PARITY) == 0) || ((wSettableStopParity & SSP.PARITY_MASK) == 0))
            {
                label3.Enabled = false;
                comboBox4.Enabled = false;
            }
            else
            {
                label3.Enabled = true;
                comboBox4.Enabled = true;
                String s = comboBox4.Text;
                if ((s == null) || (s.Length == 0)) s = "None";
                comboBox4.Items.Clear();
                if ((wSettableStopParity & SSP.PARITY_NONE) != 0) comboBox4.Items.Add("None");
                if ((wSettableStopParity & SSP.PARITY_EVEN) != 0) comboBox4.Items.Add("Even");
                if ((wSettableStopParity & SSP.PARITY_ODD) != 0) comboBox4.Items.Add("Odd");
                if ((wSettableStopParity & SSP.PARITY_MARK) != 0) comboBox4.Items.Add("Mark");
                if ((wSettableStopParity & SSP.PARITY_SPACE) != 0) comboBox4.Items.Add("Space");
                if ((s != null) && (s.Length != 0))
                {
                    Boolean f = false;
                    for (Int32 i = 0; i < comboBox4.Items.Count; i++)
                    {
                        if (String.Compare(s, comboBox4.Items[i] as String, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            comboBox4.SelectedIndex = i;
                            f = true;
                            break;
                        }
                    }
                    if (!f) comboBox4.SelectedIndex = 0;
                }
                else
                {
                    comboBox4.SelectedIndex = 0;
                }
            }
            if (((dwSettableParams & SP.STOPBITS) == 0) || ((wSettableStopParity & SSP.STOPBITS_MASK) == 0))
            {
                label4.Enabled = false;
                comboBox5.Enabled = false;
            }
            else
            {
                label4.Enabled = true;
                comboBox5.Enabled = true;
                String s = comboBox5.Text;
                if ((s == null) || (s.Length == 0)) s = "1";
                comboBox5.Items.Clear();
                if ((wSettableStopParity & SSP.STOPBITS_10) != 0) comboBox5.Items.Add("1");
                if ((wSettableStopParity & SSP.STOPBITS_15) != 0) comboBox5.Items.Add("1.5");
                if ((wSettableStopParity & SSP.STOPBITS_20) != 0) comboBox5.Items.Add("2");
                if ((s != null) && (s.Length != 0))
                {
                    Boolean f = false;
                    for (Int32 i = 0; i < comboBox5.Items.Count; i++)
                    {
                        if (String.Compare(s, comboBox5.Items[i] as String, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            comboBox5.SelectedIndex = i;
                            f = true;
                            break;
                        }
                    }
                    if (!f) comboBox5.SelectedIndex = 0;
                }
                else
                {
                    comboBox5.SelectedIndex = 0;
                }
            }
            if (((dwSettableParams & SP.HANDSHAKING) == 0) || ((dwProvCapabilities & PCF.RTSCTS) == 0))
            {
                checkBox1.Enabled = false;
            }
            else
            {
                checkBox1.Enabled = true;
            }
        }

        private SerialPort GetSerialPort(String name)
        {
            if (mSerialPorts.ContainsKey(name)) return mSerialPorts[name];
            SerialPort port = new SerialPort(name);
            mSerialPorts.Add(name, port);
            return port;
        }
    }
}