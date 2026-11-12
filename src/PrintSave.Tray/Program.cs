using System;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using PrintSave.Core;

namespace PrintSave.Tray
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Elevated helper invoked by the Settings window to change folder security.
            if (args.Length >= 2 && args[0].Equals("--apply-security", StringComparison.OrdinalIgnoreCase))
                return ApplySecurity(args[1]);

            bool created;
            using (var mutex = new Mutex(true, "PrintSave.Tray.SingleInstance", out created))
            {
                if (!created) return 0; // already running

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var ctx = new TrayApp())
                    Application.Run(ctx);
            }
            return 0;
        }

        private static int ApplySecurity(string onoff)
        {
            try
            {
                bool on = onoff.Equals("on", StringComparison.OrdinalIgnoreCase);
                Config cfg = ConfigStore.Load();
                cfg.SecureArchiveFolder = on;
                if (on)
                {
                    using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                        cfg.OwnerSid = id.User != null ? id.User.Value : null;
                }
                ConfigStore.Save(cfg);
                if (on) FolderSecurity.Apply(cfg.RootDir, cfg.OwnerSid);
                else FolderSecurity.Relax(cfg.RootDir);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not change folder security:\n\n" + ex.Message,
                    "PrintSave", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
