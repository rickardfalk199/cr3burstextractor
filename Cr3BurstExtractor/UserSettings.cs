using System;
using System.IO;
using System.Text.Json;

namespace Cr3BurstExtractor;

/// <summary>
/// File-backed persistence for user preferences. Lives in %APPDATA%\Cr3BurstExtractor\settings.json.
/// All IO is best-effort — failures are swallowed so a broken settings folder never blocks the app.
/// Call <see cref="Save"/> explicitly to write changes to disk.
/// </summary>
internal static class UserSettings
{
    static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cr3BurstExtractor");

    static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

    // Pre-0.1 builds used a marker file. Kept here so existing users don't get re-prompted by the splash.
    static string LegacySkipSplashFile => Path.Combine(SettingsDir, "skip_splash");

    sealed class Data
    {
        public bool SkipSplash { get; set; }

        public string? ScanFolder { get; set; }

        public string? BackupFolder { get; set; }

        // Default true preserves prior behaviour (move originals away after extraction)
        // for users upgrading from a build without this flag.
        public bool MoveOriginalsToBackup { get; set; } = true;
    }

    static readonly Data _data = Load();

    static Data Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(SettingsFile));
                if (d != null) return d;
            }

            if (File.Exists(LegacySkipSplashFile))
                return new Data { SkipSplash = true };
        }
        catch
        {
            /* fall through to defaults */
        }

        return new Data();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(
                SettingsFile,
                JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(LegacySkipSplashFile)) File.Delete(LegacySkipSplashFile);
        }
        catch
        {
            /* best-effort */
        }
    }

    public static bool SkipSplash
    {
        get => _data.SkipSplash;
        set => _data.SkipSplash = value;
    }

    public static string? ScanFolder
    {
        get => _data.ScanFolder;
        set => _data.ScanFolder = value;
    }

    public static string? BackupFolder
    {
        get => _data.BackupFolder;
        set => _data.BackupFolder = value;
    }

    public static bool MoveOriginalsToBackup
    {
        get => _data.MoveOriginalsToBackup;
        set => _data.MoveOriginalsToBackup = value;
    }
}