This is a VT52 terminal emulator for Windows, written in C#.

The aim of this project is to accurately reproduce the experience of using a real
Digital Equipment Corporation (DEC) VT52 terminal, including the look of the screen.

![Screenshot](User%20Man%20Screen%20Text.png)

This program requires the .NET Framework, v2.0 or newer.

Controls:

Regular keyboard keys work as expected.  
Numeric keypad works like the VT52 keypad.  
NumLock, Num/, and Num* are PF1, PF2, and PF3.  
F1, F2, and F3 can also be used as PF1, PF2, and PF3.  
F5 opens the settings dialog.  
F6 opens the connection dialog.  
F11/F12 adjust screen brightness.

Note: the emulator treats the '[' and ']' keys as most systems do: unshifted they are square brackets,
and shifted they are curly braces ('{' and '}').  On a real VT52, the unshifted keys are '[' and '{',
while the shifted keys are ']' and '}', respectively.

Settings:

The transmit and receive speed can be adjusted in the Settings dialog.  A real VT52 had maximum
transmit/receive speeds of 9600 bps (set using 2 dials), but the emulator will allow 19200 bps
or "line speed" which means "as fast as the underlying physical connection can go".  The terminal
speed can be set independently of the speed of the underlying physical connection so that you
can, for example, reproduce ASCII animations that look best at a slower speed (even though you
are actually using a much faster connection such as Ethernet).

Connections:

The emulator can be connected to a target system using your computer's serial port.  The serial
port settings (baud rate, data bits, parity, and stop bits) are configurable.

The emulator can also be connected to a target system over the network, using telnet or raw TCP.  By
default telnet connections use port 23, but connections to other ports can be specified by adding a
colon and the port number to the host name or IP address, e.g. "host:2023" or "192.168.1.100:2023".
Raw TCP connections don't have a default port, so you must always specify it.

Note: BSD telnet historically suppressed negotiation of Telnet options when connecting to a port
other than the Telnet well-known-port (23).  This implementation of telnet always negotiates options
regardless of the specified port.  Use the raw TCP network connection option to connect without
any Telnet processing (options, IAC doubling, CR mapping, etc.).

Command-Line Options:

-t host[:port] - connect to host via telnet (port defaults to 23 if unspecified)  
-r host:port - connect to host via raw TCP connection  
-o s{+|-} - enable/disable Swap BS/DEL option  
-o r{+|-} - enable/disable Auto Repeat option  
-o g{+|-} - enable/disable Green CRT Filter option

-o Options may be combined, e.g. -os-r+ to disable Swap BS/DEL and enable Auto Repeat
