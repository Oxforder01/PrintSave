using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using PrintSave.Core;
using PrintSave.Core.Logging;
using PrintSave.Index;

namespace PrintSave.ConsoleHost
{
    internal static class Program
    {
        private static readonly ManualResetEventSlim Exit = new ManualResetEventSlim(false);

        private static int Main(string[] args)
        {
            Console.WriteLine("PrintSave - capture console host");
            Console.WriteLine("==================================");

            var opt = new CaptureOptions();
            string search = null;
            bool reindex = false, stats = false, showSettings = false;
            string secure = null;
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--root" || args[i] == "-r") && i + 1 < args.Length)
                    opt.RootDir = args[++i];
                else if ((args[i] == "--search" || args[i] == "-s") && i + 1 < args.Length)
                    search = args[++i];
                else if (args[i] == "--list") search = "";
                else if (args[i] == "--reindex") reindex = true;
                else if (args[i] == "--stats") stats = true;
                else if (args[i] == "--settings") showSettings = true;
                else if (args[i] == "--secure" && i + 1 < args.Length) secure = args[++i];
            }

            if (showSettings) return ShowSettings();
            if (secure != null) return SetSecure(secure);
            if (search != null || reindex || stats)
                return RunIndexCommand(opt.RootDir, search, reindex, stats);

            if (!IsElevated())
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: PrintSave must run as Administrator.");
                Console.WriteLine("The spooler directory and printer admin access require it.");
                Console.WriteLine("Run from an elevated terminal, or use run-console.ps1.");
                return 2;
            }

            var log = new Log(Path.Combine(opt.RootDir, "logs"));
            log.OnMessage += (level, line) =>
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = level == LogLevel.Error ? ConsoleColor.Red
                    : level == LogLevel.Warn ? ConsoleColor.Yellow
                    : ConsoleColor.Gray;
                Console.WriteLine(line);
                Console.ForegroundColor = prev;
            };

            var converter = new PrintSave.Conversion.XpsToPdfConverter();
            var engine = new CaptureEngine(opt, log, converter);
            PrintIndex index = null;
            try
            {
                index = new PrintIndex(opt.RootDir, log);
                ReconcileResult rr = index.Reconcile();
                log.Info(string.Format("Index: {0} documents (+{1} found on disk, -{2} missing).",
                    index.Count(), rr.Added, rr.Removed));
                engine.JobCaptured += job => index.Add(job);
            }
            catch (Exception ex)
            {
                log.Error("Index unavailable; capturing without it", ex);
            }
            engine.JobCaptured += job =>
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Green;
                string note = job.Converted ? " (converted to PDF)" : " (" + job.SavedFormat + ", raw)";
                Console.WriteLine(">>> SAVED: " + job.SavedPath + note);
                Console.ForegroundColor = prev;
            };

            Console.CancelKeyPress += (s, e) => { e.Cancel = true; Exit.Set(); };

            try
            {
                engine.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to start: " + ex.Message);
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("Running. Print anything to any printer and watch it get saved under:");
            Console.WriteLine("  " + opt.RootDir);
            Console.WriteLine("Press Ctrl+C to stop (restores printer settings).");
            Console.WriteLine();

            Exit.Wait();

            Console.WriteLine();
            Console.WriteLine("Stopping...");
            engine.Stop();
            if (index != null) index.Dispose();
            return 0;
        }

        private static int RunIndexCommand(string root, string search, bool reindex, bool stats)
        {
            try
            {
                using (var index = new PrintIndex(root))
                {
                    if (reindex)
                    {
                        ReconcileResult r = index.Reconcile();
                        if (r.Error != null) { Console.WriteLine("Reindex failed: " + r.Error); return 1; }
                        Console.WriteLine(string.Format("Reindexed: +{0} added, -{1} removed.", r.Added, r.Removed));
                    }
                    if (stats || reindex)
                        Console.WriteLine(string.Format("{0} documents indexed in {1}", index.Count(), index.DatabasePath));
                    if (search != null)
                    {
                        var hits = index.Search(new SearchQuery { Text = search, Limit = 50 });
                        foreach (IndexedDocument d in hits)
                        {
                            Console.WriteLine(string.Format("{0:yyyy-MM-dd HH:mm}  {1,-30}  {2,-12} {3,-20} {4}",
                                d.PrintedUtc.ToLocalTime(), Trunc(d.Document, 30), Trunc(d.User ?? "-", 12),
                                Trunc(d.Printer ?? "-", 20), d.SavedPath));
                        }
                        Console.WriteLine(hits.Count + " result(s).");
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Index error: " + ex.Message);
                return 1;
            }
        }

        private static string Trunc(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "~";
        }

        private static int ShowSettings()
        {
            Config cfg = ConfigStore.Load();
            Console.WriteLine("Settings file: " + ConfigStore.ConfigPath);
            Console.WriteLine("  Archive root         : " + cfg.RootDir);
            Console.WriteLine("  Folder security      : " + (cfg.SecureArchiveFolder ? "ON (locked to you + SYSTEM + Administrators)" : "off (easiest access - default)"));
            Console.WriteLine("  Secured for owner SID: " + (cfg.OwnerSid ?? "-"));
            Console.WriteLine();
            Console.WriteLine("Change with:  --secure on    |    --secure off   (both require Administrator)");
            return 0;
        }

        private static int SetSecure(string onoff)
        {
            bool on;
            if (onoff.Equals("on", StringComparison.OrdinalIgnoreCase)) on = true;
            else if (onoff.Equals("off", StringComparison.OrdinalIgnoreCase)) on = false;
            else { Console.WriteLine("usage: --secure on|off"); return 2; }

            if (!IsElevated())
            {
                Console.WriteLine("ERROR: changing folder security requires Administrator.");
                Console.WriteLine("Run this from an elevated terminal.");
                return 2;
            }

            Config cfg = ConfigStore.Load();
            try
            {
                if (on)
                {
                    cfg.SecureArchiveFolder = true;
                    cfg.OwnerSid = CurrentUserSid();
                    ConfigStore.Save(cfg);
                    FolderSecurity.Apply(cfg.RootDir, cfg.OwnerSid);
                    Console.WriteLine("Folder security ON: " + cfg.RootDir);
                    Console.WriteLine("Access is now limited to you, SYSTEM and Administrators.");
                }
                else
                {
                    cfg.SecureArchiveFolder = false;
                    ConfigStore.Save(cfg);
                    FolderSecurity.Relax(cfg.RootDir);
                    Console.WriteLine("Folder security OFF (easiest access): " + cfg.RootDir);
                }
                Console.WriteLine("The service keeps this setting across restarts and reboots.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to change folder security: " + ex.Message);
                return 1;
            }
        }

        private static string CurrentUserSid()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return id.User != null ? id.User.Value : null;
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
