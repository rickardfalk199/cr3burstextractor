using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor.Managers;

/// <summary>
/// Top-level orchestrator: parses a burst CR3, identifies the per-frame samples,
/// and dispatches <see cref="FrameBuilder"/> once per frame.
/// </summary>
public static class BurstExtractor
{
    /// <summary>
    /// Returns the number of frames in a CR3 file (max sample count across image traks).
    /// 0 if no moov / no traks / unreadable; 1 for a single-frame CR3; >1 for a burst.
    /// </summary>
    public static int GetFrameCount(string cr3Path)
    {
        byte[] fileBytes = File.ReadAllBytes(cr3Path);
        var topBoxes = BoxParser.ParseLevel(fileBytes, 0, fileBytes.Length);
        var moovBox = topBoxes.FirstOrDefault(b => b.Type == "moov");
        if (moovBox == null) return 0;

        int frameCount = 0;
        foreach (var trak in BoxQuery.FindAll(moovBox.Children, b => b.Type == "trak"))
        {
            var stbl = BoxQuery.GetStbl(trak);
            if (stbl == null) continue;
            var sizes = SampleTableReader.ReadStsz(fileBytes, stbl);
            if (sizes != null && sizes.Count > frameCount) frameCount = sizes.Count;
        }

        return frameCount;
    }

    public static int Extract(string cr3Path, string outputDir)
    {
        if (!File.Exists(cr3Path)) throw new FileNotFoundException("File not found.", cr3Path);
        Directory.CreateDirectory(outputDir);

        byte[] fileBytes = File.ReadAllBytes(cr3Path);

        // 1. Parse full box tree (keep raw bytes of every box)
        List<Box> topBoxes = BoxParser.ParseLevel(fileBytes, 0, fileBytes.Length);

        // 2. Locate required top-level boxes
        Box? ftypBox = topBoxes.FirstOrDefault(b => b.Type == "ftyp");
        Box? moovBox = topBoxes.FirstOrDefault(b => b.Type == "moov");
        if (moovBox == null) throw new InvalidDataException("No moov box found.");

        // 3. Collect all mdat ranges (there may be multiple in a roll)
        List<(long Offset, long Size)> mdats = BoxQuery.CollectMdat(topBoxes);
        if (mdats.Count == 0) throw new InvalidDataException("No mdat box found.");

        // 3b. Top-level uuid boxes (XMP / PRVW preview / CMTA) and the original
        //     mdat box offset.  These are referenced by the CTBO table inside moov
        //     and must be carried over (and CTBO re-pointed) for the file to be a
        //     valid, self-contained CR3 — otherwise CTBO points past EOF and
        //     Adobe/DPP refuse to decode the raw even though the preview shows.
        List<Box> topUuids = topBoxes.Where(b => b.Type == "uuid").ToList();
        long origMdatBoxOffset = topBoxes.First(b => b.Type == "mdat").RawOffset;

        // 4. Build a merged byte[] view of all mdat payload (offsets in the
        //    sample tables are absolute file offsets, so we keep the original
        //    file bytes and just slice from them directly).

        // 5. Walk tracks and collect per-track samples
        Box? innerUuid = BoxQuery.FindFirst(moovBox.Children, b => b.Type == "uuid");
        List<Box> traks = BoxQuery.FindAll(moovBox.Children, b => b.Type == "trak").ToList();

        if (traks.Count == 0)
            throw new InvalidDataException("No trak boxes found in moov.");

        // Determine how many frames are in the burst (= samples in any image trak)
        int frameCount = 0;
        foreach (var trak in traks)
        {
            var stbl = BoxQuery.GetStbl(trak);
            if (stbl == null) continue;
            var sizes = SampleTableReader.ReadStsz(fileBytes, stbl);
            if (sizes != null && sizes.Count > frameCount) frameCount = sizes.Count;
        }

        if (frameCount == 0) throw new InvalidDataException("No samples found in any track.");
        if (frameCount == 1)
        {
            Console.WriteLine("Only one frame found – this may not be a burst roll.");
        }

        string baseName = Path.GetFileNameWithoutExtension(cr3Path);
        int digits = frameCount.ToString().Length;
        int written = 0;

        for (int frameIdx = 0; frameIdx < frameCount; frameIdx++)
        {
            string outName = $"{baseName}_{(frameIdx + 1).ToString($"D{digits}")}.CR3";
            string outPath = Path.Combine(outputDir, outName);

            byte[] cr3 = FrameBuilder.Build(
                fileBytes,
                ftypBox,
                moovBox,
                traks,
                topUuids,
                origMdatBoxOffset,
                frameIdx);
            File.WriteAllBytes(outPath, cr3);

            // We just wrote a single-frame CR3 — pre-seed the non-burst cache so
            // the next scan doesn't have to open and parse it.
            NonBurstCache.MarkNonBurst(outPath, new FileInfo(outPath));

            Console.WriteLine($"  [{frameIdx + 1}/{frameCount}] {outName}  ({cr3.Length:N0} bytes)");
            written++;
        }

        return written;
    }
}