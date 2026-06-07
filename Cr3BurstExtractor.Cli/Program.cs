using Cr3BurstExtractor;
using Cr3BurstExtractor.Managers;

namespace Cr3BurstExtractor.Cli;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitFailure = 1;
    private const int ExitSkipped = 2;
    private const int ExitUsage = 64;
    private const int ExitNotFound = 66;

    private static int Main(string[] args)
    {
        // Settings shared with the WinForms tool (%APPDATA%\Cr3BurstExtractor\settings.json).
        if (args.Length == 1 && args[0] == "--get-scan-folder")
        {
            Console.WriteLine(UserSettings.ScanFolder ?? string.Empty);
            return ExitOk;
        }
        if (args.Length == 2 && args[0] == "--set-scan-folder")
        {
            UserSettings.ScanFolder = string.IsNullOrWhiteSpace(args[1]) ? null : args[1];
            UserSettings.Save();
            return ExitOk;
        }

        bool countOnly = false;
        var positional = new List<string>(args.Length);
        foreach (var arg in args)
        {
            if (arg is "--count-only" or "-c") countOnly = true;
            else if (arg is "--help" or "-h" or "/?") { PrintUsage(Console.Out); return ExitOk; }
            else positional.Add(arg);
        }

        if (positional.Count < 1 || positional.Count > 2)
        {
            PrintUsage(Console.Error);
            return ExitUsage;
        }

        string input = positional[0];
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"error: input file not found: {input}");
            return ExitNotFound;
        }

        try
        {
            if (countOnly)
            {
                int frames = BurstExtractor.GetFrameCount(input);
                Console.WriteLine($"FRAMES {frames}");
                return ExitOk;
            }

            var info = new FileInfo(input);
            if (NonBurstCache.IsKnownNonBurst(input, info))
            {
                Console.WriteLine("SKIPPED cached");
                return ExitSkipped;
            }

            int frameCount = BurstExtractor.GetFrameCount(input);
            if (frameCount <= 1)
            {
                NonBurstCache.MarkNonBurst(input, info);
                NonBurstCache.Save();
                Console.WriteLine("SKIPPED non-burst");
                return ExitSkipped;
            }

            string outputDir = positional.Count == 2
                ? positional[1]
                : Path.GetDirectoryName(Path.GetFullPath(input)) ?? Directory.GetCurrentDirectory();

            int written = BurstExtractor.Extract(input, outputDir);
            // Extract() pre-seeds each output frame as non-burst in the in-memory
            // cache; persist those marks so subsequent runs skip them.
            NonBurstCache.Save();
            Console.WriteLine($"EXTRACTED {written}");
            return ExitOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitFailure;
        }
    }

    private static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Cr3BurstExtractor.Cli — extract per-frame CR3s from a Canon burst roll");
        w.WriteLine();
        w.WriteLine("Usage:");
        w.WriteLine("  Cr3BurstExtractor.Cli <input.cr3> [output-dir]");
        w.WriteLine("  Cr3BurstExtractor.Cli --count-only <input.cr3>");
        w.WriteLine("  Cr3BurstExtractor.Cli --get-scan-folder");
        w.WriteLine("  Cr3BurstExtractor.Cli --set-scan-folder <path>");
        w.WriteLine();
        w.WriteLine("If output-dir is omitted, frames are written next to the input file.");
        w.WriteLine();
        w.WriteLine("Exit codes:");
        w.WriteLine("  0  EXTRACTED <n>     burst extracted, n frames written");
        w.WriteLine("  2  SKIPPED ...       single-frame CR3 (cached or just classified) — nothing written");
        w.WriteLine("  1  generic error;  64 usage error;  66 input not found");
    }
}
