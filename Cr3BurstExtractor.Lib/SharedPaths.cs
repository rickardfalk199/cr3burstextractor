using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Cr3BurstExtractor;

/// <summary>
/// Single source of truth for shared on-disk locations used by both the
/// interactive WinForms tool and the Windows Service worker. State lives in
/// <c>%ProgramData%\Cr3BurstExtractor\</c> so a service running as
/// <c>LocalSystem</c> sees the same files as the user-mode app
/// (per-user <c>%APPDATA%</c> would split them across profiles).
///
/// On first read, settings/cache files are migrated from the legacy
/// <c>%APPDATA%</c> location so existing installs upgrade silently.
///
/// Tests can override the directory via the <c>CR3BURST_DATA_DIR</c> env var
/// so the static UserSettings / NonBurstCache singletons don't touch the
/// real ProgramData store.
/// </summary>
public static class SharedPaths
{
    const string FolderName = "Cr3BurstExtractor";
    const string DataDirEnvVar = "CR3BURST_DATA_DIR";

    public static string Dir
    {
        get
        {
            string? overridePath = Environment.GetEnvironmentVariable(DataDirEnvVar);
            if (!string.IsNullOrEmpty(overridePath)) return overridePath;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                FolderName);
        }
    }

    public static string LegacyDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        FolderName);

    public static string SettingsFile  => Path.Combine(Dir, "settings.json");
    public static string CacheFile     => Path.Combine(Dir, "non_burst_cache.json");
    public static string ServiceLogFile => Path.Combine(Dir, "service.log");

    /// <summary>
    /// Ensures <see cref="Dir"/> exists. On Windows, also grants
    /// <c>BUILTIN\Users</c> Modify rights so a LocalSystem-created folder
    /// remains writable by the interactive user (and vice versa). Best-effort:
    /// access denied is swallowed so non-admin first-runs still work; the
    /// service install step (elevated) will fix it for everyone.
    /// </summary>
    public static void EnsureDir()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (OperatingSystem.IsWindows())
                TryGrantUsersModify(Dir);
        }
        catch
        {
            /* best-effort */
        }
    }

    /// <summary>
    /// If <paramref name="fileName"/> doesn't exist in <see cref="Dir"/> but
    /// the same-named file exists in <see cref="LegacyDir"/>, copies it across.
    /// One-time migration that lets existing %APPDATA% installs upgrade
    /// without losing settings or the non-burst cache.
    /// </summary>
    public static void MigrateLegacyIfNeeded(string fileName)
    {
        try
        {
            string target = Path.Combine(Dir, fileName);
            if (File.Exists(target)) return;

            string legacy = Path.Combine(LegacyDir, fileName);
            if (!File.Exists(legacy)) return;

            EnsureDir();
            File.Copy(legacy, target, overwrite: false);
        }
        catch
        {
            /* best-effort — old file stays in place, new install starts fresh */
        }
    }

    /// <summary>
    /// Atomic write helper: writes <paramref name="content"/> to
    /// <paramref name="path"/>.tmp then renames over the target. Prevents a
    /// torn file when both the WinForms tool and the service write the same
    /// JSON concurrently.
    /// </summary>
    public static void AtomicWriteAllText(string path, string content)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        // File.Move with overwrite=true is atomic on NTFS for same-volume rename.
        File.Move(tmp, path, overwrite: true);
    }

    [SupportedOSPlatform("windows")]
    static void TryGrantUsersModify(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            var security = info.GetAccessControl();
            var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var rule = new FileSystemAccessRule(
                usersSid,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            security.AddAccessRule(rule);
            info.SetAccessControl(security);
        }
        catch
        {
            /* swallowed — Users may already have Modify, or we're a
               non-admin user creating the dir for ourselves */
        }
    }
}
