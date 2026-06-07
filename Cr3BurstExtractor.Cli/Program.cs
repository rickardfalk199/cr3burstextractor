using Cr3BurstExtractor.Managers;

namespace Cr3BurstExtractor.Cli;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 64;
    private const int ExitNotFound = 66;
    private const int ExitFailure = 1;

    private static int Main(string[] args)
    {
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

            string outputDir = positional.Count == 2
                ? positional[1]
                : Path.GetDirectoryName(Path.GetFullPath(input)) ?? Directory.GetCurrentDirectory();

            int written = BurstExtractor.Extract(input, outputDir);
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
        w.WriteLine();
        w.WriteLine("If output-dir is omitted, frames are written next to the input file.");
        w.WriteLine("On success the final stdout line is 'EXTRACTED <n>' (or 'FRAMES <n>' for --count-only).");
    }
}
