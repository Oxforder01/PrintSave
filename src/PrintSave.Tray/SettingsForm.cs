using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PrintSave.Core;

namespace PrintSave.Tray
{
    internal sealed class SettingsForm : Form
    {
        private readonly TextBox _root;
        private readonly CheckBox _secure;
        private readonly CheckBox _startup;
        private bool _suppress;

        public SettingsForm()
        {
            Text = "PrintSave — Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 250);
            Icon = TrayIcon.Build();

            Config cfg = ConfigStore.Load();

            var lblRoot = new Label { Text = "Archive folder:", Left = 16, Top = 16, AutoSize = true };
            _root = new TextBox { Left = 16, Top = 38, Width = 330, Text = cfg.RootDir, ReadOnly = true };
            var open = new Button { Text = "Open", Left = 356, Top = 36, Width = 88 };
            open.Click += (s, e) => OpenRoot();

            _secure = new CheckBox
            {
                Text = "Restrict archive folder to me, SYSTEM and Administrators",
                Left = 16, Top = 84, Width = 430, AutoSize = true,
                Checked = cfg.SecureArchiveFolder
            };
            var secureNote = new Label
            {
                Text = "Default is off (easiest access). Changing this asks for admin approval once.",
                Left = 34, Top = 108, Width = 410, ForeColor = Color.Gray, AutoSize = true
            };

            _startup = new CheckBox
            {
                Text = "Open the PrintSave tray when I sign in",
                Left = 16, Top = 146, Width = 430, AutoSize = true,
                Checked = StartupManager.IsEnabled()
            };

            var close = new Button { Text = "Close", Left = 356, Top = 206, Width = 88, DialogResult = DialogResult.OK };

            _secure.CheckedChanged += (s, e) => ToggleSecure();
            _startup.CheckedChanged += (s, e) => { if (!_suppress) StartupManager.SetEnabled(_startup.Checked); };

            Controls.AddRange(new Control[] { lblRoot, _root, open, _secure, secureNote, _startup, close });
            AcceptButton = close;
        }

        private void OpenRoot()
        {
            try
            {
                Directory.CreateDirectory(_root.Text);
                Process.Start("explorer.exe", "\"" + _root.Text + "\"");
            }
            catch { }
        }

        private void ToggleSecure()
        {
            if (_suppress) return;
            bool want = _secure.Checked;
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--apply-security " + (want ? "on" : "off"))
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process p = Process.Start(psi);
                if (p != null) p.WaitForExit();
            }
            catch (Win32Exception)
            {
                // UAC cancelled — fall through and re-sync to the actual saved state.
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "PrintSave");
            }

            // Reflect whatever actually got persisted.
            _suppress = true;
            _secure.Checked = ConfigStore.Load().SecureArchiveFolder;
            _suppress = false;
        }
    }
}
