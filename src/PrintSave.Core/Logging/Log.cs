using System;
using System.IO;
using System.Text;

namespace PrintSave.Core.Logging
{
    public enum LogLevel { Info, Warn, Error }

    /// <summary>
    /// Minimal thread-safe logger: writes to the console and to a rolling-by-day file.
    /// (Phase 4 will swap this for structured logging + the Windows Event Log.)
    /// </summary>
    public sealed class Log
    {
        private readonly object _gate = new object();
        private readonly string _logDir;

        public event Action<LogLevel, string> OnMessage;

        public Log(string logDir)
        {
            _logDir = logDir;
            try { Directory.CreateDirectory(_logDir); } catch { }
        }

        public void Info(string msg) { Write(LogLevel.Info, msg); }
        public void Warn(string msg) { Write(LogLevel.Warn, msg); }
        public void Error(string msg) { Write(LogLevel.Error, msg); }

        public void Error(string msg, Exception ex)
        {
            Write(LogLevel.Error, msg + " :: " + ex.GetType().Name + ": " + ex.Message);
        }

        private void Write(LogLevel level, string msg)
        {
            string line = string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1,-5}] {2}",
                DateTime.Now, level.ToString().ToUpperInvariant(), msg);

            lock (_gate)
            {
                try
                {
                    string file = Path.Combine(_logDir, "printsave-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { /* never let logging crash the engine */ }
            }

            var handler = OnMessage;
            if (handler != null)
            {
                try { handler(level, line); } catch { }
            }
        }
    }
}
