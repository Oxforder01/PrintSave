using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using PrintSave.Core.Logging;
using PrintSave.Core.Native;

namespace PrintSave.Core
{
    /// <summary>
    /// The Phase 1 capture backbone. Sets "keep printed documents" on every printer, polls the
    /// spooler for completed jobs, archives each into the date folders, then clears it from the queue.
    /// Designed to be host-agnostic: the console host and (later) the Windows Service both drive it.
    /// </summary>
    public sealed class CaptureEngine
    {
        private readonly CaptureOptions _opt;
        private readonly Log _log;
        private readonly PrinterManager _printers = new PrinterManager();
        private readonly Organizer _organizer;
        private readonly IDocumentConverter _converter;
        private readonly string _spoolDir;

        private readonly HashSet<string> _processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ProcessedStore _processedStore;
        private readonly HashSet<string> _flagEnabledByUs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _knownPrinters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Thread _thread;
        private volatile bool _running;

        private DateTime _lastDiskCheck = DateTime.MinValue;
        private bool _lowDiskWarned;

        public event Action<PrintJobInfo> JobCaptured;

        /// <summary>Raised when free space first drops below the warn threshold (freeGB, thresholdGB).</summary>
        public event Action<double, double> LowDiskSpace;

        public CaptureEngine(CaptureOptions opt, Log log, IDocumentConverter converter = null)
        {
            _opt = opt;
            _log = log;
            _converter = converter;
            _organizer = new Organizer(opt.RootDir);
            _processedStore = new ProcessedStore(opt.RootDir);
            _spoolDir = ResolveSpoolDir();
        }

        public string SpoolDirectory => _spoolDir;

