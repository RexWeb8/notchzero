using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace NotchZero
{
    /// <summary>User settings, saved as JSON in %LOCALAPPDATA%\NotchZero\settings.json.</summary>
    internal sealed class AppSettings
    {
        public string Mascot { get; set; } = nameof(NotchZero.MascotKind.Cat);
        public bool ShowMascotWhenIdle { get; set; } = false;
        public bool ShowSongPeek { get; set; } = true;
        public bool AutoZone { get; set; } = true;
        public string Zone { get; set; } = "WLY01";
        public bool FirstRunDone { get; set; } = false;

        [JsonIgnore]
        public MascotKind MascotKind
        {
            get => Enum.TryParse(Mascot, out MascotKind kind) ? kind : NotchZero.MascotKind.Cat;
            set => Mascot = value.ToString();
        }

        private static string FilePath => Path.Combine(Log.Dir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Loading settings");
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Saving settings");
            }
        }
    }

    /// <summary>"Start with Windows", via the per-user Run key (no admin rights needed).</summary>
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "NotchZero";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(ValueName) != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled) key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\"");
                else key.DeleteValue(ValueName, false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Start with Windows");
            }
        }
    }
}
