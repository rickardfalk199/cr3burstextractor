using System.Text;
using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor.Managers;

/// <summary>
/// Clones the source moov subtree, trimming each track's sample table down to
/// one frame and rewriting the durations to match.  Co64/stco offsets are
/// emitted as 0 here and filled in later by <see cref="BoxPatcher.PatchOffsets"/>
/// once the new file layout is known.
/// </summary>
public static class MoovBuilder
{
    // ----------------------------------------------------------
    // Build a patched moov (deep clone with sample tables trimmed to one frame)
    // Returns the bytes; also returns the size via out param.
    // ----------------------------------------------------------
    public static byte[] BuildPatched(
        byte[] src,
        Box moovBox,
        List<(byte[] Data, int TrakIdx)> frameSamples,
        int frameIdx,
        byte[]? frameJpeg,
        out long moovSize)
    {
        using var ms = new MemoryStream();

        // We will write the moov content, then prepend the 8-byte header.
        // To know the total size we write content first, then prepend.

        using var content = new MemoryStream();

        // ---- Pre-compute single-frame durations ----
        // The roll's mvhd/tkhd/mdhd durations span all N frames; left as-is they
        // make the 1-sample file look like an N-frame movie, which Adobe (Lightroom/
        // Photoshop) treats as video and refuses to open as a still raw. We rewrite
        // every duration to one frame so the headers agree with the 1-sample tables.
        var trakList = moovBox.Children.Where(c => c.Type == "trak").ToList();
        Box? mvhd = moovBox.Children.FirstOrDefault(c => c.Type == "mvhd");
        uint mvhdTs = mvhd != null ? SampleTableReader.ReadMvhdMdhdTimescale(src, mvhd) : 0;

        var mediaDur = new uint[trakList.Count]; // per-frame duration in each track's media timescale
        var trackDur = new uint[trakList.Count]; // same, expressed in the movie timescale (for tkhd)
        uint movieDur = 0;
        for (int i = 0; i < trakList.Count; i++)
        {
            var stbl = BoxQuery.GetStbl(trakList[i]);
            var mdhd = trakList[i].Children.FirstOrDefault(c => c.Type == "mdia")?
                .Children.FirstOrDefault(c => c.Type == "mdhd");
            uint mdhdTs = mdhd != null ? SampleTableReader.ReadMvhdMdhdTimescale(src, mdhd) : 0;
            uint delta = stbl != null ? SampleTableReader.ReadFirstSttsDelta(src, stbl) : 0;
            if (delta == 0) delta = 1;
            mediaDur[i] = delta;
            trackDur[i] = (mvhdTs != 0 && mdhdTs != 0)
                ? (uint)((long)delta * mvhdTs / mdhdTs)
                : delta;
            if (trackDur[i] > movieDur) movieDur = trackDur[i];
        }

        foreach (var child in moovBox.Children)
        {
            if (child.Type == "trak")
            {
                // Find trak index
                int ti = trakList.IndexOf(child);

                WritePatchedTrak(
                    content,
                    src,
                    child,
                    frameSamples[ti].Data.Length,
                    frameIdx,
                    ti,
                    mediaDur[ti],
                    trackDur[ti]);
            }
            else if (child.Type == "mvhd")
            {
                // Patch movie duration down to a single frame
                content.Write(BoxPatcher.PatchDuration(BoxQuery.GetRawBox(src, child), DurField.Mvhd, movieDur));
            }
            else if (child.Type == "THMB" && frameJpeg != null)
            {
                // Replace the roll-level thumbnail with this frame's JPEG so
                // Lightroom and Explorer don't show frame 0's thumbnail before
                // switching to the correct PRVW preview.
                content.Write(ThmbBuilder.BuildWithJpeg(BoxQuery.GetRawBox(src, child), frameJpeg));
            }
            else
            {
                // Copy the box verbatim (CMT1, CMT2, CMT3, CMT4, CNCV, uuid, etc.)
                content.Write(BoxQuery.GetRawBox(src, child));
            }
        }

        byte[] contentBytes = content.ToArray();
        moovSize = 8 + contentBytes.Length;

        BinaryHelpers.WriteUInt32BE(ms, (uint)moovSize);
        ms.Write(Encoding.ASCII.GetBytes("moov"));
        ms.Write(contentBytes);

        return ms.ToArray();
    }

