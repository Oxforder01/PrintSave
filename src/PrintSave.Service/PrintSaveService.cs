using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using PrintSave.Conversion;
using PrintSave.Core;
using PrintSave.Core.Logging;

namespace PrintSave.Service
{
    /// <summary>The always-on PrintSave Windows Service (runs as LocalSystem).</summary>
    public sealed class PrintSaveService : ServiceBase
    {
        public const string Name = "PrintSave";

        private CaptureEngine _engine;
        private Log _log;
        private EventLog _eventLog;

        public PrintSaveService()
        {
            ServiceName = Name;
            CanStop = true;
            CanShutdown = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                var opt = new CaptureOptions
                {
                    // A service keeps the keep-jobs flag set across restarts so nothing is missed;
                    // the uninstall step restores printer settings explicitly.
                    RestoreFlagsOnStop = false,
                    WriteJsonSidecar = false
                };

                _log = new Log(Path.Combine(opt.RootDir, "logs"));
                SetupEventLog();
                _log.OnMessage += OnLog;

                _engine = new CaptureEngine(opt, _log, new XpsToPdfConverter());
                _engine.Start();
                WriteEvent("PrintSave started. Archive root: " + opt.RootDir, EventLogEntryType.Information);
            }
            catch (Exception ex)
            {
                WriteEvent("PrintSave failed to start: " + ex, EventLogEntryType.Error);
                throw; // let the SCM mark the start as failed (recovery actions kick in)
            }
        }

        protected override void OnStop()
        {
            try { if (_engine != null) _engine.Stop(); } catch { }
            WriteEvent("PrintSave stopped.", EventLogEntryType.Information);
        }

        protected override void OnShutdown()
        {
            OnStop();
        }

        private void SetupEventLog()
        {
            try
            {
                if (!EventLog.SourceExists(Name))
                    EventLog.CreateEventSource(Name, "Application");
                _eventLog = new EventLog("Application") { Source = Name };
            }
            catch
            {
                _eventLog = null; // file log still works
            }
        }

        private void OnLog(LogLevel level, string line)
        {
            if (level == LogLevel.Error) WriteEvent(line, EventLogEntryType.Error);
            else if (level == LogLevel.Warn) WriteEvent(line, EventLogEntryType.Warning);
        }

        private void WriteEvent(string msg, EventLogEntryType type)
        {
            if (_eventLog == null) return;
            try
            {
                if (msg != null && msg.Length > 30000) msg = msg.Substring(0, 30000);
                _eventLog.WriteEntry(msg, type);
            }
            catch { }
        }
    }
}
