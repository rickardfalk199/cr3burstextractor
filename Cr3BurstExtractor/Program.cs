using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

namespace Cr3BurstExtractor;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Console subcommands need stdout/stderr visible in the parent shell.
        // Because OutputType=WinExe, no console is attached by default —
        // AttachConsole hooks us into the parent's console if there is one.
        if (args.Length > 0 && IsConsoleSubcommand(args[0]))
            AttachParentConsole();

        if (args.Length > 0)
        {
            switch (args[0])
            {
                // One-shot icon generator: produces a multi-resolution app.ico
                // from the burst-stack logo. Used at build setup time so the
                // published .exe gets a custom icon in Explorer.
                case "--generate-icon":
                    string outPath = args.Length > 1 ? args[1] : "app.ico";
                    IconExporter.WriteMultiResIcon(outPath, new[] { 16, 32, 48, 64, 128, 256 });
                    return 0;

                case "--service":
                    RunService(args);
                    return 0;

                case "--install":   return ServiceCommands.Install();
                case "--uninstall": return ServiceCommands.Uninstall();
                case "--start":     return ServiceCommands.Start();
                case "--stop":      return ServiceCommands.Stop();
                case "--status":    return ServiceCommands.Status();

                case "--help":
                case "-h":
                case "/?":
                    PrintUsage();
                    return 0;
            }
        }

        // Default: interactive WinForms app.
        ApplicationConfiguration.Initialize();

        if (!UserSettings.SkipSplash)
        {
            using var splash = new SplashForm();
            if (splash.ShowDialog() != DialogResult.OK) return 0;
        }

        Application.Run(new MainForm());
        return 0;
    }

    static void RunService(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(o => o.ServiceName = ServiceStatus.ServiceName);
        builder.Services.AddHostedService<ServiceWorker>();

        builder.Logging.ClearProviders();
        if (OperatingSystem.IsWindows())
            builder.Logging.AddEventLog(o => o.SourceName = ServiceStatus.ServiceName);
        builder.Logging.AddProvider(new FileLoggerProvider(SharedPaths.ServiceLogFile));

        var host = builder.Build();
        host.Run();
    }

    static bool IsConsoleSubcommand(string arg) => arg switch
    {
        "--install" or "--uninstall" or "--start" or "--stop" or "--status"
            or "--help" or "-h" or "/?" or "--generate-icon" => true,
        _ => false,
    };

    static void PrintUsage()
    {
        Console.WriteLine("CR3 Burst Extractor — interactive UI by default.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Cr3BurstExtractor.exe                  Launch the WinForms UI.");
        Console.WriteLine("  Cr3BurstExtractor.exe --service        Run as a Windows Service (used by sc.exe).");
        Console.WriteLine("  Cr3BurstExtractor.exe --install        Install the Windows Service (elevates via UAC).");
        Console.WriteLine("  Cr3BurstExtractor.exe --uninstall      Remove the Windows Service (elevates via UAC).");
        Console.WriteLine("  Cr3BurstExtractor.exe --start          Start the installed service.");
        Console.WriteLine("  Cr3BurstExtractor.exe --stop           Stop the installed service.");
        Console.WriteLine("  Cr3BurstExtractor.exe --status         Show the service's current status via sc.exe.");
        Console.WriteLine("  Cr3BurstExtractor.exe --generate-icon  Regenerate app.ico (build helper).");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AttachConsole(int dwProcessId);
    const int AttachParentProcessId = -1;

    static void AttachParentConsole()
    {
        try { AttachConsole(AttachParentProcessId); } catch { /* no console; harmless */ }
    }
}
