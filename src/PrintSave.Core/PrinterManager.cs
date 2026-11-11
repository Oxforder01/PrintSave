using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PrintSave.Core.Native;

namespace PrintSave.Core
{
    /// <summary>Enumerates printers and manages the "keep printed documents" attribute.</summary>
    public sealed class PrinterManager
    {
        // Field offsets within PRINTER_INFO_2 (13 pointer-sized fields precede Attributes).
        private static int AttributesOffset => 13 * IntPtr.Size;
        private static int SecurityDescriptorOffset => 12 * IntPtr.Size;

        /// <summary>Returns the names of all local printers and printer connections.</summary>
        public IReadOnlyList<string> EnumeratePrinters()
        {
            var names = new List<string>();
            int flags = SpoolerNative.PRINTER_ENUM_LOCAL | SpoolerNative.PRINTER_ENUM_CONNECTIONS;

            SpoolerNative.EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out int needed, out _);
            if (needed <= 0) return names;

            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!SpoolerNative.EnumPrinters(flags, null, 4, buffer, needed, out needed, out int returned))
                    throw new SpoolerException("EnumPrinters failed", Marshal.GetLastWin32Error());

                int size = Marshal.SizeOf(typeof(SpoolerNative.PRINTER_INFO_4));
                for (int i = 0; i < returned; i++)
                {
                    IntPtr p = IntPtr.Add(buffer, i * size);
                    var info = (SpoolerNative.PRINTER_INFO_4)Marshal.PtrToStructure(p, typeof(SpoolerNative.PRINTER_INFO_4));
                    if (!string.IsNullOrEmpty(info.pPrinterName))
                        names.Add(info.pPrinterName);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return names;
        }

        /// <summary>True if the printer currently keeps printed jobs.</summary>
        public bool IsKeepPrintedJobsSet(string printerName)
        {
            IntPtr h = Open(printerName);
            try
            {
                int attrs = ReadAttributes(h);
                return (attrs & SpoolerNative.PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS) != 0;
            }
            finally { SpoolerNative.ClosePrinter(h); }
        }

        /// <summary>Sets or clears the keep-printed-jobs attribute. Returns true if it changed.</summary>
        public bool SetKeepPrintedJobs(string printerName, bool enabled)
        {
            IntPtr h = Open(printerName);
            IntPtr buffer = IntPtr.Zero;
            try
            {
                SpoolerNative.GetPrinter(h, 2, IntPtr.Zero, 0, out int needed);
                if (needed <= 0)
                    throw new SpoolerException("GetPrinter size probe failed", Marshal.GetLastWin32Error());

                buffer = Marshal.AllocHGlobal(needed);
                if (!SpoolerNative.GetPrinter(h, 2, buffer, needed, out needed))
                    throw new SpoolerException("GetPrinter failed", Marshal.GetLastWin32Error());

                int attrs = Marshal.ReadInt32(buffer, AttributesOffset);
                bool currently = (attrs & SpoolerNative.PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS) != 0;
                if (currently == enabled) return false;

                int newAttrs = enabled
                    ? attrs | SpoolerNative.PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS
                    : attrs & ~SpoolerNative.PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS;

                // Don't attempt to re-apply a security descriptor we didn't author.
                Marshal.WriteIntPtr(buffer, SecurityDescriptorOffset, IntPtr.Zero);
                Marshal.WriteInt32(buffer, AttributesOffset, newAttrs);

                if (!SpoolerNative.SetPrinter(h, 2, buffer, 0))
                    throw new SpoolerException("SetPrinter failed", Marshal.GetLastWin32Error());
                return true;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                SpoolerNative.ClosePrinter(h);
            }
        }

        private int ReadAttributes(IntPtr hPrinter)
        {
            SpoolerNative.GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out int needed);
            if (needed <= 0)
                throw new SpoolerException("GetPrinter size probe failed", Marshal.GetLastWin32Error());
            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!SpoolerNative.GetPrinter(hPrinter, 2, buffer, needed, out needed))
                    throw new SpoolerException("GetPrinter failed", Marshal.GetLastWin32Error());
                return Marshal.ReadInt32(buffer, AttributesOffset);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        internal static IntPtr Open(string printerName)
        {
            var def = new SpoolerNative.PRINTER_DEFAULTS
            {
                pDatatype = IntPtr.Zero,
                pDevMode = IntPtr.Zero,
                DesiredAccess = SpoolerNative.PRINTER_ALL_ACCESS
            };
            if (!SpoolerNative.OpenPrinter(printerName, out IntPtr h, ref def))
                throw new SpoolerException("OpenPrinter failed for '" + printerName + "'", Marshal.GetLastWin32Error());
            return h;
        }
    }

    public sealed class SpoolerException : Exception
    {
        public int Win32Error { get; }
        public SpoolerException(string message, int win32Error)
            : base(message + " (win32 error " + win32Error + ")")
        {
            Win32Error = win32Error;
        }
    }
}
