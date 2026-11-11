using System;
using System.IO;

namespace PrintSave.Core
{
    /// <summary>Detects the spool data format from the file's leading bytes.</summary>
    public static class FormatDetector
    {
        public struct Result
        {
            public string Name;       // e.g. "XPS"
            public string Extension;  // e.g. ".xps"
        }

        public static Result Detect(string filePath)
        {
            byte[] head = ReadHead(filePath, 8192);
            return Detect(head);
        }

        public static Result Detect(byte[] b)
        {
            if (StartsWith(b, "%PDF")) return Make("PDF", ".pdf");
            if (StartsWith(b, "%!")) return Make("PostScript", ".ps");
            if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04)
                return Make("XPS", ".xps"); // PK.. = ZIP/OPC container

            byte[] uel = { 0x1B, 0x25, 0x2D, 0x31, 0x32, 0x33, 0x34, 0x35, 0x58 }; // ESC %-12345X
            if (IndexOf(b, uel, 64) >= 0) return Make("PCL", ".pcl");

            byte[] emf = { 0x20, 0x45, 0x4D, 0x46 }; // " EMF"
            if (IndexOf(b, emf, b.Length) >= 0) return Make("EMF", ".emf");

            return Make("Unknown", ".spl");
        }

        private static Result Make(string name, string ext)
        {
            return new Result { Name = name, Extension = ext };
        }

        private static byte[] ReadHead(string file, int max)
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read,
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
    }
}
