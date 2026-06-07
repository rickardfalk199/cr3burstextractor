using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cr3BurstExtractor;
using Cr3BurstExtractor.Helpers;
using Cr3BurstExtractor.Managers;
using Xunit;
using Xunit.Abstractions;

namespace Cr3BurstExtractor.Tests;

/// <summary>
/// Runs <see cref="BurstExtractor.Extract"/> on the test burst once and exposes
/// helpers for fetching the bytes of any of our extracted frames or any DPP
/// reference frame on demand.
/// </summary>
public sealed class ExtractionFixture : IDisposable
{
    /// <summary>Primary burst — has 5 DPP reference frames (1, 2, 3, 4, 11).</summary>
    public const string PrimaryBurst = "375A4182";

    /// <summary>Secondary burst — has 2 DPP reference frames (6, 10) for cross-burst diffs.</summary>
    public const string SecondaryBurst = "375A7575";

    public string TestDir { get; }
    public string BurstPath => GetBurstPath(PrimaryBurst);

    readonly Dictionary<string, BurstState> _states = new();

    sealed class BurstState
    {
        public string TempDir = "";
        public Dictionary<int, byte[]> OurCache = new();
        public Dictionary<int, byte[]> DppCache = new();
    }

    public ExtractionFixture()
    {
        TestDir = TestDataDir();
    }

    public string GetBurstPath(string burstStem) => Path.Combine(TestDir, burstStem + ".CR3");

    BurstState State(string burstStem)
    {
        if (!_states.TryGetValue(burstStem, out var s))
        {
            string burstPath = GetBurstPath(burstStem);
            if (!File.Exists(burstPath)) throw new FileNotFoundException($"Missing burst: {burstPath}");
            string tempDir = Path.Combine(Path.GetTempPath(),
                $"Cr3BurstExtractorTests_{burstStem}_" + Guid.NewGuid().ToString("N"));
            BurstExtractor.Extract(burstPath, tempDir);
            s = new BurstState { TempDir = tempDir };
            _states[burstStem] = s;
        }
        return s;
    }

    public byte[] DppFrame(int frame1Based) => DppFrame(PrimaryBurst, frame1Based);

    public byte[] DppFrame(string burstStem, int frame1Based)
    {
        var s = State(burstStem);
        if (!s.DppCache.TryGetValue(frame1Based, out var bytes))
        {
            string path = Path.Combine(TestDir, $"{burstStem}_{frame1Based:D2}.CR3");
            if (!File.Exists(path)) throw new FileNotFoundException($"Missing DPP reference: {path}");
            bytes = File.ReadAllBytes(path);
            s.DppCache[frame1Based] = bytes;
        }
        return bytes;
    }

    public byte[] OurFrame(int frame1Based) => OurFrame(PrimaryBurst, frame1Based);

    public byte[] OurFrame(string burstStem, int frame1Based)
    {
        var s = State(burstStem);
        if (!s.OurCache.TryGetValue(frame1Based, out var bytes))
        {
            string path = Directory.EnumerateFiles(s.TempDir, $"*_{frame1Based:D2}.CR3").FirstOrDefault()
                       ?? Directory.EnumerateFiles(s.TempDir, $"*_{frame1Based}.CR3").FirstOrDefault()
                       ?? throw new FileNotFoundException(
                              $"No extracted frame matched {frame1Based} in {s.TempDir}");
            bytes = File.ReadAllBytes(path);
            s.OurCache[frame1Based] = bytes;
        }
        return bytes;
    }

    public void Dispose()
    {
        foreach (var s in _states.Values)
        {
            try { if (Directory.Exists(s.TempDir)) Directory.Delete(s.TempDir, recursive: true); }
            catch { /* leave on disk if locked */ }
        }
    }

    /// <summary>
    /// Test data lives at Cr3BurstExtractor.Tests/TestBurst/ next to the .csproj.
    /// Walk up from the test assembly location until we find a parent containing
    /// a TestBurst directory — works whether tests run from the IDE, dotnet test,
    /// or a CI runner with arbitrary working directory.
    /// </summary>
    static string TestDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "TestBurst");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate TestBurst directory walking up from the test assembly. " +
            "Expected at Cr3BurstExtractor.Tests/TestBurst/.");
    }
}

/// <summary>
/// Two layers of tests:
///   1. "MatchesDpp" — for each DPP reference frame, verifies our extraction
///      produces the same per-track samples and preview payloads as DPP.
///   2. "Diagnose_DppPerFrames..." — compares DPP's own frames pairwise to
///      determine which preview boxes DPP rewrites per frame. The outcome
///      tells us where to invest if we want to fix the "all previews look
///      identical in Lightroom" symptom.
/// </summary>
public class ExtractionTests : IClassFixture<ExtractionFixture>
{
    readonly ExtractionFixture _fx;
    readonly ITestOutputHelper _out;

