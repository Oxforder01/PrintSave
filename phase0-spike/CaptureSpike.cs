// PrintSave - Phase 0 capture spike
//
// Purpose: prove we can capture a real print job from the Windows spooler on THIS PC,
// and report which spool format the printer produces (EMF / XPS / PDF / PostScript / PCL).
//
// How it works:
//   1. Opens the target printer (default printer, or the name passed as an argument).
//   2. Sets the "keep printed documents" flag so the spool files survive after printing.
//   3. Waits for you to print one document.
//   4. Copies the job's .SPL (data) and .SHD (metadata) out of the spool directory.
//   5. Detects the spool format and extracts best-effort metadata, then prints a report.
//   6. Restores the printer flag to how it was.
//
// Must be run elevated (the spool directory and printer admin access require it).
// Compiled with the in-box .NET Framework compiler, so this stays within older C# syntax.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace PrintSave.Phase0
{
    internal static class CaptureSpike
    {
        // Printer attribute bit that keeps spool files after printing.
        private const int PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS = 0x00000100;
        private const int PRINTER_ALL_ACCESS = 0x000F000C;

        private static int Main(string[] args)
        {
            Console.WriteLine("PrintSave - Phase 0 capture spike");
            Console.WriteLine("=================================");
            Console.WriteLine();

            if (!IsElevated())
            {
                Console.WriteLine("ERROR: this must run as Administrator.");
                Console.WriteLine("The spool directory and printer admin access are not readable otherwise.");
                Console.WriteLine("Use run.ps1, which relaunches elevated.");
                return 2;
            }

            string spoolDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "spool", "PRINTERS");
            if (!Directory.Exists(spoolDir))
            {
                Console.WriteLine("ERROR: spool directory not found: " + spoolDir);
                return 2;
            }

            string printerName = args.Length > 0 ? args[0] : GetDefaultPrinterName();
            if (string.IsNullOrEmpty(printerName))
            {
                Console.WriteLine("ERROR: could not determine a printer. Pass one as an argument:");
                Console.WriteLine("  PrintSaveSpike.exe \"Microsoft Print to PDF\"");
                return 2;
            }

            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captured");
            Directory.CreateDirectory(outDir);

            Console.WriteLine("Spool directory : " + spoolDir);
            Console.WriteLine("Target printer  : " + printerName);
            Console.WriteLine("Output folder   : " + outDir);
            Console.WriteLine();

            // Remember the existing .SHD files so we only act on a NEW one.
            HashSet<string> knownBefore = SnapshotShdFiles(spoolDir);

            bool flagWasAlreadySet;
            bool flagChanged = false;
            try
            {
                flagChanged = EnsureKeepPrintedJobs(printerName, out flagWasAlreadySet);
                if (flagWasAlreadySet)
                    Console.WriteLine("'Keep printed documents' was already enabled on this printer.");
                else if (flagChanged)
                    Console.WriteLine("Enabled 'Keep printed documents' on this printer (will restore on exit).");
            }
            catch (Exception ex)
            {
                Console.WriteLine("WARNING: could not set the keep-printed-jobs flag: " + ex.Message);
                Console.WriteLine("Will still watch the spool directory, but capture may be less reliable.");
            }

            try
            {
                Console.WriteLine();
                Console.WriteLine(">>> Now print ONE document to \"" + printerName + "\".");
                Console.WriteLine(">>> Waiting up to 120 seconds for a job... (Ctrl+C to cancel)");
                Console.WriteLine();

                string shdPath = WaitForNewShd(spoolDir, knownBefore, TimeSpan.FromSeconds(120));
                if (shdPath == null)
                {
                    Console.WriteLine("No new print job detected within the time limit.");
                    return 1;
                }

                string splPath = Path.ChangeExtension(shdPath, ".SPL");
                Console.WriteLine("Detected job: " + Path.GetFileName(shdPath));

                WaitUntilStable(splPath, TimeSpan.FromSeconds(15));

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string outShd = Path.Combine(outDir, stamp + ".SHD");
                string outSpl = Path.Combine(outDir, stamp + ".SPL");
                CopyShared(shdPath, outShd);
                long splBytes = CopyShared(splPath, outSpl);

                Console.WriteLine();
                Console.WriteLine("Captured:");
                Console.WriteLine("  " + outShd);
                Console.WriteLine("  " + outSpl + "  (" + splBytes + " bytes)");
                Console.WriteLine();

                ReportFormat(outSpl);
                Console.WriteLine();
                ReportShdMetadata(outShd);
            }
            finally
            {
                if (flagChanged)
                {
                    try
                    {
                        RestoreKeepPrintedJobs(printerName);
                        Console.WriteLine();
                        Console.WriteLine("Restored the printer's original 'keep printed documents' setting.");
                        Console.WriteLine("Note: the test job may remain in the print queue. You can clear it from");
                        Console.WriteLine("the printer's queue window if you like.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("WARNING: could not restore the printer flag: " + ex.Message);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("Phase 0 done. The spool format reported above is what drives the conversion work.");
            return 0;
        }

        // ---- format detection -------------------------------------------------

        private static void ReportFormat(string splFile)
        {
            byte[] head = ReadHead(splFile, 8192);
            string format = DetectFormat(head);

            Console.WriteLine("SPOOL FORMAT: " + format);
            Console.WriteLine("  First 16 bytes: " + HexDump(head, 16));
            Console.WriteLine("  As text       : " + AsciiPreview(head, 32));

            switch (format)
            {
                case "EMF":
                    Console.WriteLine("  => GDI/EMF. Converter plan: parse EMF-SPL records, render to PDF.");
                    break;
                case "XPS":
                    Console.WriteLine("  => XPS (OPC/ZIP). Converter plan: XPS -> PDF (straightforward).");
                    break;
                case "PDF":
                    Console.WriteLine("  => Already PDF. Just store it.");
                    break;
                case "PostScript":
                    Console.WriteLine("  => PostScript. Converter plan: Ghostscript PS -> PDF.");
                    break;
                case "PCL/PJL (RAW)":
                    Console.WriteLine("  => PCL/RAW. Conversion is partial; may need a PCL interpreter.");
                    break;
                default:
                    Console.WriteLine("  => Unrecognized. Send me the hex above and I'll identify it.");
                    break;
            }
        }

        private static string DetectFormat(byte[] b)
        {
            if (StartsWith(b, "%PDF")) return "PDF";
            if (StartsWith(b, "%!")) return "PostScript";
            if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04)
                return "XPS"; // PK.. = ZIP/OPC container used by XPS
            // Universal Exit Language / PJL: ESC %-12345X
            byte[] uel = new byte[] { 0x1B, 0x25, 0x2D, 0x31, 0x32, 0x33, 0x34, 0x35, 0x58 };
            if (IndexOf(b, uel, 64) >= 0) return "PCL/PJL (RAW)";
            // EMF signature " EMF" appears early in GDI spool data.
            byte[] emf = new byte[] { 0x20, 0x45, 0x4D, 0x46 };
            if (IndexOf(b, emf, b.Length) >= 0) return "EMF";
            return "Unknown/RAW";
        }

        // ---- SHD metadata (best effort) --------------------------------------

        private static void ReportShdMetadata(string shdFile)
        {
            Console.WriteLine("JOB METADATA (best-effort from .SHD):");
            byte[] data = File.ReadAllBytes(shdFile);
            List<string> strings = ExtractUnicodeStrings(data, 3);
            if (strings.Count == 0)
            {
                Console.WriteLine("  (no readable strings found)");
                return;
            }
            // Show the distinct printable strings; these typically include document name,
            // user, machine, printer, port and datatype.
            HashSet<string> seen = new HashSet<string>();
            foreach (string s in strings)
            {
                string t = s.Trim();
                if (t.Length >= 3 && seen.Add(t))
                    Console.WriteLine("  - " + t);
            }
            Console.WriteLine();
            Console.WriteLine("  (The real service will parse these into named fields:");
            Console.WriteLine("   document name, user, machine, printer, datatype, submitted time.)");
        }

        private static List<string> ExtractUnicodeStrings(byte[] data, int minLen)
        {
            List<string> result = new List<string>();
            StringBuilder cur = new StringBuilder();
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                char c = (char)(data[i] | (data[i + 1] << 8));
                if (c >= 0x20 && c < 0x7F)
                {
                    cur.Append(c);
                }
                else
                {
                    if (cur.Length >= minLen) result.Add(cur.ToString());
                    cur.Length = 0;
                }
            }
            if (cur.Length >= minLen) result.Add(cur.ToString());
            return result;
        }

        // ---- spool watching / file copy --------------------------------------

        private static HashSet<string> SnapshotShdFiles(string spoolDir)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string f in Directory.GetFiles(spoolDir, "*.SHD"))
                    set.Add(f);
            }
            catch { }
            return set;
        }

        private static string WaitForNewShd(string spoolDir, HashSet<string> knownBefore, TimeSpan timeout)
        {
            DateTime end = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < end)
            {
                try
                {
                    foreach (string f in Directory.GetFiles(spoolDir, "*.SHD"))
                    {
                        if (!knownBefore.Contains(f))
                            return f;
                    }
                }
                catch { }
                Thread.Sleep(400);
            }
            return null;
        }

        private static void WaitUntilStable(string file, TimeSpan timeout)
        {
            DateTime end = DateTime.UtcNow + timeout;
            long last = -1;
            int stableCount = 0;
            while (DateTime.UtcNow < end)
            {
                long len = -1;
                try { len = new FileInfo(file).Length; } catch { }
                if (len >= 0 && len == last) { stableCount++; if (stableCount >= 2) return; }
                else { stableCount = 0; last = len; }
                Thread.Sleep(300);
            }
        }

        private static long CopyShared(string src, string dst)
        {
            // The spooler may still hold the file open; share read/write to avoid lock errors.
            using (FileStream inStream = new FileStream(src, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (FileStream outStream = new FileStream(dst, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[65536];
                int n;
                long total = 0;
                while ((n = inStream.Read(buf, 0, buf.Length)) > 0)
                {
                    outStream.Write(buf, 0, n);
                    total += n;
                }
                return total;
            }
        }

        private static byte[] ReadHead(string file, int max)
        {
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                int len = (int)Math.Min(max, fs.Length);
                byte[] buf = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int n = fs.Read(buf, read, len - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read == len) return buf;
                byte[] trimmed = new byte[read];
                Array.Copy(buf, trimmed, read);
                return trimmed;
            }
        }

        // ---- byte helpers -----------------------------------------------------

        private static bool StartsWith(byte[] b, string ascii)
        {
            if (b.Length < ascii.Length) return false;
            for (int i = 0; i < ascii.Length; i++)
                if (b[i] != (byte)ascii[i]) return false;
            return true;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int limit)
        {
            int max = Math.Min(haystack.Length, limit) - needle.Length;
            for (int i = 0; i <= max; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        private static string HexDump(byte[] b, int count)
        {
            StringBuilder sb = new StringBuilder();
            int n = Math.Min(count, b.Length);
            for (int i = 0; i < n; i++)
            {
                sb.Append(b[i].ToString("X2"));
                sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        private static string AsciiPreview(byte[] b, int count)
        {
            StringBuilder sb = new StringBuilder();
            int n = Math.Min(count, b.Length);
            for (int i = 0; i < n; i++)
                sb.Append(b[i] >= 0x20 && b[i] < 0x7F ? (char)b[i] : '.');
            return sb.ToString();
        }

        // ---- elevation --------------------------------------------------------

        private static bool IsElevated()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ---- spooler P/Invoke -------------------------------------------------

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetDefaultPrinterW")]
        private static extern bool GetDefaultPrinter(StringBuilder pszBuffer, ref int pcchBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct PRINTER_DEFAULTS
        {
            public IntPtr pDatatype;
            public IntPtr pDevMode;
            public int DesiredAccess;
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenPrinterW")]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetPrinterW")]
        private static extern bool GetPrinter(IntPtr hPrinter, int Level, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetPrinterW")]
        private static extern bool SetPrinter(IntPtr hPrinter, int Level, IntPtr pPrinter, int Command);

        private static string GetDefaultPrinterName()
        {
            int size = 0;
            GetDefaultPrinter(null, ref size); // probe size
            if (size <= 0) return null;
            StringBuilder sb = new StringBuilder(size);
            if (GetDefaultPrinter(sb, ref size)) return sb.ToString();
            return null;
        }

        // Offsets within PRINTER_INFO_2 (pointer-sized fields before each DWORD).
        // On x64 a pointer is 8 bytes; we compute from IntPtr.Size so x86 works too.
        private static int AttributesOffset() { return 13 * IntPtr.Size; }
        private static int SecurityDescriptorOffset() { return 12 * IntPtr.Size; }

        private static IntPtr OpenForAdmin(string printerName)
        {
            PRINTER_DEFAULTS def = new PRINTER_DEFAULTS();
            def.pDatatype = IntPtr.Zero;
            def.pDevMode = IntPtr.Zero;
            def.DesiredAccess = PRINTER_ALL_ACCESS;
            IntPtr h;
            if (!OpenPrinter(printerName, out h, ref def))
                throw new Exception("OpenPrinter failed (error " + Marshal.GetLastWin32Error() + ")");
            return h;
        }

        // Returns true if we changed the flag (so caller should restore it later).
        private static bool EnsureKeepPrintedJobs(string printerName, out bool alreadySet)
        {
            alreadySet = false;
            IntPtr h = OpenForAdmin(printerName);
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int needed;
                GetPrinter(h, 2, IntPtr.Zero, 0, out needed); // probe size
                if (needed <= 0) throw new Exception("GetPrinter size probe failed (error " + Marshal.GetLastWin32Error() + ")");
                buffer = Marshal.AllocHGlobal(needed);
                if (!GetPrinter(h, 2, buffer, needed, out needed))
                    throw new Exception("GetPrinter failed (error " + Marshal.GetLastWin32Error() + ")");

                int attrs = Marshal.ReadInt32(buffer, AttributesOffset());
                if ((attrs & PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS) != 0)
                {
                    alreadySet = true;
                    return false; // nothing to change, nothing to restore
                }

                // Avoid trying to re-apply a security descriptor we don't own.
                Marshal.WriteIntPtr(buffer, SecurityDescriptorOffset(), IntPtr.Zero);
                Marshal.WriteInt32(buffer, AttributesOffset(), attrs | PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS);

                if (!SetPrinter(h, 2, buffer, 0))
                    throw new Exception("SetPrinter failed (error " + Marshal.GetLastWin32Error() + ")");
                return true;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                ClosePrinter(h);
            }
        }

        private static void RestoreKeepPrintedJobs(string printerName)
        {
            IntPtr h = OpenForAdmin(printerName);
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int needed;
                GetPrinter(h, 2, IntPtr.Zero, 0, out needed);
                if (needed <= 0) throw new Exception("GetPrinter size probe failed (error " + Marshal.GetLastWin32Error() + ")");
                buffer = Marshal.AllocHGlobal(needed);
                if (!GetPrinter(h, 2, buffer, needed, out needed))
                    throw new Exception("GetPrinter failed (error " + Marshal.GetLastWin32Error() + ")");

                int attrs = Marshal.ReadInt32(buffer, AttributesOffset());
                Marshal.WriteIntPtr(buffer, SecurityDescriptorOffset(), IntPtr.Zero);
                Marshal.WriteInt32(buffer, AttributesOffset(), attrs & ~PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS);

                if (!SetPrinter(h, 2, buffer, 0))
                    throw new Exception("SetPrinter failed (error " + Marshal.GetLastWin32Error() + ")");
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                ClosePrinter(h);
            }
        }
    }
}
