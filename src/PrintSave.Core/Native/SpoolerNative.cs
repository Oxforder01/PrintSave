using System;
using System.Runtime.InteropServices;

namespace PrintSave.Core.Native
{
    /// <summary>Raw P/Invoke surface for the Windows print spooler (winspool.drv).</summary>
    internal static class SpoolerNative
    {
        // Printer attributes
        public const int PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS = 0x00000100;

        // Access rights
        public const int PRINTER_ALL_ACCESS = 0x000F000C;

        // EnumPrinters flags
        public const int PRINTER_ENUM_LOCAL = 0x00000002;
        public const int PRINTER_ENUM_CONNECTIONS = 0x00000004;

        // Job status flags
        public const uint JOB_STATUS_PAUSED = 0x00000001;
        public const uint JOB_STATUS_ERROR = 0x00000002;
        public const uint JOB_STATUS_DELETING = 0x00000004;
        public const uint JOB_STATUS_SPOOLING = 0x00000008;
        public const uint JOB_STATUS_PRINTING = 0x00000010;
        public const uint JOB_STATUS_OFFLINE = 0x00000020;
        public const uint JOB_STATUS_PAPEROUT = 0x00000040;
        public const uint JOB_STATUS_PRINTED = 0x00000080;
        public const uint JOB_STATUS_DELETED = 0x00000100;
        public const uint JOB_STATUS_BLOCKED_DEVQ = 0x00000200;
        public const uint JOB_STATUS_USER_INTERVENTION = 0x00000400;
        public const uint JOB_STATUS_RESTART = 0x00000800;
        public const uint JOB_STATUS_COMPLETE = 0x00001000;
        public const uint JOB_STATUS_RETAINED = 0x00002000;

        // SetJob commands
        public const int JOB_CONTROL_DELETE = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct PRINTER_DEFAULTS
        {
            public IntPtr pDatatype;
            public IntPtr pDevMode;
            public int DesiredAccess;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PRINTER_INFO_4
        {
            public string pPrinterName;
            public string pServerName;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct JOB_INFO_1
        {
            public uint JobId;
            public string pPrinterName;
            public string pMachineName;
            public string pUserName;
            public string pDocument;
            public string pDatatype;
            public string pStatus;
            public uint Status;
            public uint Priority;
            public uint Position;
            public uint TotalPages;
            public uint PagesPrinted;
            public SYSTEMTIME Submitted;
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenPrinterW")]
        public static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

        [DllImport("winspool.drv", SetLastError = true, EntryPoint = "ClosePrinter")]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetPrinterW")]
        public static extern bool GetPrinter(IntPtr hPrinter, int Level, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetPrinterW")]
        public static extern bool SetPrinter(IntPtr hPrinter, int Level, IntPtr pPrinter, int Command);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "EnumPrintersW")]
        public static extern bool EnumPrinters(int Flags, string Name, int Level, IntPtr pPrinterEnum,
            int cbBuf, out int pcbNeeded, out int pcReturned);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "EnumJobsW")]
        public static extern bool EnumJobs(IntPtr hPrinter, int FirstJob, int NoJobs, int Level, IntPtr pJob,
            int cbBuf, out int pcbNeeded, out int pcReturned);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetJobW")]
        public static extern bool SetJob(IntPtr hPrinter, uint JobId, int Level, IntPtr pJob, int Command);
    }
}