    public ExtractionTests(ExtractionFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // -------------------------------------------------------------------
    // 1. Match-against-DPP tests, run once per available DPP reference.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void TrackSamplesMatchDpp(int frame)
    {
        var dpp = ReadAllTrackFirstSamples(_fx.DppFrame(frame));
        var ours = ReadAllTrackFirstSamples(_fx.OurFrame(frame));

        Assert.True(dpp.Count > 0, "DPP reference has no image tracks?");
        Assert.Equal(dpp.Count, ours.Count);
        for (int i = 0; i < dpp.Count; i++)
        {
            Assert.True(
                dpp[i].SequenceEqual(ours[i]),
                $"Frame {frame} trak {i} sample bytes differ: DPP={dpp[i].Length}, ours={ours[i].Length}.");
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void PrvwJpegMatchesDpp(int frame)
    {
        var dpp = ExtractPrvwJpeg(_fx.DppFrame(frame));
        if (dpp == null) return; // DPP didn't write PRVW — nothing to compare.

        var ours = ExtractPrvwJpeg(_fx.OurFrame(frame));
        Assert.NotNull(ours);
        Assert.True(dpp.SequenceEqual(ours),
            $"Frame {frame} PRVW differs: DPP={dpp.Length}, ours={ours.Length}.");
    }

    /// <summary>
    /// Our THMB content intentionally diverges from DPP: we stuff the track-1
    /// JPEG into a correctly-structured THMB box rather than re-encoding a tiny
    /// 160x120 thumbnail the way DPP does. So we only assert presence here,
    /// not byte-equality. Per-frame uniqueness is covered by <see cref="OurThmbIsPerFrame"/>.
    /// </summary>
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void OurThmbExistsWhenDppHasIt(int frame)
    {
        var dpp = ExtractThmbJpeg(_fx.DppFrame(frame));
        if (dpp == null) return;

        var ours = ExtractThmbJpeg(_fx.OurFrame(frame));
        Assert.NotNull(ours);
    }

    [Fact]
    public void OurThmbIsPerFrame()
    {
        var f1 = ExtractThmbJpeg(_fx.OurFrame(1));
        var f4 = ExtractThmbJpeg(_fx.OurFrame(4));
        Assert.NotNull(f1);
        Assert.NotNull(f4);
        Assert.False(f1.SequenceEqual(f4),
            "Our THMB is IDENTICAL between extracted frame 1 and frame 4 — per-frame rewrite isn't working.");
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void ExifIfd1ThumbnailMatchesDpp(int frame)
    {
        var dpp = ExtractExifIfd1ThumbnailFromCmt1(_fx.DppFrame(frame));
        if (dpp == null) return;

        var ours = ExtractExifIfd1ThumbnailFromCmt1(_fx.OurFrame(frame));
        Assert.NotNull(ours);
        Assert.True(dpp.SequenceEqual(ours),
            $"Frame {frame} EXIF IFD1 thumbnail differs: DPP={dpp.Length}, ours={ours.Length}.");
    }

    // -------------------------------------------------------------------
    // Per-frame metadata-box parity with DPP.
    //
    // Canon CR3 stores its metadata as four nested boxes inside moov.uuid:
    //   CMT1 — main EXIF (IFD0) + optional IFD1 thumbnail chain
    //   CMT2 — EXIF SubIFD
    //   CMT3 — Canon MakerNote
    //   CMT4 — GPS
    //
    // Each test below asserts our output's bytes equal DPP's for that box.
    // Where DPP rewrites a box per-frame and we don't, the test fails — that's
    // the intended regression marker pointing at remaining work.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void Cmt1MatchesDpp(int frame) => AssertMoovUuidBoxMatchesDpp(frame, "CMT1");

    /// <summary>
    /// DPP makes consistent field-level edits to CMT2 (EXIF SubIFD) at fixed
    /// TIFF tag offsets — same offsets across different bursts (verified via
    /// Diagnose_Cmt2_OffsetSummary_BothBursts). We don't replicate those yet.
    /// Re-enable this test when CMT2 patching is implemented.
    /// </summary>
    [Theory(Skip = "TODO: replicate DPP's field-level CMT2 edits (~240 bytes at fixed TIFF tag offsets).")]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void Cmt2MatchesDpp(int frame) => AssertMoovUuidBoxMatchesDpp(frame, "CMT2");

    /// <summary>
    /// DPP rewrites CMT3 (Canon MakerNote) per-frame; large diff region starts
    /// at byte +1144/+1152 in both test bursts. Reproducing this requires
    /// parsing the Canon MakerNote TIFF IFD and updating per-frame indexed
    /// arrays. Re-enable when implemented.
    /// </summary>
    [Theory(Skip = "TODO: replicate DPP's per-frame CMT3 (MakerNote) rewrite — requires Canon MakerNote IFD parsing.")]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void Cmt3MatchesDpp(int frame) => AssertMoovUuidBoxMatchesDpp(frame, "CMT3");

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void Cmt4MatchesDpp(int frame) => AssertMoovUuidBoxMatchesDpp(frame, "CMT4");

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void CncvMatchesDpp(int frame) => AssertMoovUuidBoxMatchesDpp(frame, "CNCV");

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(11)]
    public void XmpUuidMatchesDpp(int frame)
    {
        // Top-level XMP uuid (be7acfcb-97a9-42e8-9c71-999491e3afac).
        var dpp = ExtractTopLevelUuidByIdentifier(_fx.DppFrame(frame), XmpUuid);
        var ours = ExtractTopLevelUuidByIdentifier(_fx.OurFrame(frame), XmpUuid);
        if (dpp == null && ours == null) return;
        Assert.NotNull(dpp);
        Assert.NotNull(ours);
        Assert.True(dpp.SequenceEqual(ours),
            $"Frame {frame} XMP uuid differs: DPP={dpp.Length}, ours={ours.Length}.");
    }

    void AssertMoovUuidBoxMatchesDpp(int frame, string boxType)
    {
        var dpp = ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(frame), boxType);
        var ours = ExtractRawBoxBytesInMoovUuid(_fx.OurFrame(frame), boxType);
        if (dpp == null && ours == null) return;
        Assert.NotNull(dpp);
        Assert.NotNull(ours);
        Assert.True(dpp.SequenceEqual(ours),
            $"Frame {frame} {boxType} differs: DPP={dpp.Length} bytes, ours={ours.Length} bytes.");
    }

    // -------------------------------------------------------------------
    // 2. Diagnostic: report what DPP does with each box.
    //
    // Single test that always passes; the test output (visible with
    // `dotnet test --verbosity detailed` or in any test-runner UI) is the
    // diagnostic. For each box we classify it as:
    //   ABSENT     — DPP omits the box entirely from single-frame output
    //   ROLL-LEVEL — DPP keeps the same bytes across frames
    //   PER-FRAME  — DPP writes different bytes per frame
    // -------------------------------------------------------------------

    [Fact]
    public void Diagnose_ThmbHeaderLayout()
    {
        DumpThmb("BURST",        File.ReadAllBytes(_fx.BurstPath));
        DumpThmb("DPP frame 1",  _fx.DppFrame(1));
        DumpThmb("DPP frame 4",  _fx.DppFrame(4));
        DumpThmb("OURS frame 1", _fx.OurFrame(1));
        DumpThmb("OURS frame 4", _fx.OurFrame(4));
    }

    void DumpThmb(string label, byte[] file)
    {
        var thmb = GetMoovUuidChildren(file).FirstOrDefault(c => c.Type == "THMB");
        if (thmb == null) { _out.WriteLine($"--- {label}: no THMB ---"); return; }

        int start = thmb.RawOffset;
        int size  = thmb.RawSize;
        int dump  = Math.Min(48, size);

        _out.WriteLine($"--- {label}: THMB at file offset {start}, total box size {size} ---");
        for (int i = 0; i < dump; i += 16)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"  +{i:D2}  ");
            for (int j = 0; j < 16; j++)
            {
                if (i + j < dump) sb.Append($"{file[start + i + j]:X2} ");
                else              sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(" | ");
            for (int j = 0; j < 16 && i + j < dump; j++)
            {
                byte b = file[start + i + j];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            _out.WriteLine(sb.ToString());
        }

        uint boxSize = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(start));
        int soi = -1;
        for (int i = start + 8; i + 2 < start + size; i++)
            if (file[i] == 0xFF && file[i + 1] == 0xD8 && file[i + 2] == 0xFF) { soi = i; break; }
        if (soi >= 0)
        {
            int headerLen = soi - start;
            int jpegLen   = (start + size) - soi;
            _out.WriteLine($"  decoded: boxSize={boxSize}, JPEG-SOI at byte {headerLen} (header is {headerLen} bytes), JPEG payload={jpegLen} bytes");
        }
        _out.WriteLine("");
    }

    [Fact]
    public void Diagnose_DumpDppBoxStructure()
    {
        DumpStructure("BURST", File.ReadAllBytes(_fx.BurstPath));
        DumpStructure("DPP frame 1", _fx.DppFrame(1));
        DumpStructure("DPP frame 4", _fx.DppFrame(4));
        DumpStructure("OURS frame 1", _fx.OurFrame(1));
    }

    // -------------------------------------------------------------------
    // Byte-range diff diagnostics. Each test compares two CMT* extractions
    // and prints the contiguous ranges where they differ, along with the
    // first ~16 bytes of each side. Output reveals which TIFF tag offsets
    // DPP rewrites.
    // -------------------------------------------------------------------

    [Fact]
    public void Diagnose_Cmt2_BurstVsDpp()
    {
        DiffBytes("BURST vs DPP frame 1 :: CMT2",
            ExtractRawBoxBytesInMoovUuid(File.ReadAllBytes(_fx.BurstPath), "CMT2"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(1), "CMT2"));
    }

    [Fact]
    public void Diagnose_Cmt3_BurstVsDpp()
    {
        DiffBytes("BURST vs DPP frame 1 :: CMT3",
            ExtractRawBoxBytesInMoovUuid(File.ReadAllBytes(_fx.BurstPath), "CMT3"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(1), "CMT3"));
    }

    [Fact]
    public void Diagnose_Cmt3_DppFrame1VsDppFrame4()
    {
        DiffBytes("DPP frame 1 vs DPP frame 4 :: CMT3",
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(1), "CMT3"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(4), "CMT3"));
    }

