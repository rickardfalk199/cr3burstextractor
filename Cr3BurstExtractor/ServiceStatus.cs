using System.Diagnostics;

namespace Cr3BurstExtractor;

public enum ServiceState
{
    NotInstalled,
    Stopped,
    StartPending,
    StopPending,
    Running,
    Unknown,
}

/// <summary>
/// Thin read-only wrapper around <c>sc.exe query</c>. We shell out rather
/// than take a <c>System.ServiceProcess.ServiceController</c> NuGet
/// dependency — keeps the project's dependency surface minimal and avoids
/// pulling System.ServiceProcess.* into the form's published exe.
/// </summary>
public static class ServiceStatus
{
    public const string ServiceName = "Cr3BurstExtractor";

    public static ServiceState QueryState()
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", $"query {ServiceName}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return ServiceState.Unknown;
            string stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            // sc query returns 1060 ("service does not exist") when not installed.
            if (p.ExitCode == 1060) return ServiceState.NotInstalled;
            if (p.ExitCode != 0) return ServiceState.Unknown;

            // Parse the "STATE              : 4  RUNNING" line.
            foreach (var line in stdout.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line[..colon].Trim();
                if (!string.Equals(key, "STATE", StringComparison.OrdinalIgnoreCase)) continue;
                string val = line[(colon + 1)..].Trim();
                if (val.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))      return ServiceState.Running;
                if (val.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))      return ServiceState.Stopped;
                if (val.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase)) return ServiceState.StartPending;
                if (val.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase))  return ServiceState.StopPending;
                return ServiceState.Unknown;
            }
            return ServiceState.Unknown;
        }
        catch
        {
            return ServiceState.Unknown;
        }
    }
}
