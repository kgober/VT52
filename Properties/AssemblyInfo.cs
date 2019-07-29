// AssemblyInfo.cs
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

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("VT52")]
[assembly: AssemblyDescription("VT52 Terminal Emulator")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Kenneth Gober")]
[assembly: AssemblyProduct("VT52")]
[assembly: AssemblyCopyright("Copyright © Kenneth Gober 2016, 2017, 2019")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("c5d7feb3-cec5-47f4-baf5-e3bbed3add36")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version 
//      Build Number
//      Revision
//
[assembly: AssemblyVersion("1.0.0.7")]
[assembly: AssemblyFileVersion("1.0.0.7")]
// 1.0.0.0 - initial release
// 1.0.0.1 - fix ConnectionDialog exception when no COM ports present
//         - fix window size in Windows 10
// 1.0.0.2 - allow serial port settings to be specified in ConnectionDialog
//         - add Settings, Connection, Brightness +/-, and About to system menu
//         - handle telnet session disconnection
// 1.0.0.3 - fix another ConnectionDialog exception when no COM ports present
//         - fix ConnectionDialog tab order
//         - add Raw TCP connection option
// 1.0.0.4 - add command line options for Raw TCP and Emulator Options
//         - add version number to About dialog
//         - improve keyboard handling
// 1.0.0.5 - back-port improvements from VT05:
//         - refactor terminal state processing
//         - improve screen color to more closely match P4 phosphor color
//         - improve handling of connections
//         - add GitHub URL to About dialog
// 1.0.0.6 - back-port improvements from VT05:
//         - allow paste from clipboard (mouse right click)
//         - move VT52-specific code to VT52.cs
//         - improve handling of serial port exceptions
//         - fix hardcoded telnet terminal type/speed/size
// 1.0.0.7 - allow aspect ratio to be locked/unlocked (default locked)