        public void Start()
        {
            Directory.CreateDirectory(_opt.RootDir);
            _log.Info("PrintSave capture starting. Archive root: " + _opt.RootDir);
            _log.Info("Spool directory: " + _spoolDir);
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "PrintSaveCapture" };
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(5));
            if (_opt.RestoreFlagsOnStop) RestoreFlags();
            _log.Info("PrintSave capture stopped.");
        }

        private void Loop()
        {
            DateTime lastFlagPass = DateTime.MinValue;
            while (_running)
            {
                try
                {
                    if ((DateTime.UtcNow - lastFlagPass).TotalSeconds >= _opt.ReapplyFlagsEverySec)
                    {
                        ApplyFlagsToAllPrinters();
                        lastFlagPass = DateTime.UtcNow;
                    }
                    PollOnce();

                    if ((DateTime.UtcNow - _lastDiskCheck).TotalSeconds >= _opt.CheckDiskEverySec)
                    {
                        CheckDiskSpace();
                        _lastDiskCheck = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("capture loop error", ex);
                }
                SleepInterruptible(_opt.PollIntervalMs);
            }
        }

        // Warn once when the archive drive drops below the threshold; re-arm after it recovers
        // (with hysteresis so a value hovering at the line doesn't warn repeatedly). Never deletes.
        private void CheckDiskSpace()
        {
            double threshold = _opt.WarnFreeSpaceGB;
            if (threshold <= 0) return;

            double freeGB;
            if (!DiskSpace.TryGetFreeGB(_opt.RootDir, out freeGB)) return;

            if (freeGB < threshold)
            {
                if (!_lowDiskWarned)
                {
                    _lowDiskWarned = true;
                    _log.Warn(string.Format(CultureInfo.InvariantCulture,
                        "Low disk space: {0:0.0} GB free on the archive drive (warn threshold {1:0.#} GB). " +
                        "Capturing continues; nothing is deleted.", freeGB, threshold));
                    Action<double, double> h = LowDiskSpace;
                    if (h != null) h(freeGB, threshold);
                }
            }
            else if (_lowDiskWarned && freeGB >= threshold * 1.1)
            {
                _lowDiskWarned = false;
                _log.Info(string.Format(CultureInfo.InvariantCulture,
                    "Disk space recovered: {0:0.0} GB free on the archive drive.", freeGB));
            }
        }

        private void ApplyFlagsToAllPrinters()
        {
            IReadOnlyList<string> printers;
            try { printers = _printers.EnumeratePrinters(); }
            catch (Exception ex) { _log.Error("EnumeratePrinters failed", ex); return; }

            foreach (string name in printers)
            {
                if (_knownPrinters.Add(name))
                    _log.Info("Printer found: " + name);

                try
                {
                    if (_printers.SetKeepPrintedJobs(name, true))
                    {
                        _flagEnabledByUs.Add(name);
                        _log.Info("Enabled keep-printed-jobs on: " + name);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Could not set keep-jobs on '" + name + "'", ex);
                }
            }
        }

        private void PollOnce()
        {
            // Snapshot of printers we know about (plus any discovered this pass).
            List<string> printers;
            try { printers = new List<string>(_printers.EnumeratePrinters()); }
            catch { printers = new List<string>(_knownPrinters); }

            foreach (string printer in printers)
            {
                IntPtr h;
                try { h = PrinterManager.Open(printer); }
                catch (Exception ex) { _log.Error("Open '" + printer + "' failed", ex); continue; }

                try
                {
                    foreach (var job in ReadJobs(h))
                    {
                        if (!IsCompleted(job.Status)) continue;
                        string key = printer + "|" + job.JobId;
                        if (_processed.Contains(key)) continue;
                        HandleCompletedJob(h, printer, job, key);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Polling '" + printer + "' failed", ex);
                }
                finally
                {
                    SpoolerNative.ClosePrinter(h);
                }
            }
        }

        private void HandleCompletedJob(IntPtr hPrinter, string printer, SpoolerNative.JOB_INFO_1 job, string key)
        {
            PrintJobInfo info = BuildInfo(job, printer);
            string fingerprint = Fingerprint(info);

            // Already captured in an earlier run? Don't save a duplicate; just clear it from the queue.
            if (_processedStore.Contains(fingerprint))
            {
                _log.Info(string.Format("Already captured job {0} (\"{1}\"); clearing from queue.",
                    job.JobId, info.Document));
                if (_opt.DeleteAfterCapture) DeleteJob(hPrinter, job.JobId, printer);
                _processed.Add(key);
                return;
            }

            string spoolPath = SpoolPathFor(job.JobId);
            info.SpoolFilePath = spoolPath;
            if (!File.Exists(spoolPath))
            {
                // Printed, but no spool file to copy (RAW passthrough, or already cleaned). Skip once.
                _log.Warn(string.Format("Job {0} on '{1}' (\"{2}\") has no spool file at {3}; skipping.",
                    job.JobId, printer, info.Document, spoolPath));
                _processed.Add(key);
                return;
            }

            try
            {
                WaitUntilStable(spoolPath, TimeSpan.FromSeconds(15));

                SaveJob(info);
                _processedStore.Add(fingerprint);
                _processed.Add(key);
                _log.Info("Captured " + info + " -> " + info.SavedPath);

                var handler = JobCaptured;
                if (handler != null) { try { handler(info); } catch { } }

                if (_opt.DeleteAfterCapture)
                    DeleteJob(hPrinter, job.JobId, printer);
            }
            catch (Exception ex)
            {
                _log.Error(string.Format("Failed to capture job {0} on '{1}'", job.JobId, printer), ex);
                _processed.Add(key); // don't spin forever on a bad job
            }
        }

        private static PrintJobInfo BuildInfo(SpoolerNative.JOB_INFO_1 job, string printer)
        {
            return new PrintJobInfo
            {
                JobId = job.JobId,
                Printer = string.IsNullOrEmpty(job.pPrinterName) ? printer : job.pPrinterName,
                Machine = job.pMachineName,
                User = job.pUserName,
                Document = job.pDocument,
                Datatype = job.pDatatype,
                Status = job.Status,
                TotalPages = job.TotalPages,
                SubmittedUtc = ToUtc(job.Submitted),
                CapturedUtc = DateTime.UtcNow
            };
        }

        // Stable across restarts/reboots: based on submit time + document + printer, not the volatile job id.
        private static string Fingerprint(PrintJobInfo info)
        {
            string submitted = info.SubmittedUtc.HasValue
                ? info.SubmittedUtc.Value.Ticks.ToString(CultureInfo.InvariantCulture)
                : "job" + info.JobId;
            return (info.Printer ?? "") + "|" + submitted + "|" + (info.Document ?? "") + "|" + info.TotalPages;
        }

        private void SaveJob(PrintJobInfo info)
        {
            FormatDetector.Result fmt = FormatDetector.Detect(info.SpoolFilePath);
            info.Format = fmt.Name;

            string savedPath = null;

            if (_converter != null && _converter.CanConvert(fmt.Name))
            {
                string pdfPath = _organizer.BuildTargetPath(info, ".pdf");
                if (_converter.TryConvertToPdf(info.SpoolFilePath, pdfPath, fmt.Name, out string convError))
                {
                    savedPath = pdfPath;
                    info.Converted = true;
                    info.SavedFormat = "PDF";
                }
                else
                {
                    _log.Warn(string.Format("Conversion of {0} job {1} failed ({2}); saving raw {0}.",
                        fmt.Name, info.JobId, convError));
                }
            }

            if (savedPath == null)
            {
                // No converter, unsupported format, or conversion failed: keep the raw spool so nothing is lost.
                savedPath = _organizer.BuildTargetPath(info, fmt.Extension);
                Organizer.CopyRaw(info.SpoolFilePath, savedPath);
                info.SavedFormat = fmt.Name;
            }

            info.SavedPath = savedPath;
            if (_opt.WriteJsonSidecar)
                _organizer.WriteSidecar(info);
        }

        private void DeleteJob(IntPtr hPrinter, uint jobId, string printer)
        {
            try
            {
                if (SpoolerNative.SetJob(hPrinter, jobId, 0, IntPtr.Zero, SpoolerNative.JOB_CONTROL_DELETE))
                    _log.Info(string.Format("Cleared job {0} from queue on '{1}'.", jobId, printer));
                else
                    _log.Warn(string.Format("SetJob DELETE failed for job {0} on '{1}' (win32 {2})",
                        jobId, printer, Marshal.GetLastWin32Error()));
            }
            catch (Exception ex)
            {
                _log.Error("DeleteJob error", ex);
            }
        }

        private List<SpoolerNative.JOB_INFO_1> ReadJobs(IntPtr hPrinter)
        {
            var list = new List<SpoolerNative.JOB_INFO_1>();
            SpoolerNative.EnumJobs(hPrinter, 0, 0xFFFF, 1, IntPtr.Zero, 0, out int needed, out _);
            if (needed <= 0) return list;

            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!SpoolerNative.EnumJobs(hPrinter, 0, 0xFFFF, 1, buffer, needed, out needed, out int returned))
                    throw new SpoolerException("EnumJobs failed", Marshal.GetLastWin32Error());

                int size = Marshal.SizeOf(typeof(SpoolerNative.JOB_INFO_1));
                for (int i = 0; i < returned; i++)
                {
                    IntPtr p = IntPtr.Add(buffer, i * size);
                    list.Add((SpoolerNative.JOB_INFO_1)Marshal.PtrToStructure(p, typeof(SpoolerNative.JOB_INFO_1)));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return list;
        }

        private static bool IsCompleted(uint status)
        {
            bool printed = (status & SpoolerNative.JOB_STATUS_PRINTED) != 0
                           || (status & SpoolerNative.JOB_STATUS_RETAINED) != 0;
            bool busy = (status & SpoolerNative.JOB_STATUS_SPOOLING) != 0
                        || (status & SpoolerNative.JOB_STATUS_DELETING) != 0;
            return printed && !busy;
        }

        private string SpoolPathFor(uint jobId)
        {
            return Path.Combine(_spoolDir, jobId.ToString("D5", CultureInfo.InvariantCulture) + ".SPL");
        }

        private void RestoreFlags()
        {
            foreach (string name in _flagEnabledByUs)
            {
                try
                {
                    _printers.SetKeepPrintedJobs(name, false);
                    _log.Info("Restored keep-printed-jobs (off) on: " + name);
                }
                catch (Exception ex)
                {
                    _log.Error("Could not restore flag on '" + name + "'", ex);
                }
            }
        }

        private static DateTime? ToUtc(SpoolerNative.SYSTEMTIME st)
        {
            if (st.wYear == 0) return null;
            try
            {
                return new DateTime(st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond,
                    st.wMilliseconds, DateTimeKind.Utc);
            }
            catch { return null; }
        }

        private static void WaitUntilStable(string file, TimeSpan timeout)
        {
            DateTime end = DateTime.UtcNow + timeout;
            long last = -1;
            int stable = 0;
            while (DateTime.UtcNow < end)
            {
                long len = -1;
                try { len = new FileInfo(file).Length; } catch { }
                if (len >= 0 && len == last) { if (++stable >= 2) return; }
                else { stable = 0; last = len; }
                Thread.Sleep(250);
            }
        }

        private void SleepInterruptible(int ms)
        {
            int slept = 0;
            while (_running && slept < ms)
            {
                Thread.Sleep(Math.Min(100, ms - slept));
                slept += 100;
            }
        }

        private static string ResolveSpoolDir()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default)
                    .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers"))
                {
                    string dir = k?.GetValue("DefaultSpoolDirectory") as string;
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "PRINTERS");
        }
    }
}