    // ----------------------------------------------------------
    // Write a patched trak box: same metadata, but stts/stsc/stsz/co64 trimmed to 1 sample
    // ----------------------------------------------------------
    static void WritePatchedTrak(
        Stream dest,
        byte[] src,
        Box trak,
        long sampleSize,
        int frameIdx,
        int trakIdx,
        uint mediaDuration,
        uint trackDuration)
    {
        using var trakContent = new MemoryStream();

        foreach (var child in trak.Children)
        {
            if (child.Type == "mdia")
                WritePatchedMdia(trakContent, src, child, sampleSize, frameIdx, mediaDuration);
            else if (child.Type == "tkhd")
                trakContent.Write(
                    BoxPatcher.PatchDuration(BoxQuery.GetRawBox(src, child), DurField.Tkhd, trackDuration));
            else
                trakContent.Write(BoxQuery.GetRawBox(src, child)); // edts, etc.
        }

        byte[] tc = trakContent.ToArray();
        BinaryHelpers.WriteUInt32BE(dest, (uint)(tc.Length + 8));
        dest.Write(Encoding.ASCII.GetBytes("trak"));
        dest.Write(tc);
    }

    static void WritePatchedMdia(
        Stream dest,
        byte[] src,
        Box mdia,
        long sampleSize,
        int frameIdx,
        uint mediaDuration)
    {
        using var mdiaContent = new MemoryStream();

        foreach (var child in mdia.Children)
        {
            if (child.Type == "minf")
                WritePatchedMinf(mdiaContent, src, child, sampleSize, frameIdx, mediaDuration);
            else if (child.Type == "mdhd")
                mdiaContent.Write(
                    BoxPatcher.PatchDuration(BoxQuery.GetRawBox(src, child), DurField.Mdhd, mediaDuration));
            else
                mdiaContent.Write(BoxQuery.GetRawBox(src, child)); // hdlr
        }

        byte[] mc = mdiaContent.ToArray();
        BinaryHelpers.WriteUInt32BE(dest, (uint)(mc.Length + 8));
        dest.Write(Encoding.ASCII.GetBytes("mdia"));
        dest.Write(mc);
    }

    static void WritePatchedMinf(
        Stream dest,
        byte[] src,
        Box minf,
        long sampleSize,
        int frameIdx,
        uint mediaDuration)
    {
        using var minfContent = new MemoryStream();

        foreach (var child in minf.Children)
        {
            if (child.Type == "stbl")
                WritePatchedStbl(minfContent, src, child, sampleSize, frameIdx, mediaDuration);
            else
                minfContent.Write(BoxQuery.GetRawBox(src, child)); // vmhd, nmhd, dinf, etc.
        }

        byte[] mc = minfContent.ToArray();
        BinaryHelpers.WriteUInt32BE(dest, (uint)(mc.Length + 8));
        dest.Write(Encoding.ASCII.GetBytes("minf"));
        dest.Write(mc);
    }

    static void WritePatchedStbl(
        Stream dest,
        byte[] src,
        Box stbl,
        long sampleSize,
        int frameIdx,
        uint mediaDuration)
    {
        using var stblContent = new MemoryStream();

        foreach (var child in stbl.Children)
        {
            switch (child.Type)
            {
                case "stsz":
                    // Rewrite: 1 sample with the correct size
                    SampleTableWriter.WriteStsz(stblContent, (uint)sampleSize);
                    break;

                case "stts":
                    // 1 entry: 1 sample, duration = one frame (matches mdhd)
                    SampleTableWriter.WriteStts(stblContent, mediaDuration);
                    break;

                case "stsc":
                    // 1 chunk, 1 sample, stsd index 1
                    SampleTableWriter.WriteStsc(stblContent);
                    break;

                case "co64":
                    // Placeholder offset 0 — will be patched later
                    SampleTableWriter.WriteCo64(stblContent, 0);
                    break;

                case "stco":
                    // Placeholder offset 0 — will be patched later
                    SampleTableWriter.WriteStco(stblContent, 0);
                    break;

                case "free":
                    // Drop padding inside stbl. The roll leaves free boxes between the
                    // sample-table boxes; Adobe Camera Raw's CR3 parser fails to open a
                    // file that has them (verified with Adobe DNG Converter — removing
                    // them is the single change that turns "program error" into success).
                    break;

                default:
                    // stsd (contains CRAW/CMP1/CDI1/IAD1/JPEG — essential), and anything else
                    stblContent.Write(BoxQuery.GetRawBox(src, child));
                    break;
            }
        }

        byte[] sc = stblContent.ToArray();
        BinaryHelpers.WriteUInt32BE(dest, (uint)(sc.Length + 8));
        dest.Write(Encoding.ASCII.GetBytes("stbl"));
        dest.Write(sc);
    }
}