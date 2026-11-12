using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using PrintSave.Conversion;
using PrintSave.Core;
using PrintSave.Core.Logging;
using PrintSave.Index;

namespace PrintSave.Service
{
    /// <summary>The always-on PrintSave Windows Service (runs as LocalSystem).</summary>
    public sealed class PrintSaveService : ServiceBase
    {
        public const string Name = "PrintSave";

        private CaptureEngine _engine;
        private Log _log;
        private EventLog _eventLog;
        private PrintIndex _index;

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
                OpenIndex(opt.RootDir);
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
            try { if (_index != null) _index.Dispose(); } catch { }
            _index = null;
            WriteEvent("PrintSave stopped.", EventLogEntryType.Information);
        }

        protected override void OnShutdown()
        {
            OnStop();
        }

        // The index is an add-on: if it can't open, capture still runs and the next start's
        // reconcile picks up whatever was saved in the meantime.
        private void OpenIndex(string rootDir)
        {
            try
            {
                _index = new PrintIndex(rootDir, _log);
                _engine.JobCaptured += job => { var ix = _index; if (ix != null) ix.Add(job); };
                var t = new Thread(() =>
                {
                    try
                    {
                        var ix = _index;
                        if (ix == null) return;
                        ReconcileResult r = ix.Reconcile();
                        _log.Info(string.Format("Index ready ({0} documents; +{1} found on disk, -{2} missing).",
                            ix.Count(), r.Added, r.Removed));
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Index reconcile thread failed", ex); // e.g. service stopped mid-reconcile
                    }
                }) { IsBackground = true, Name = "PrintSaveIndexReconcile" };
                t.Start();
            }
            catch (Exception ex)
            {
                _index = null;
                _log.Error("Index unavailable; capture continues without it", ex);
            }
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
