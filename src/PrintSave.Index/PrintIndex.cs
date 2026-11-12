using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using PrintSave.Core;
using PrintSave.Core.Logging;

namespace PrintSave.Index
{
    /// <summary>
    /// The searchable metadata index: one SQLite database at {root}\.state\index.db with a row per
    /// archived document and an FTS5 table over document name / user / printer / machine.
    /// Thread-safe; WAL mode lets other processes (tray UI, console search) read while the service writes.
    /// </summary>
    public sealed class PrintIndex : IDisposable
    {
        private const int SchemaVersion = 1;
        private const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        private readonly object _gate = new object();
        private readonly SqliteConnection _conn;
        private readonly string _rootDir;
        private readonly Log _log;

        public string DatabasePath { get; }

        public PrintIndex(string rootDir, Log log = null, bool readOnly = false)
        {
            _rootDir = rootDir;
            _log = log;
            string dir = Path.Combine(rootDir, ".state");
            if (!readOnly) Directory.CreateDirectory(dir);
            DatabasePath = Path.Combine(dir, "index.db");

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            };
            _conn = new SqliteConnection(csb.ToString());
            _conn.Open();
            Exec("PRAGMA busy_timeout = 5000;");
            if (!readOnly)
            {
                Exec("PRAGMA journal_mode = WAL;");
                Exec("PRAGMA synchronous = NORMAL;");
                Migrate();
            }
        }

        // ---------------------------------------------------------------- schema

