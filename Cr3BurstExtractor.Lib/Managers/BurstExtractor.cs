namespace Cr3BurstExtractor.Managers;

/// <summary>
/// Path-based convenience wrappers over <see cref="BurstReader"/>. These exist
/// for callers that already work in terms of file paths (CLI, the form's
/// scan-and-extract loop). New callers that have streams in hand should use
/// <see cref="BurstReader"/> directly.
/// </summary>
public static class BurstExtractor
{
    /// <summary>
    /// Returns the number of frames in a CR3 file (max sample count across image traks).
    /// 0 if the file lacks a moov; 1 for a single-frame CR3; >1 for a burst.
    /// </summary>
    public static int GetFrameCount(string cr3Path)
    {
        using var fs = File.OpenRead(cr3Path);
        return GetFrameCount(fs);
    }

    /// <summary>
    /// Stream-based frame counter. Returns 0 if the stream isn't a parseable
    /// CR3 (no moov / no image traks), to match the path-based overload's
    /// historical behavior — used by callers that just want to classify a file
    /// without throwing.
    /// </summary>
    public static int GetFrameCount(Stream input)
    {
        try
        {
            using var reader = BurstReader.Open(input);
            return reader.FrameCount;
        }
        catch (InvalidDataException)
        {
            return 0;
        }
    }

    public static int Extract(string cr3Path, string outputDir)
    {
        if (!File.Exists(cr3Path)) throw new FileNotFoundException("File not found.", cr3Path);
        Directory.CreateDirectory(outputDir);

        using var fs = File.OpenRead(cr3Path);
        using var reader = BurstReader.Open(fs);

        if (reader.FrameCount == 1)
            Console.WriteLine("Only one frame found – this may not be a burst roll.");

        string baseName = Path.GetFileNameWithoutExtension(cr3Path);
        int digits = reader.FrameCount.ToString().Length;
        int written = 0;

        for (int frameIdx = 0; frameIdx < reader.FrameCount; frameIdx++)
        {
            string outName = $"{baseName}_{(frameIdx + 1).ToString($"D{digits}")}.CR3";
            string outPath = Path.Combine(outputDir, outName);

            using (var outStream = File.Create(outPath))
                reader.ExtractFrame(frameIdx, outStream);

            // We just wrote a single-frame CR3 — pre-seed the non-burst cache so
            // the next scan doesn't have to open and parse it.
            NonBurstCache.MarkNonBurst(outPath, new FileInfo(outPath));

            long size = new FileInfo(outPath).Length;
            Console.WriteLine($"  [{frameIdx + 1}/{reader.FrameCount}] {outName}  ({size:N0} bytes)");
            written++;
        }

        return written;
    }
}
