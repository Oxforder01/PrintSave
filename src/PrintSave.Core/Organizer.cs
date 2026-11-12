using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PrintSave.Core
{
    /// <summary>Places a captured spool file into the year/month/day archive with a metadata sidecar.</summary>
    public sealed class Organizer
    {
        private readonly string _rootDir;

        public Organizer(string rootDir)
        {
            _rootDir = rootDir;
        }

        /// <summary>
        /// Copies the job's spool file into {root}\yyyy\MM\dd\HHmmss_{doc}{ext} and writes a .json sidecar.
        /// Sets job.SavedPath. Returns the saved file path.
        /// </summary>
        public string Save(PrintJobInfo job)
        {
            DateTime stamp = (job.SubmittedUtc ?? job.CapturedUtc).ToLocalTime();

            // Month folder in words (e.g. "November"), per user preference.
            string dir = Path.Combine(_rootDir,
                stamp.ToString("yyyy", CultureInfo.InvariantCulture),
                stamp.ToString("MMMM", CultureInfo.InvariantCulture),
                stamp.ToString("dd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);

            var fmt = FormatDetector.Detect(job.SpoolFilePath);
            job.Format = fmt.Name;

            string baseName = stamp.ToString("HHmmss", CultureInfo.InvariantCulture)
                              + "_" + Sanitize(StripKnownExtension(job.Document));
            string dest = Resolve(dir, baseName, fmt.Extension);

            CopyShared(job.SpoolFilePath, dest);
            job.SavedPath = dest;

            WriteSidecar(job, Path.ChangeExtension(dest, ".json"));
            return dest;
        }

        private static string Resolve(string dir, string baseName, string ext)
        {
            string candidate = Path.Combine(dir, baseName + ext);
            if (!File.Exists(candidate)) return candidate;
            for (int i = 2; i < 10000; i++)
            {
                candidate = Path.Combine(dir, baseName + "_" + i + ext);
                if (!File.Exists(candidate)) return candidate;
            }
            // Extremely unlikely fallback.
            return Path.Combine(dir, baseName + "_" + Guid.NewGuid().ToString("N") + ext);
        }

        private static readonly string[] KnownDocExtensions =
        {
            ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".pdf", ".txt", ".rtf",
            ".odt", ".ods", ".odp", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff",
            ".html", ".htm", ".csv", ".xps", ".pub", ".vsdx", ".md"
        };

        /// <summary>Removes a trailing source extension (e.g. "Report.docx" -> "Report") to avoid doubled extensions.</summary>
        public static string StripKnownExtension(string doc)
        {
            if (string.IsNullOrWhiteSpace(doc)) return doc;
            string trimmed = doc.TrimEnd();
            foreach (string ext in KnownDocExtensions)
            {
                if (trimmed.Length > ext.Length &&
                    trimmed.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(0, trimmed.Length - ext.Length);
            }
            return doc;
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Document";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' ||
                    c == '"' || c == '<' || c == '>' || c == '|' || c < 0x20)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            string clean = sb.ToString().Trim().TrimEnd('.', ' ');
            if (clean.Length == 0) return "Document";
            if (clean.Length > 80) clean = clean.Substring(0, 80).TrimEnd('.', ' ');
            return clean;
        }

        private static void WriteSidecar(PrintJobInfo job, string jsonPath)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            Field(sb, "jobId", job.JobId.ToString(CultureInfo.InvariantCulture), false);
            Field(sb, "document", job.Document, true);
            Field(sb, "user", job.User, true);
            Field(sb, "machine", job.Machine, true);
            Field(sb, "printer", job.Printer, true);
            Field(sb, "datatype", job.Datatype, true);
            Field(sb, "format", job.Format, true);
            Field(sb, "totalPages", job.TotalPages.ToString(CultureInfo.InvariantCulture), false);
            Field(sb, "submittedUtc", job.SubmittedUtc.HasValue
                ? job.SubmittedUtc.Value.ToString("o", CultureInfo.InvariantCulture) : null, true);
            Field(sb, "capturedUtc", job.CapturedUtc.ToString("o", CultureInfo.InvariantCulture), true);
            Field(sb, "savedPath", job.SavedPath, true);
            Field(sb, "statusFlags", job.Status.ToString(CultureInfo.InvariantCulture), false, last: true);
            sb.Append("}\n");

            File.WriteAllText(jsonPath, sb.ToString(), new UTF8Encoding(false));
        }

        private static void Field(StringBuilder sb, string key, string value, bool quote, bool last = false)
        {
            sb.Append("  \"").Append(key).Append("\": ");
            if (value == null) sb.Append("null");
            else if (quote) sb.Append('"').Append(JsonEscape(value)).Append('"');
            else sb.Append(value);
            sb.Append(last ? "\n" : ",\n");
        }

        private static string JsonEscape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void CopyShared(string src, string dst)
        {
            using (var inStream = new FileStream(src, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var outStream = new FileStream(dst, FileMode.Create, FileAccess.Write))
            {
                inStream.CopyTo(outStream, 65536);
            }
        }
    }
}
