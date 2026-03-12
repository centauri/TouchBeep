#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace TouchBeep;

public static class Settings
{
    public static class Default
    {
        private static string Path => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TouchBeep", "settings.json");

        public static bool SoundEnabled
        {
            get => Load().SoundEnabled;
            set { var s = Load(); s.SoundEnabled = value; Save(s); }
        }

        public static int FrequencyHz
        {
            get => Load().FrequencyHz;
            set { var s = Load(); s.FrequencyHz = value; Save(s); }
        }

        public static int WaveType
        {
            get => Load().WaveType;
            set { var s = Load(); s.WaveType = value; Save(s); }
        }

        public static List<string> AllowedProcesses
        {
            get => Load().AllowedProcesses ?? new List<string>();
            set { var s = Load(); s.AllowedProcesses = value ?? new List<string>(); Save(s); }
        }

        private static SettingsData Load()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (File.Exists(Path))
                {
                    var json = File.ReadAllText(Path);
#if NETFRAMEWORK
                    var d = new JavaScriptSerializer().Deserialize<SettingsData>(json);
#else
                    var d = JsonSerializer.Deserialize<SettingsData>(json);
#endif
                    if (d != null) return d;
                }
            }
            catch { }
            return new SettingsData();
        }

        private static void Save(SettingsData d)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
#if NETFRAMEWORK
                var json = new JavaScriptSerializer().Serialize(d);
#else
                var json = JsonSerializer.Serialize(d);
#endif
                File.WriteAllText(Path, json);
            }
            catch { }
        }
    }

    private class SettingsData
    {
        public bool SoundEnabled { get; set; } = true;
        public int FrequencyHz { get; set; } = 800;
        public int WaveType { get; set; } = 0;
        public List<string>? AllowedProcesses { get; set; }
    }
}