    // -- Cross-burst diagnostics using the second burst 375A7575 (frames 6 and 10).
    //    If the CMT3/CMT2 diff offsets are identical between the two bursts,
    //    DPP's edit positions are stable -> we can patch by absolute offset.

    [Fact]
    public void Diagnose_Cmt3_DppFrame6VsDppFrame10_SecondaryBurst()
    {
        DiffBytes("(375A7575) DPP frame 6 vs DPP frame 10 :: CMT3",
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(ExtractionFixture.SecondaryBurst, 6),  "CMT3"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(ExtractionFixture.SecondaryBurst, 10), "CMT3"));
    }

    [Fact]
    public void Diagnose_Cmt2_BurstVsDpp_SecondaryBurst()
    {
        DiffBytes("(375A7575) BURST vs DPP frame 6 :: CMT2",
            ExtractRawBoxBytesInMoovUuid(File.ReadAllBytes(_fx.GetBurstPath(ExtractionFixture.SecondaryBurst)), "CMT2"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(ExtractionFixture.SecondaryBurst, 6), "CMT2"));
    }

    [Fact]
    public void Diagnose_Cmt3_BurstVsDpp_SecondaryBurst()
    {
        DiffBytes("(375A7575) BURST vs DPP frame 6 :: CMT3",
            ExtractRawBoxBytesInMoovUuid(File.ReadAllBytes(_fx.GetBurstPath(ExtractionFixture.SecondaryBurst)), "CMT3"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(ExtractionFixture.SecondaryBurst, 6), "CMT3"));
    }

    /// <summary>
    /// Summary diagnostic: for each diff between two byte arrays, report just
    /// the offsets where bytes change — no hex dump — so we can spot whether
    /// the edit offsets are stable across bursts.
    /// </summary>
    [Fact]
    public void Diagnose_Cmt2_OffsetSummary_BothBursts()
    {
        SummarizeOffsets("(375A4182) BURST -> DPP frame 1 :: CMT2",
            ExtractRawBoxBytesInMoovUuid(File.ReadAllBytes(_fx.BurstPath), "CMT2"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(1), "CMT2"));

        SummarizeOffsets("(375A7575) BURST -> DPP frame 6 :: CMT2",
            ExtractRawBoxBytesInMoovUuid(File.ReadAllBytes(_fx.GetBurstPath(ExtractionFixture.SecondaryBurst)), "CMT2"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(ExtractionFixture.SecondaryBurst, 6), "CMT2"));
    }

    [Fact]
    public void Diagnose_Cmt3_OffsetSummary_BothBursts()
    {
        SummarizeOffsets("(375A4182) DPP frame 1 -> DPP frame 4 :: CMT3",
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(1), "CMT3"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(4), "CMT3"));

        SummarizeOffsets("(375A7575) DPP frame 6 -> DPP frame 10 :: CMT3",
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(ExtractionFixture.SecondaryBurst, 6), "CMT3"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(ExtractionFixture.SecondaryBurst, 10), "CMT3"));

        SummarizeOffsets("(375A4182) BURST -> DPP frame 1 :: CMT3",
            ExtractRawBoxBytesInMoovUuid(File.ReadAllBytes(_fx.BurstPath), "CMT3"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(1), "CMT3"));

        SummarizeOffsets("(375A7575) BURST -> DPP frame 6 :: CMT3",
            ExtractRawBoxBytesInMoovUuid(File.ReadAllBytes(_fx.GetBurstPath(ExtractionFixture.SecondaryBurst)), "CMT3"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(ExtractionFixture.SecondaryBurst, 6), "CMT3"));
    }

    void SummarizeOffsets(string label, byte[]? a, byte[]? b)
    {
        if (a == null || b == null) { _out.WriteLine($"{label}: null"); return; }
        int len = Math.Min(a.Length, b.Length);
        var ranges = new List<(int Start, int End)>();
        int rs = -1;
        for (int i = 0; i < len; i++)
        {
            if (a[i] != b[i]) { if (rs < 0) rs = i; }
            else if (rs >= 0) { ranges.Add((rs, i)); rs = -1; }
        }
        if (rs >= 0) ranges.Add((rs, len));

        long totalDiffBytes = ranges.Sum(r => (long)(r.End - r.Start));
        _out.WriteLine($"{label}: {ranges.Count} diff ranges, {totalDiffBytes} bytes differ" +
                       (a.Length != b.Length ? $" (length: a={a.Length}, b={b.Length})" : ""));
        foreach (var r in ranges.Take(40))
            _out.WriteLine($"   @+{r.Start:D5}..{r.End - 1:D5} ({r.End - r.Start} bytes)");
        if (ranges.Count > 40)
            _out.WriteLine($"   ... +{ranges.Count - 40} more ranges");
        _out.WriteLine("");
    }

    void DiffBytes(string label, byte[]? a, byte[]? b, int maxRanges = 30)
    {
        _out.WriteLine($"=== {label} ===");
        if (a == null || b == null)
        {
            _out.WriteLine($"  cannot diff (a={(a == null ? "null" : a.Length + " bytes")}, " +
                           $"b={(b == null ? "null" : b.Length + " bytes")})");
            return;
        }
        _out.WriteLine($"  a={a.Length} bytes, b={b.Length} bytes" +
                       (a.Length != b.Length ? " (DIFFERENT LENGTHS)" : ""));

        int len = Math.Min(a.Length, b.Length);
        int rangeStart = -1;
        int rangesShown = 0;

        for (int i = 0; i < len; i++)
        {
            if (a[i] != b[i])
            {
                if (rangeStart < 0) rangeStart = i;
            }
            else if (rangeStart >= 0)
            {
                ShowDiffRange(a, b, rangeStart, i);
                rangeStart = -1;
                if (++rangesShown >= maxRanges)
                {
                    _out.WriteLine($"  ... truncated after {maxRanges} ranges");
                    return;
                }
            }
        }
        if (rangeStart >= 0) ShowDiffRange(a, b, rangeStart, len);
        if (rangesShown == 0 && rangeStart < 0)
            _out.WriteLine("  identical within shared length");
    }

    void ShowDiffRange(byte[] a, byte[] b, int start, int end)
    {
        int len = end - start;
        int show = Math.Min(len, 24);
        string aHex = string.Join(" ", Enumerable.Range(0, show).Select(i => a[start + i].ToString("X2")));
        string bHex = string.Join(" ", Enumerable.Range(0, show).Select(i => b[start + i].ToString("X2")));
        string suffix = len > show ? $" ... (+{len - show} more)" : "";
        _out.WriteLine($"  @+{start:D5}..{end - 1:D5} ({len,5} bytes)");
        _out.WriteLine($"     a: {aHex}{suffix}");
        _out.WriteLine($"     b: {bHex}{suffix}");
    }

    [Fact]
    public void Diagnose_TopLevelUuidIdentifiers()
    {
        DumpTopUuidIds("BURST",       File.ReadAllBytes(_fx.BurstPath));
        DumpTopUuidIds("DPP frame 1", _fx.DppFrame(1));
        DumpTopUuidIds("DPP frame 4", _fx.DppFrame(4));
        DumpTopUuidIds("OURS frame 1", _fx.OurFrame(1));
    }

    void DumpTopUuidIds(string label, byte[] file)
    {
        _out.WriteLine($"=== {label} top-level uuid identifiers ===");
        var top = BoxParser.ParseLevel(file, 0, file.Length);
        int idx = 0;
        foreach (var b in top.Where(b => b.Type == "uuid"))
        {
            string uuidHex = string.Join("-", Enumerable.Range(0, 16)
                .Select(i => file[b.RawOffset + 8 + i].ToString("x2"))
                .Select((s, i) => (i, s))
                .GroupBy(t => t.i < 4 ? 0 : t.i < 6 ? 1 : t.i < 8 ? 2 : t.i < 10 ? 3 : 4)
                .Select(g => string.Concat(g.Select(t => t.s))));
            string name = NameKnownUuid(file, b.RawOffset + 8);
            _out.WriteLine($"  #{idx++} @ [{b.RawOffset}, +{b.RawSize}]  UUID={uuidHex}  {name}");
        }
        _out.WriteLine("");
    }

    static string NameKnownUuid(byte[] file, int uuidOff)
    {
        // PRVW preview
        var prvw = new byte[] { 0xea,0xf4,0x2b,0x5e,0x1c,0x98,0x4b,0x88,0xb9,0xfb,0xb7,0xdc,0x40,0x6e,0x4d,0x16 };
        // XMP (ISO/MP4 standard)
        var xmp  = new byte[] { 0xbe,0x7a,0xcf,0xcb,0x97,0xa9,0x42,0xe8,0x9c,0x71,0x99,0x94,0x91,0xe3,0xaf,0xac };
        // Canon CR3 moov-internal metadata wrapper
        var canonMoov = new byte[] { 0x85,0xc0,0xb6,0x87,0x82,0x0f,0x11,0xe0,0x81,0x11,0xf4,0xce,0x46,0x2b,0x6a,0x48 };
        if (MatchUuid(file, uuidOff, prvw))      return "(PRVW preview)";
        if (MatchUuid(file, uuidOff, xmp))       return "(XMP)";
        if (MatchUuid(file, uuidOff, canonMoov)) return "(Canon moov-meta)";
        return "(unknown)";
    }

    static bool MatchUuid(byte[] file, int off, byte[] uuid)
    {
        for (int i = 0; i < 16; i++) if (file[off + i] != uuid[i]) return false;
        return true;
    }

    void DumpStructure(string label, byte[] file)
    {
        _out.WriteLine($"=== {label} ({file.Length:N0} bytes) ===");
        var top = BoxParser.ParseLevel(file, 0, file.Length);
        foreach (var b in top)
        {
            _out.WriteLine($"  TOP {b.Type}  [{b.RawOffset}, +{b.RawSize}]");
            if (b.Type == "moov")
            {
                foreach (var c in b.Children)
                {
                    _out.WriteLine($"    moov.{c.Type}  [{c.RawOffset}, +{c.RawSize}]");
                    if (c.Type == "uuid" && c.RawSize > 32)
                    {
                        // Re-parse the inside of the wrapper uuid: 8-byte box header,
                        // then 16-byte UUID, then nested boxes.
                        int innerStart = c.RawOffset + 8 + 16;
                        int innerEnd = c.RawOffset + c.RawSize;
                        var inner = BoxParser.ParseLevel(file, innerStart, innerEnd);
                        foreach (var ic in inner)
                            _out.WriteLine($"      moov.uuid.{ic.Type}  [{ic.RawOffset}, +{ic.RawSize}]");
                    }
                }
            }
        }
        _out.WriteLine("");
    }

    [Fact]
    public void Diagnose_WhatDppDoesWithEachBox()
    {
        Classify("THMB (in-moov small thumbnail)",
            ExtractThmbJpeg(_fx.DppFrame(1)),
            ExtractThmbJpeg(_fx.DppFrame(4)));

        Classify("PRVW (top-level uuid large preview)",
            ExtractPrvwJpeg(_fx.DppFrame(1)),
            ExtractPrvwJpeg(_fx.DppFrame(4)));

        Classify("CMT1 IFD1 thumbnail (EXIF thumbnail)",
            ExtractExifIfd1ThumbnailFromCmt1(_fx.DppFrame(1)),
            ExtractExifIfd1ThumbnailFromCmt1(_fx.DppFrame(4)));

        Classify("CMT2 (EXIF SubIFD raw bytes)",
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(1), "CMT2"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(4), "CMT2"));

        Classify("CMT3 (Canon MakerNote raw bytes)",
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(1), "CMT3"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(4), "CMT3"));

        Classify("CMT4 (GPS raw bytes)",
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(1), "CMT4"),
            ExtractRawBoxBytesInMoovUuid(_fx.DppFrame(4), "CMT4"));
    }

    void Classify(string name, byte[]? f1, byte[]? f4)
    {
        string verdict;
        if (f1 == null && f4 == null)
            verdict = "ABSENT (DPP omits this box in single-frame output)";
        else if (f1 == null || f4 == null)
            verdict = $"ASYMMETRIC (frame 1: {(f1 == null ? "absent" : f1.Length + " bytes")}, " +
                      $"frame 4: {(f4 == null ? "absent" : f4.Length + " bytes")})";
        else if (f1.SequenceEqual(f4))
            verdict = $"ROLL-LEVEL ({f1.Length} bytes, identical bytes across frames)";
        else
            verdict = $"PER-FRAME (frame 1: {f1.Length} bytes, frame 4: {f4.Length} bytes — bytes differ)";

        _out.WriteLine($"DPP {name}: {verdict}");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    static List<byte[]> ReadAllTrackFirstSamples(byte[] file)
    {
        var top = BoxParser.ParseLevel(file, 0, file.Length);
        var moov = top.FirstOrDefault(b => b.Type == "moov")
                   ?? throw new InvalidDataException("no moov");
        var samples = new List<byte[]>();
        foreach (var trak in moov.Children.Where(c => c.Type == "trak"))
        {
            var stbl = BoxQuery.GetStbl(trak);
            if (stbl == null) continue;
            var offsets = SampleTableReader.ReadCo64(file, stbl) ?? SampleTableReader.ReadStco(file, stbl);
            var sizes = SampleTableReader.ReadStsz(file, stbl);
            if (offsets == null || sizes == null || offsets.Count == 0 || sizes.Count == 0) continue;
            samples.Add(BoxQuery.ReadSlice(file, offsets[0], sizes[0]));
        }
        return samples;
    }

    static readonly byte[] PrvwUuid =
    {
        0xea, 0xf4, 0x2b, 0x5e, 0x1c, 0x98, 0x4b, 0x88,
        0xb9, 0xfb, 0xb7, 0xdc, 0x40, 0x6e, 0x4d, 0x16
    };

    static readonly byte[] XmpUuid =
    {
        0xbe, 0x7a, 0xcf, 0xcb, 0x97, 0xa9, 0x42, 0xe8,
        0x9c, 0x71, 0x99, 0x94, 0x91, 0xe3, 0xaf, 0xac
    };

    /// <summary>
    /// Returns the full raw bytes (including outer 8-byte box header + 16-byte UUID)
    /// of the top-level uuid box whose 16-byte identifier matches <paramref name="uuid"/>,
    /// or null if no such box exists.
    /// </summary>
    static byte[]? ExtractTopLevelUuidByIdentifier(byte[] file, byte[] uuid)
    {
        var top = BoxParser.ParseLevel(file, 0, file.Length);
        foreach (var b in top.Where(b => b.Type == "uuid"))
        {
            if (b.RawOffset + 8 + 16 > file.Length) continue;
            bool match = true;
            for (int i = 0; i < 16; i++)
                if (file[b.RawOffset + 8 + i] != uuid[i]) { match = false; break; }
            if (!match) continue;
            int size = Math.Min(b.RawSize, file.Length - b.RawOffset);
            return file.AsSpan(b.RawOffset, size).ToArray();
        }
        return null;
    }

    static byte[]? ExtractPrvwJpeg(byte[] file)
    {
        var top = BoxParser.ParseLevel(file, 0, file.Length);
        foreach (var box in top.Where(b => b.Type == "uuid"))
        {
            if (box.RawOffset + 8 + 16 > file.Length) continue;
            bool match = true;
            for (int i = 0; i < 16; i++)
                if (file[box.RawOffset + 8 + i] != PrvwUuid[i]) { match = false; break; }
            if (!match) continue;

            int jpegSizeOffset = box.RawOffset + 8 + 16 + 4 + 4 + 4 + 4 + 2 + 2 + 2 + 2;
            if (jpegSizeOffset + 4 > file.Length) return null;
            uint jpegSize = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(jpegSizeOffset));
            int jpegStart = jpegSizeOffset + 4;
            if (jpegStart + jpegSize > file.Length) return null;
            return file.AsSpan(jpegStart, (int)jpegSize).ToArray();
        }
        return null;
    }

    /// <summary>
    /// Canon CR3 wraps CMT1/CMT2/CMT3/CMT4/THMB/CNCV/CCTP/CTBO/CNOP inside a
    /// single `uuid` box at the start of moov. This helper returns the parsed
    /// children of that wrapper (or empty if absent).
    /// </summary>
    static List<Box> GetMoovUuidChildren(byte[] file)
    {
        var top = BoxParser.ParseLevel(file, 0, file.Length);
        var moov = top.FirstOrDefault(b => b.Type == "moov");
        if (moov == null) return new List<Box>();
        var wrapper = moov.Children.FirstOrDefault(c => c.Type == "uuid" && c.RawSize > 24);
        if (wrapper == null) return new List<Box>();
        int innerStart = wrapper.RawOffset + 8 + 16;
        int innerEnd = wrapper.RawOffset + wrapper.RawSize;
        return BoxParser.ParseLevel(file, innerStart, innerEnd);
    }

    static byte[]? ExtractThmbJpeg(byte[] file)
    {
        var thmb = GetMoovUuidChildren(file).FirstOrDefault(c => c.Type == "THMB");
        if (thmb == null) return null;

        int start = thmb.RawOffset;
        int end = thmb.RawOffset + thmb.RawSize;
        if (end > file.Length) end = file.Length;

        int soi = -1;
        for (int i = start + 8; i + 2 < end; i++)
        {
            if (file[i] == 0xFF && file[i + 1] == 0xD8 && file[i + 2] == 0xFF) { soi = i; break; }
        }
        if (soi < 0) return null;

        int eoi = -1;
        for (int j = end - 2; j > soi + 2; j--)
        {
            if (file[j] == 0xFF && file[j + 1] == 0xD9) { eoi = j + 2; break; }
        }
        if (eoi < 0) eoi = end;

        return file.AsSpan(soi, eoi - soi).ToArray();
    }

    static byte[]? ExtractExifIfd1ThumbnailFromCmt1(byte[] file)
    {
        var cmt1 = GetMoovUuidChildren(file).FirstOrDefault(c => c.Type == "CMT1");
        if (cmt1 == null) return null;

        int tiffStart = cmt1.RawOffset + 8;
        int tiffEnd = cmt1.RawOffset + cmt1.RawSize;
        if (tiffEnd > file.Length || tiffStart + 8 > tiffEnd) return null;

        bool le;
        if (file[tiffStart] == (byte)'I' && file[tiffStart + 1] == (byte)'I') le = true;
        else if (file[tiffStart] == (byte)'M' && file[tiffStart + 1] == (byte)'M') le = false;
        else return null;

        uint ifd0Rel = ReadU32(file, tiffStart + 4, le);
        int ifd0Start = tiffStart + (int)ifd0Rel;
        if (ifd0Start + 2 > tiffEnd) return null;
        ushort ifd0Count = ReadU16(file, ifd0Start, le);
        int nextIfdPtr = ifd0Start + 2 + 12 * ifd0Count;
        if (nextIfdPtr + 4 > tiffEnd) return null;
        uint ifd1Rel = ReadU32(file, nextIfdPtr, le);
        if (ifd1Rel == 0) return null;

        int ifd1Start = tiffStart + (int)ifd1Rel;
        if (ifd1Start + 2 > tiffEnd) return null;
        ushort ifd1Count = ReadU16(file, ifd1Start, le);

        uint? jpegRelOffset = null;
        uint? jpegLength = null;
        for (int i = 0; i < ifd1Count; i++)
        {
            int entry = ifd1Start + 2 + i * 12;
            if (entry + 12 > tiffEnd) break;
            ushort tag = ReadU16(file, entry, le);
            uint value = ReadU32(file, entry + 8, le);
            if (tag == 0x0201) jpegRelOffset = value;
            else if (tag == 0x0202) jpegLength = value;
        }
        if (jpegRelOffset == null || jpegLength == null) return null;
        int jpegStart = tiffStart + (int)jpegRelOffset.Value;
        int jpegSize = (int)jpegLength.Value;
        if (jpegStart < 0 || jpegStart + jpegSize > tiffEnd) return null;
        return file.AsSpan(jpegStart, jpegSize).ToArray();
    }

    static byte[]? ExtractRawBoxBytesInMoovUuid(byte[] file, string boxType)
    {
        var box = GetMoovUuidChildren(file).FirstOrDefault(c => c.Type == boxType);
        if (box == null) return null;
        int size = box.RawSize;
        if (box.RawOffset + size > file.Length) size = file.Length - box.RawOffset;
        return file.AsSpan(box.RawOffset, size).ToArray();
    }

    static ushort ReadU16(byte[] buf, int off, bool le) =>
        le ? BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(off))
           : BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(off));

    static uint ReadU32(byte[] buf, int off, bool le) =>
        le ? BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(off))
           : BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(off));
}
