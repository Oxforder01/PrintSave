using System;
using System.IO;
using System.Web.Script.Serialization;

namespace PrintSave.Core
{
    /// <summary>Loads and saves <see cref="Config"/> as JSON at %ProgramData%\PrintSave\config.json.</summary>
    public static class ConfigStore
    {
        public static string ConfigPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "PrintSave", "config.json");
            }
        }

        /// <summary>Never throws; returns defaults if the file is missing or unreadable.</summary>
        public static Config Load()
        {
            try
            {
                string path = ConfigPath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    Config cfg = new JavaScriptSerializer().Deserialize<Config>(json);
                    if (cfg != null)
                    {
                        if (string.IsNullOrWhiteSpace(cfg.RootDir)) cfg.RootDir = @"C:\PrintSave";
                        return cfg;
                    }
                }
            }
            catch { }
            return new Config();
        }

        public static void Save(Config cfg)
        {
            string path = ConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = new JavaScriptSerializer().Serialize(cfg ?? new Config());
            File.WriteAllText(path, json);
        }
    }
}
