using Cr3BurstExtractor.Managers;

namespace Cr3BurstExtractor;

public enum AutoExtractOutcome
{
    /// File was already in the non-burst cache; nothing was done.
    Cached,
    /// File was inspected and found to contain a single frame; cache was updated.
    SkippedNonBurst,
    /// File was a burst and was extracted into a sibling folder.
    Extracted,
    /// An exception was thrown while processing this file.
    Error,
}

public sealed record AutoExtractResult(
    AutoExtractOutcome Outcome,
    int FrameCount,
    string? OutputDir,
    string? MovedTo,
    Exception? Error);

/// <summary>
/// Per-file orchestrator shared by the interactive form's scan-and-extract
/// loop and the background Windows Service worker. Owns the cache-check →
/// classify → extract → move-original flow so both call sites stay in lock
/// step. Pure logic — no UI, no file watching.
///
/// Log lines emitted to <paramref name="log"/> match the format the form
/// has historically written to its console (preserved byte-for-byte so the
/// log textbox in the form looks the same after this refactor).
/// </summary>
public static class AutoExtractor
{
    public static AutoExtractResult ProcessFile(
        string cr3Path,
        bool moveOriginals,
        string? backupFolder,
        Action<string>? log)
    {
        try
        {
            var info = new FileInfo(cr3Path);
            if (NonBurstCache.IsKnownNonBurst(cr3Path, info))
                return new AutoExtractResult(AutoExtractOutcome.Cached, 1, null, null, null);

            int frames = BurstExtractor.GetFrameCount(cr3Path);
            if (frames <= 1)
            {
                log?.Invoke($"SKIP ({frames} frame): {cr3Path}");
                NonBurstCache.MarkNonBurst(cr3Path, info);
                NonBurstCache.Save();
                return new AutoExtractResult(AutoExtractOutcome.SkippedNonBurst, frames, null, null, null);
            }

            string parent = Path.GetDirectoryName(Path.GetFullPath(cr3Path))!;
            string outDir = Path.Combine(parent, Path.GetFileNameWithoutExtension(cr3Path));

            log?.Invoke($"BURST ({frames} frames): {cr3Path}");
            log?.Invoke($"  -> {outDir}");

            BurstExtractor.Extract(cr3Path, outDir);

            string? movedTo = null;
            if (moveOriginals && !string.IsNullOrEmpty(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
                movedTo = UniquePath(Path.Combine(backupFolder, Path.GetFileName(cr3Path)));
                File.Move(cr3Path, movedTo);
                log?.Invoke($"  moved original -> {movedTo}");
            }
            else
            {
                log?.Invoke($"  original left in place: {cr3Path}");
            }
            log?.Invoke(string.Empty);

            NonBurstCache.Save();
            return new AutoExtractResult(AutoExtractOutcome.Extracted, frames, outDir, movedTo, null);
        }
        catch (Exception ex)
        {
            log?.Invoke($"ERROR processing {cr3Path}: {ex.Message}");
            return new AutoExtractResult(AutoExtractOutcome.Error, 0, null, null, ex);
        }
    }

    /// <summary>
    /// Returns <paramref name="path"/> if it doesn't exist, otherwise appends
    /// <c>_1</c>, <c>_2</c>, ... before the extension until an unused name is found.
    /// </summary>
    public static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
