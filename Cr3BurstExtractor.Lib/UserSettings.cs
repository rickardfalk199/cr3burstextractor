using System.Text.Json;

namespace Cr3BurstExtractor;

/// <summary>
/// File-backed persistence for user preferences. Stored in
/// <c>%ProgramData%\Cr3BurstExtractor\settings.json</c> so the interactive
/// WinForms tool, the CLI, and the Windows Service (running as LocalSystem)
/// all see the same file. See <see cref="SharedPaths"/> for the path policy
/// and legacy %APPDATA% migration.
///
/// All IO is best-effort — failures are swallowed so a broken settings folder
/// never blocks the app. Call <see cref="Save"/> explicitly to write changes.
/// </summary>
public static class UserSettings
{
    // Pre-0.1 builds used a marker file in %APPDATA%. Kept here so existing
    // users don't get re-prompted by the splash.
    static string LegacySkipSplashFile => Path.Combine(SharedPaths.LegacyDir, "skip_splash");

    sealed class Data
    {
        public bool SkipSplash { get; set; }

        public string? ScanFolder { get; set; }

        public string? BackupFolder { get; set; }

        // Default true preserves prior behaviour (move originals away after extraction)
        // for users upgrading from a build without this flag.
        public bool MoveOriginalsToBackup { get; set; } = true;

        // When enabled, the background Windows Service auto-extracts new burst
        // CR3s as they appear in ScanFolder. The standalone tool works whether
        // this is on or off; the toggle only affects the service.
        public bool AutoExtractOnNewFiles { get; set; }
    }

    static Data _data = Load();

    static Data Load()
    {
        try
        {
            SharedPaths.MigrateLegacyIfNeeded("settings.json");

            if (File.Exists(SharedPaths.SettingsFile))
            {
                var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(SharedPaths.SettingsFile));
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

    /// <summary>
    /// Re-reads settings from disk and replaces the in-memory snapshot. The
    /// Windows Service worker calls this after it sees settings.json change
    /// on disk so toggling a value from the standalone tool takes effect
    /// without a service restart.
    /// </summary>
    public static void Reload() => _data = Load();

    public static void Save()
    {
        try
        {
            SharedPaths.EnsureDir();
            string json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            SharedPaths.AtomicWriteAllText(SharedPaths.SettingsFile, json);
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

    public static bool AutoExtractOnNewFiles
    {
        get => _data.AutoExtractOnNewFiles;
        set => _data.AutoExtractOnNewFiles = value;
    }
}
