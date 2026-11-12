using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PrintSave.Core
{
    /// <summary>
    /// Remembers which jobs have already been captured, persisted to disk so a restart or reboot
    /// never re-saves the same job. Stored as one fingerprint per line under {root}\.state.
    /// </summary>
    public sealed class ProcessedStore
    {
        private readonly string _file;
        private readonly HashSet<string> _set = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        public ProcessedStore(string rootDir)
        {
            string dir = Path.Combine(rootDir, ".state");
            try
            {
                Directory.CreateDirectory(dir);
                var di = new DirectoryInfo(dir);
                if ((di.Attributes & FileAttributes.Hidden) == 0)
                    di.Attributes |= FileAttributes.Hidden;
            }
            catch { }
            _file = Path.Combine(dir, "captured.log");
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_file))
                    foreach (string line in File.ReadAllLines(_file))
                        if (line.Length > 0) _set.Add(line);
            }
            catch { }
        }

        public bool Contains(string fingerprint)
        {
            lock (_gate) return _set.Contains(fingerprint);
        }

        /// <summary>Adds a fingerprint. Returns false if it was already present.</summary>
        public bool Add(string fingerprint)
        {
            lock (_gate)
            {
                if (!_set.Add(fingerprint)) return false;
                try { File.AppendAllText(_file, fingerprint + "\n", new UTF8Encoding(false)); }
                catch { }
                return true;
            }
        }
    }
}
