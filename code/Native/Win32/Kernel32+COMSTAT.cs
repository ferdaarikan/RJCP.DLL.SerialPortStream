﻿// Copyright © Jason Curl 2012-2021
// Sources at https://github.com/jcurl/SerialPortStream
// Licensed under the Microsoft Public License (Ms-PL)

namespace RJCP.IO.Ports.Native.Win32
{
    using System;
    using System.Runtime.InteropServices;

    internal static partial class Kernel32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct COMSTAT
        {
            [MarshalAs(UnmanagedType.U4)]
            public ComStatFlags Flags;
            public UInt32 cbInQue;
            public UInt32 cbOutQue;
        }
    }
}
