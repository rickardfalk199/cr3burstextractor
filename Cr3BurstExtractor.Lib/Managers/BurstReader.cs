using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor.Managers;

/// <summary>
/// Stream-based primary API for splitting a burst CR3 into per-frame CR3s.
/// Parses the box tree once at <see cref="Open"/> time, then lets callers
/// query <see cref="FrameCount"/> and pull individual frames out to any
/// writable <see cref="Stream"/> via <see cref="ExtractFrame"/>.
///
/// The reader buffers the entire input into a <c>byte[]</c> at Open time
/// (the existing extraction implementation operates on absolute file offsets
/// and needs random access). The "stream API" promise is about source/sink
/// flexibility — not zero-copy. A 50 MB burst CR3 costs ~50 MB of managed
/// memory while the reader is alive.
/// </summary>
public sealed class BurstReader : IDisposable
{
    readonly byte[] _fileBytes;
    readonly Box? _ftypBox;
    readonly Box _moovBox;
    readonly List<Box> _traks;
    readonly List<Box> _topUuids;
    readonly long _origMdatBoxOffset;

    BurstReader(
        byte[] fileBytes,
        Box? ftypBox,
        Box moovBox,
        List<Box> traks,
        List<Box> topUuids,
        long origMdatBoxOffset,
        int frameCount)
    {
        _fileBytes = fileBytes;
        _ftypBox = ftypBox;
        _moovBox = moovBox;
        _traks = traks;
        _topUuids = topUuids;
        _origMdatBoxOffset = origMdatBoxOffset;
        FrameCount = frameCount;
    }

    /// <summary>
    /// Parses a CR3 stream and returns a reader exposing per-frame extraction.
    /// The input stream is read to end and then no longer referenced — the
    /// caller is free to dispose it as soon as Open returns.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown if the file lacks a moov box, lacks mdat, or has no image traks.
    /// </exception>
    public static BurstReader Open(Stream input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        byte[] fileBytes = ReadAllBytes(input);

        List<Box> topBoxes = BoxParser.ParseLevel(fileBytes, 0, fileBytes.Length);

        Box? ftypBox = topBoxes.FirstOrDefault(b => b.Type == "ftyp");
        Box? moovBox = topBoxes.FirstOrDefault(b => b.Type == "moov");
        if (moovBox == null) throw new InvalidDataException("No moov box found.");

        List<(long Offset, long Size)> mdats = BoxQuery.CollectMdat(topBoxes);
        if (mdats.Count == 0) throw new InvalidDataException("No mdat box found.");

        List<Box> topUuids = topBoxes.Where(b => b.Type == "uuid").ToList();
        long origMdatBoxOffset = topBoxes.First(b => b.Type == "mdat").RawOffset;

        List<Box> traks = BoxQuery.FindAll(moovBox.Children, b => b.Type == "trak").ToList();
        if (traks.Count == 0)
            throw new InvalidDataException("No trak boxes found in moov.");

        int frameCount = 0;
        foreach (var trak in traks)
        {
            var stbl = BoxQuery.GetStbl(trak);
            if (stbl == null) continue;
            var sizes = SampleTableReader.ReadStsz(fileBytes, stbl);
            if (sizes != null && sizes.Count > frameCount) frameCount = sizes.Count;
        }

        if (frameCount == 0) throw new InvalidDataException("No samples found in any track.");

        return new BurstReader(fileBytes, ftypBox, moovBox, traks, topUuids, origMdatBoxOffset, frameCount);
    }

    /// <summary>
    /// Number of frames in the burst. 1 means a single-frame CR3 (still valid input).
    /// </summary>
    public int FrameCount { get; }

    /// <summary>
    /// Writes one self-contained single-frame CR3 to <paramref name="output"/>.
    /// The output stream is written and flushed, but NOT disposed — the caller
    /// owns its lifetime.
    /// </summary>
    public void ExtractFrame(int frameIndex, Stream output)
    {
        if (frameIndex < 0 || frameIndex >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameIndex), frameIndex,
                $"Frame index must be in [0, {FrameCount}).");
        if (output is null) throw new ArgumentNullException(nameof(output));

        byte[] cr3 = FrameBuilder.Build(
            _fileBytes,
            _ftypBox,
            _moovBox,
            _traks,
            _topUuids,
            _origMdatBoxOffset,
            frameIndex);
        output.Write(cr3, 0, cr3.Length);
        output.Flush();
    }

    public void Dispose()
    {
        // No unmanaged state; the fileBytes buffer is released when this
        // instance is collected. Dispose remains in the API surface so we
        // can add resource cleanup later without a breaking change, and so
        // `using var reader = ...` reads naturally at call sites.
    }

    static byte[] ReadAllBytes(Stream input)
    {
        if (input is MemoryStream ms && ms.TryGetBuffer(out var seg) && seg.Offset == 0 && seg.Count == seg.Array!.Length)
            return seg.Array;

        using var dst = new MemoryStream();
        input.CopyTo(dst);
        return dst.ToArray();
    }
}
