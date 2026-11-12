using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Windows.Forms;
using PrintSave.Core;
using PrintSave.Index;

namespace PrintSave.Tray
{
    /// <summary>Tray presence: status menu + windows. Exiting the tray does NOT stop the service.</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly Timer _diskTimer;
        private bool _diskWarnShown;
        private SettingsForm _settings;
        private SearchForm _search;

        public TrayApp()
        {
            _tray = new NotifyIcon
            {
                Icon = TrayIcon.Build(),
                Visible = true,
                Text = "PrintSave"
            };
            var menu = new ContextMenuStrip();
            menu.Opening += (s, e) => BuildMenu(menu);
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => OpenSearch();
            BuildMenu(menu);

            // Independently watch free space so the tray can warn even if the service isn't running.
            _diskTimer = new Timer { Interval = 5 * 60 * 1000 };
            _diskTimer.Tick += (s, e) => CheckDiskBalloon();
            _diskTimer.Start();
            CheckDiskBalloon();
        }

        private static bool IsLowDisk(out double freeGB, out double thresholdGB)
        {
            freeGB = 0; thresholdGB = 0;
            try
            {
                Config cfg = ConfigStore.Load();
                thresholdGB = cfg.WarnFreeSpaceGB;
                if (thresholdGB <= 0) return false;
                double f;
                if (!DiskSpace.TryGetFreeGB(cfg.RootDir, out f)) return false;
                freeGB = f;
                return f < thresholdGB;
            }
            catch { return false; }
        }

        private void CheckDiskBalloon()
        {
            double free, threshold;
            if (IsLowDisk(out free, out threshold))
            {
                if (!_diskWarnShown)
                {
                    _diskWarnShown = true;
                    _tray.ShowBalloonTip(8000, "PrintSave — low disk space",
                        string.Format(CultureInfo.InvariantCulture,
                            "Only {0:0.0} GB free on the archive drive. Documents are still being saved; free up space soon.",
                            free),
                        ToolTipIcon.Warning);
                }
            }
            else
            {
                _diskWarnShown = false; // re-arm once space recovers
            }
        }

        private static string RootDir()
        {
            try { return ConfigStore.Load().RootDir; } catch { return @"C:\PrintSave"; }
        }

        private void BuildMenu(ContextMenuStrip menu)
        {
            string status = ServiceStatus();
            _tray.Text = "PrintSave — " + status;

            menu.Items.Clear();
            menu.Items.Add(new ToolStripMenuItem("PrintSave — " + status) { Enabled = false });

            string last = LastSaved();
            if (last != null) menu.Items.Add(new ToolStripMenuItem(last) { Enabled = false });

            double free, threshold;
            if (IsLowDisk(out free, out threshold))
            {
                var warn = new ToolStripMenuItem(
                    string.Format(CultureInfo.InvariantCulture, "⚠ Low disk space: {0:0.0} GB free", free))
                { Enabled = false, ForeColor = System.Drawing.Color.Firebrick };
                menu.Items.Add(warn);
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Search documents\u2026", null, (s, e) => OpenSearch());
            menu.Items.Add("Open archive folder", null, (s, e) => OpenFolder(RootDir()));
            menu.Items.Add("Settings\u2026", null, (s, e) => OpenSettings());
            menu.Items.Add("View logs", null, (s, e) => OpenFolder(Path.Combine(RootDir(), "logs")));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());
        }

        private static string ServiceStatus()
        {
            try
            {
                using (var sc = new ServiceController("PrintSave"))
                    return sc.Status == ServiceControllerStatus.Running
                        ? "running"
                        : sc.Status.ToString().ToLowerInvariant();
            }
            catch { return "not installed"; }
        }

        private static string LastSaved()
        {
            try
            {
                string db = Path.Combine(RootDir(), ".state", "index.db");
                if (!File.Exists(db)) return null;
                using (var ix = new PrintIndex(RootDir(), null, true))
                {
                    var hits = ix.Search(new SearchQuery { Text = "", Limit = 1 });
                    if (hits.Count > 0)
                        return "Last: " + Trunc(hits[0].Document, 32) + "  ("
                               + hits[0].PrintedUtc.ToLocalTime().ToString("MMM d, HH:mm") + ")";
                }
            }
            catch { }
            return null;
        }

        private void OpenSearch()
        {
            if (_search == null || _search.IsDisposed) _search = new SearchForm(RootDir());
            ShowForm(_search);
        }

        private void OpenSettings()
        {
            if (_settings == null || _settings.IsDisposed) _settings = new SettingsForm();
            ShowForm(_settings);
        }

        private static void ShowForm(Form f)
        {
            if (!f.Visible) f.Show();
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Activate();
            f.BringToFront();
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "PrintSave"); }
        }

        private static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "\u2026";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_diskTimer != null) _diskTimer.Dispose();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
