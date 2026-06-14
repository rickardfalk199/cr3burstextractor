using System.IO;
using Cr3BurstExtractor.Managers;
using Xunit;

namespace Cr3BurstExtractor.Tests;

/// <summary>
/// Exercises the stream-based <see cref="BurstReader"/> API: opening from a
/// <see cref="Stream"/>, querying <see cref="BurstReader.FrameCount"/>, and
/// writing a single frame to an arbitrary <see cref="Stream"/>. The
/// integration-style "DPP matches" coverage lives in <see cref="ExtractionTests"/>;
/// these tests are about the public API shape.
/// </summary>
public class BurstReaderTests : IClassFixture<ExtractionFixture>
{
    readonly ExtractionFixture _fix;

    public BurstReaderTests(ExtractionFixture fix) { _fix = fix; }

    [Fact]
    public void Open_FromFileStream_ReportsExpectedFrameCount()
    {
        using var fs = File.OpenRead(_fix.BurstPath);
        using var reader = BurstReader.Open(fs);
        Assert.True(reader.FrameCount > 1,
            $"Expected primary burst to contain multiple frames; got {reader.FrameCount}.");
    }

    [Fact]
    public void Open_FromMemoryStream_ReportsSameFrameCountAsFileStream()
    {
        byte[] bytes = File.ReadAllBytes(_fix.BurstPath);

        int viaFile;
        using (var fs = File.OpenRead(_fix.BurstPath))
        using (var r = BurstReader.Open(fs))
            viaFile = r.FrameCount;

        using var ms = new MemoryStream(bytes, writable: false);
        using var reader = BurstReader.Open(ms);
        Assert.Equal(viaFile, reader.FrameCount);
    }

    [Fact]
    public void ExtractFrame_ToMemoryStream_ProducesParseableSingleFrameCr3()
    {
        using var fs = File.OpenRead(_fix.BurstPath);
        using var reader = BurstReader.Open(fs);

        using var output = new MemoryStream();
        reader.ExtractFrame(0, output);
        output.Position = 0;

        // Round-trip: the extracted bytes should themselves parse as a
        // single-frame CR3. This proves the stream API produces a
        // self-contained file, not just a "slice" that relies on the
        // original file at known offsets.
        using var roundTrip = BurstReader.Open(output);
        Assert.Equal(1, roundTrip.FrameCount);
    }

    [Fact]
    public void ExtractFrame_RejectsIndexOutOfRange()
    {
        using var fs = File.OpenRead(_fix.BurstPath);
        using var reader = BurstReader.Open(fs);

        using var sink = new MemoryStream();
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => reader.ExtractFrame(reader.FrameCount, sink));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => reader.ExtractFrame(-1, sink));
    }
}
