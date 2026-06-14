using System.Diagnostics;
using System.Security.Principal;

namespace Cr3BurstExtractor;

/// <summary>
/// Console-mode subcommand handlers for installing, uninstalling, starting,
/// stopping, and querying the Windows Service. Each shells out to
/// <c>sc.exe</c>. All five operations require admin; the dispatcher
/// re-launches the current exe elevated via UAC and exits if the user is
/// not already an administrator.
/// </summary>
public static class ServiceCommands
{
    public const string ServiceName = "Cr3BurstExtractor";
    const string DisplayName        = "CR3 Burst Extractor";
    const string Description        = "Watches the scan folder configured in the CR3 Burst Extractor app and auto-extracts new burst CR3 files as they appear.";

    public static int Install()
    {
        if (!EnsureElevated("--install", out int relaunchExit)) return relaunchExit;
        string exe = ExecutablePath();
        int rc = Run("sc.exe", $"create {ServiceName} binPath= \"\\\"{exe}\\\" --service\" start= auto DisplayName= \"{DisplayName}\"");
        if (rc != 0) return rc;
        Run("sc.exe", $"description {ServiceName} \"{Description}\"");
        Console.WriteLine($"Service '{ServiceName}' installed. Use --start to start it.");
        return 0;
    }

    public static int Uninstall()
    {
        if (!EnsureElevated("--uninstall", out int relaunchExit)) return relaunchExit;
        // Best-effort stop first; sc delete on a running service leaves it
        // marked for delete until the next reboot.
        Run("sc.exe", $"stop {ServiceName}");
        int rc = Run("sc.exe", $"delete {ServiceName}");
        if (rc == 0) Console.WriteLine($"Service '{ServiceName}' removed.");
        return rc;
    }

    public static int Start()
    {
        if (!EnsureElevated("--start", out int relaunchExit)) return relaunchExit;
        return Run("sc.exe", $"start {ServiceName}");
    }

    public static int Stop()
    {
        if (!EnsureElevated("--stop", out int relaunchExit)) return relaunchExit;
        return Run("sc.exe", $"stop {ServiceName}");
    }

    public static int Status()
    {
        // Read-only; no elevation needed.
        return Run("sc.exe", $"query {ServiceName}");
    }

    static int Run(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (!string.IsNullOrWhiteSpace(stdout)) Console.Write(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.Write(stderr);
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to run {fileName} {args}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// If already running elevated, returns true. Otherwise re-launches the
    /// current exe with the given <paramref name="subcommand"/> via UAC and
    /// returns false (after setting <paramref name="relaunchExit"/> to the
    /// child's exit code, or 1223 on UAC cancel).
    /// </summary>
    static bool EnsureElevated(string subcommand, out int relaunchExit)
    {
        relaunchExit = 0;
        if (IsElevated()) return true;

        try
        {
            var psi = new ProcessStartInfo(ExecutablePath(), subcommand)
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            using var p = Process.Start(psi);
            if (p == null) { relaunchExit = 1; return false; }
            p.WaitForExit();
            relaunchExit = p.ExitCode;
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled the UAC prompt.
            Console.Error.WriteLine("error: elevation cancelled.");
            relaunchExit = 1223;
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to elevate: {ex.Message}");
            relaunchExit = 1;
            return false;
        }
    }

    static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    static string ExecutablePath()
    {
        // Environment.ProcessPath is the actual exe (works with single-file publish);
        // Assembly.Location is empty in single-file. Fall back to Process for safety.
        string? p = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(p)) return p;
        return Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine executable path.");
    }
}