        private void Migrate()
        {
            long version = Convert.ToInt64(Scalar("PRAGMA user_version;"), CultureInfo.InvariantCulture);
            if (version >= SchemaVersion) return;

            using (var tx = _conn.BeginTransaction())
            {
                Exec(@"
CREATE TABLE IF NOT EXISTS documents (
    id            INTEGER PRIMARY KEY,
    saved_path    TEXT NOT NULL UNIQUE COLLATE NOCASE,
    document      TEXT,
    user_name     TEXT,
    machine       TEXT,
    printer       TEXT,
    source_format TEXT,
    saved_format  TEXT,
    converted     INTEGER NOT NULL DEFAULT 0,
    pages         INTEGER,
    printed_utc   TEXT NOT NULL,   -- submit time if known, else capture time
    captured_utc  TEXT NOT NULL,
    size_bytes    INTEGER,
    origin        TEXT NOT NULL    -- 'capture' (live) or 'backfill' (found on disk)
);
CREATE INDEX IF NOT EXISTS ix_documents_printed ON documents(printed_utc);
CREATE INDEX IF NOT EXISTS ix_documents_printer ON documents(printer);
CREATE INDEX IF NOT EXISTS ix_documents_user    ON documents(user_name);

CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(
    document, user_name, printer, machine,
    content='documents', content_rowid='id', tokenize='unicode61 remove_diacritics 2');

CREATE TRIGGER IF NOT EXISTS documents_ai AFTER INSERT ON documents BEGIN
  INSERT INTO documents_fts(rowid, document, user_name, printer, machine)
  VALUES (new.id, new.document, new.user_name, new.printer, new.machine);
END;
CREATE TRIGGER IF NOT EXISTS documents_ad AFTER DELETE ON documents BEGIN
  INSERT INTO documents_fts(documents_fts, rowid, document, user_name, printer, machine)
  VALUES ('delete', old.id, old.document, old.user_name, old.printer, old.machine);
END;
CREATE TRIGGER IF NOT EXISTS documents_au AFTER UPDATE ON documents BEGIN
  INSERT INTO documents_fts(documents_fts, rowid, document, user_name, printer, machine)
  VALUES ('delete', old.id, old.document, old.user_name, old.printer, old.machine);
  INSERT INTO documents_fts(rowid, document, user_name, printer, machine)
  VALUES (new.id, new.document, new.user_name, new.printer, new.machine);
END;", tx);
                Exec("PRAGMA user_version = " + SchemaVersion + ";", tx);
                tx.Commit();
            }
        }

        // ---------------------------------------------------------------- writes

        /// <summary>Records a freshly captured job. Upserts on saved path. Never throws.</summary>
        public bool Add(PrintJobInfo job)
        {
            if (job == null || string.IsNullOrEmpty(job.SavedPath)) return false;
            try
            {
                long? size = null;
                try { size = new FileInfo(job.SavedPath).Length; } catch { }

                var rec = new IndexedDocument
                {
                    SavedPath = job.SavedPath,
                    Document = job.Document,
                    User = job.User,
                    Machine = job.Machine,
                    Printer = job.Printer,
                    SourceFormat = job.Format,
                    SavedFormat = job.SavedFormat,
                    Converted = job.Converted,
                    Pages = job.TotalPages > 0 ? (int?)job.TotalPages : null,
                    PrintedUtc = job.SubmittedUtc ?? job.CapturedUtc,
                    CapturedUtc = job.CapturedUtc,
                    SizeBytes = size,
                    Origin = "capture"
                };
                lock (_gate) Upsert(rec, null);
                return true;
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Error("Index add failed for " + job.SavedPath, ex);
                return false;
            }
        }

        private void Upsert(IndexedDocument d, SqliteTransaction tx)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO documents (saved_path, document, user_name, machine, printer, source_format, saved_format,
                       converted, pages, printed_utc, captured_utc, size_bytes, origin)
VALUES ($path, $doc, $user, $machine, $printer, $src, $saved, $conv, $pages, $printed, $captured, $size, $origin)
ON CONFLICT(saved_path) DO UPDATE SET
    document = excluded.document, user_name = excluded.user_name, machine = excluded.machine,
    printer = excluded.printer, source_format = excluded.source_format, saved_format = excluded.saved_format,
    converted = excluded.converted, pages = excluded.pages, printed_utc = excluded.printed_utc,
    captured_utc = excluded.captured_utc, size_bytes = excluded.size_bytes, origin = excluded.origin;";
                cmd.Parameters.AddWithValue("$path", d.SavedPath);
                cmd.Parameters.AddWithValue("$doc", (object)d.Document ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$user", (object)d.User ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$machine", (object)d.Machine ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$printer", (object)d.Printer ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$src", (object)d.SourceFormat ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$saved", (object)d.SavedFormat ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$conv", d.Converted ? 1 : 0);
                cmd.Parameters.AddWithValue("$pages", (object)d.Pages ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$printed", ToIso(d.PrintedUtc));
                cmd.Parameters.AddWithValue("$captured", ToIso(d.CapturedUtc));
                cmd.Parameters.AddWithValue("$size", (object)d.SizeBytes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$origin", d.Origin);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Brings the index in line with the archive on disk: adds files that were saved but never
        /// indexed (e.g. captured before the index existed) and removes rows whose file was deleted.
        /// Backfilled rows get what the path tells us: date/time from the folders + HHmmss prefix,
        /// document name from the file name. Never throws.
        /// </summary>
        public ReconcileResult Reconcile()
        {
            var result = new ReconcileResult();
            // Archive root unreachable (unplugged drive, permissions)? Do nothing rather than drop every row.
            if (!Directory.Exists(_rootDir)) { result.Error = "archive root not found"; return result; }
            try
            {
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                lock (_gate)
                {
                    using (var cmd = _conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT saved_path FROM documents;";
                        using (var r = cmd.ExecuteReader())
                            while (r.Read()) known.Add(r.GetString(0));
                    }
                }

                var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var toAdd = new List<IndexedDocument>();
                foreach (string file in EnumerateArchiveFiles())
                {
                    onDisk.Add(file);
                    if (known.Contains(file)) continue;
                    IndexedDocument d = FromPath(file);
                    if (d != null) toAdd.Add(d);
                }

                var toRemove = new List<string>();
                foreach (string path in known)
                    if (!onDisk.Contains(path) && !File.Exists(path)) toRemove.Add(path);

                if (toAdd.Count == 0 && toRemove.Count == 0) return result;

                lock (_gate)
                {
                    using (var tx = _conn.BeginTransaction())
                    {
                        foreach (IndexedDocument d in toAdd) Upsert(d, tx);
                        foreach (string path in toRemove)
                        {
                            using (var cmd = _conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText = "DELETE FROM documents WHERE saved_path = $p;";
                                cmd.Parameters.AddWithValue("$p", path);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
                result.Added = toAdd.Count;
                result.Removed = toRemove.Count;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                if (_log != null) _log.Error("Index reconcile failed", ex);
            }
            return result;
        }

        private static readonly string[] ArchiveExtensions = { ".pdf", ".xps", ".oxps", ".emf", ".ps", ".spl", ".prn", ".pcl" };

        private IEnumerable<string> EnumerateArchiveFiles()
        {
            if (!Directory.Exists(_rootDir)) yield break;
            // Only {root}\yyyy\... trees: skips logs, .state and anything else living in the root.
            foreach (string yearDir in Directory.GetDirectories(_rootDir))
            {
                string year = Path.GetFileName(yearDir);
                if (year.Length != 4 || !int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    continue;
                string[] files;
                try { files = Directory.GetFiles(yearDir, "*", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (string f in files)
                {
                    string ext = Path.GetExtension(f);
                    foreach (string ok in ArchiveExtensions)
                        if (string.Equals(ext, ok, StringComparison.OrdinalIgnoreCase)) { yield return f; break; }
                }
            }
        }

        /// <summary>Recovers what metadata it can from {root}\yyyy\MMMM\dd\HHmmss_{doc}[_n].ext.</summary>
        internal static IndexedDocument FromPath(string file)
        {
            var fi = new FileInfo(file);
            string name = Path.GetFileNameWithoutExtension(file);
            string doc = name;
            DateTime local = fi.LastWriteTime;

            try
            {
                string dayDir = fi.Directory.Name;
                string monthDir = fi.Directory.Parent.Name;
                string yearDir = fi.Directory.Parent.Parent.Name;
                if (DateTime.TryParseExact(yearDir + " " + monthDir + " " + dayDir, "yyyy MMMM dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
                {
                    if (name.Length > 7 && name[6] == '_' &&
                        DateTime.TryParseExact(name.Substring(0, 6), "HHmmss", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime t))
                    {
                        // Legacy format: HHmmss_{doc}[_n]
                        local = day.Add(t.TimeOfDay);
                        doc = name.Substring(7);
                        int us = name.LastIndexOf('_');
                        if (us > 7 && int.TryParse(name.Substring(us + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _)
                            && File.Exists(Path.Combine(fi.DirectoryName, name.Substring(0, us) + fi.Extension)))
                            doc = name.Substring(7, us - 7);
                    }
                    else
                    {
                        // Current format: {doc}[ nn] — keep the file's time of day, drop a trailing " nn" clash suffix.
                        local = day.Add(fi.LastWriteTime.TimeOfDay);
                        doc = StripClashSuffix(name, fi);
                    }
                }
            }
            catch { }

            string ext = fi.Extension.TrimStart('.').ToUpperInvariant();
            DateTime printedUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
            return new IndexedDocument
            {
                SavedPath = file,
                Document = doc,
                SourceFormat = null,
                SavedFormat = ext,
                Converted = false,
                PrintedUtc = printedUtc,
                CapturedUtc = fi.CreationTimeUtc,
                SizeBytes = fi.Length,
                Origin = "backfill"
            };
        }

        // "Report 01" -> "Report" when "Report.ext" sits beside it (Organizer's same-day clash suffix).
        private static string StripClashSuffix(string name, FileInfo fi)
        {
            int sp = name.LastIndexOf(' ');
            if (sp > 0 && sp + 1 < name.Length &&
                int.TryParse(name.Substring(sp + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
                File.Exists(Path.Combine(fi.DirectoryName, name.Substring(0, sp) + fi.Extension)))
                return name.Substring(0, sp);
            return name;
        }

        // ---------------------------------------------------------------- reads

        public long Count()
        {
            lock (_gate) return Convert.ToInt64(Scalar("SELECT COUNT(*) FROM documents;"), CultureInfo.InvariantCulture);
        }

        /// <summary>Newest first. Free text matches document/user/printer/machine by word prefix.</summary>
        public List<IndexedDocument> Search(SearchQuery q)
        {
            q = q ?? new SearchQuery();
            var sql = new StringBuilder("SELECT d.* FROM documents d");
            var where = new List<string>();
            using (var cmd = _conn.CreateCommand())
            {
                string match = BuildMatch(q.Text);
                if (match != null)
                {
                    sql.Append(" JOIN documents_fts f ON f.rowid = d.id");
                    where.Add("documents_fts MATCH $match");
                    cmd.Parameters.AddWithValue("$match", match);
                }
                if (q.FromUtc.HasValue)
                {
                    where.Add("d.printed_utc >= $from");
                    cmd.Parameters.AddWithValue("$from", ToIso(q.FromUtc.Value));
                }
                if (q.ToUtc.HasValue)
                {
                    where.Add("d.printed_utc < $to");
                    cmd.Parameters.AddWithValue("$to", ToIso(q.ToUtc.Value));
                }
                if (!string.IsNullOrEmpty(q.Printer))
                {
                    where.Add("d.printer = $printer COLLATE NOCASE");
                    cmd.Parameters.AddWithValue("$printer", q.Printer);
                }
                if (!string.IsNullOrEmpty(q.User))
                {
                    where.Add("d.user_name = $user COLLATE NOCASE");
                    cmd.Parameters.AddWithValue("$user", q.User);
                }
                if (where.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", where));
                sql.Append(" ORDER BY d.printed_utc DESC, d.id DESC LIMIT $limit OFFSET $offset;");
                cmd.Parameters.AddWithValue("$limit", q.Limit > 0 ? q.Limit : 100);
                cmd.Parameters.AddWithValue("$offset", Math.Max(0, q.Offset));
                cmd.CommandText = sql.ToString();

                var list = new List<IndexedDocument>();
                lock (_gate)
                {
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) list.Add(Read(r));
                }
                return list;
            }
        }

        /// <summary>Turns user text into a safe FTS5 query: every word must match as a prefix.</summary>
        internal static string BuildMatch(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var terms = new List<string>();
            var cur = new StringBuilder();
            foreach (char c in text + " ")
            {
                if (char.IsLetterOrDigit(c)) { cur.Append(c); continue; }
                if (cur.Length > 0) { terms.Add("\"" + cur + "\"*"); cur.Clear(); }
            }
            return terms.Count == 0 ? null : string.Join(" AND ", terms);
        }

        private static IndexedDocument Read(SqliteDataReader r)
        {
            return new IndexedDocument
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                SavedPath = Str(r, "saved_path"),
                Document = Str(r, "document"),
                User = Str(r, "user_name"),
                Machine = Str(r, "machine"),
                Printer = Str(r, "printer"),
                SourceFormat = Str(r, "source_format"),
                SavedFormat = Str(r, "saved_format"),
                Converted = r.GetInt64(r.GetOrdinal("converted")) != 0,
                Pages = r.IsDBNull(r.GetOrdinal("pages")) ? (int?)null : r.GetInt32(r.GetOrdinal("pages")),
                PrintedUtc = FromIso(Str(r, "printed_utc")),
                CapturedUtc = FromIso(Str(r, "captured_utc")),
                SizeBytes = r.IsDBNull(r.GetOrdinal("size_bytes")) ? (long?)null : r.GetInt64(r.GetOrdinal("size_bytes")),
                Origin = Str(r, "origin")
            };
        }

        // ---------------------------------------------------------------- helpers

        private static string Str(SqliteDataReader r, string col)
        {
            int i = r.GetOrdinal(col);
            return r.IsDBNull(i) ? null : r.GetString(i);
        }

        private static string ToIso(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Local) dt = dt.ToUniversalTime();
            return dt.ToString(IsoFormat, CultureInfo.InvariantCulture);
        }

        private static DateTime FromIso(string s)
        {
            return DateTime.ParseExact(s, IsoFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        private void Exec(string sql, SqliteTransaction tx = null)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private object Scalar(string sql)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = sql;
                return cmd.ExecuteScalar();
            }
        }

        public void Dispose()
        {
            lock (_gate) _conn.Dispose();
        }
    }

    public sealed class ReconcileResult
    {
        public int Added { get; set; }
        public int Removed { get; set; }
        public string Error { get; set; }
    }

    public sealed class SearchQuery
    {
        public string Text { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public string Printer { get; set; }
        public string User { get; set; }
        public int Limit { get; set; } = 100;
        public int Offset { get; set; }
    }

    public sealed class IndexedDocument
    {
        public long Id { get; set; }
        public string SavedPath { get; set; }
        public string Document { get; set; }
        public string User { get; set; }
        public string Machine { get; set; }
        public string Printer { get; set; }
        public string SourceFormat { get; set; }
        public string SavedFormat { get; set; }
        public bool Converted { get; set; }
        public int? Pages { get; set; }
        public DateTime PrintedUtc { get; set; }
        public DateTime CapturedUtc { get; set; }
        public long? SizeBytes { get; set; }
        public string Origin { get; set; }
    }
}
