using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PrintSave.Tray
{
    /// <summary>Builds the app icon once (a simple blue "P").</summary>
    internal static class TrayIcon
    {
        private static Icon _cached;

        public static Icon Build()
        {
            if (_cached != null) return _cached;
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var bg = new SolidBrush(Color.FromArgb(0x2D, 0x6C, 0xDF)))
                    g.FillRectangle(bg, 2, 2, 28, 28);
                using (var f = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fg = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("P", f, fg, new RectangleF(2, 1, 28, 28), sf);
                }
                _cached = Icon.FromHandle(bmp.GetHicon()); // handle lives for the app's lifetime
            }
            return _cached;
        }
    }

    /// <summary>Toggles "start at sign-in" via the per-user Run key.</summary>
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PrintSaveTray";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static void SetEnabled(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true)
                                       ?? Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }
}
