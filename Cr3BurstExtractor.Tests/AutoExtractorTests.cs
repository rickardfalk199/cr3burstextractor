using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Cr3BurstExtractor.Tests;

/// <summary>
/// Verifies the per-file orchestrator <see cref="AutoExtractor"/> matches the
/// observable behavior of the old per-file body inside
/// MainForm.ProcessDirectory: cache-check → classify → extract → optionally
/// move original. Each test isolates the static NonBurstCache singleton by
/// pointing SharedPaths at a fresh temp directory via CR3BURST_DATA_DIR.
/// </summary>
public class AutoExtractorTests : IClassFixture<ExtractionFixture>, IDisposable
{
    readonly ExtractionFixture _fix;
    readonly string _dataDir;
    readonly string? _previousDataDir;

    public AutoExtractorTests(ExtractionFixture fix)
    {
        _fix = fix;
        _dataDir = Path.Combine(Path.GetTempPath(),
            "Cr3BurstExtractorTests_data_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        _previousDataDir = Environment.GetEnvironmentVariable("CR3BURST_DATA_DIR");
        Environment.SetEnvironmentVariable("CR3BURST_DATA_DIR", _dataDir);
        // Force the static cache to re-read with the new path.
        NonBurstCache.Reload();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CR3BURST_DATA_DIR", _previousDataDir);
        NonBurstCache.Reload();
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* leave on disk if locked */ }
    }

    [Fact]
    public void ProcessFile_BurstInput_ReturnsExtractedWithExpectedFrames()
    {
        string workDir = Path.Combine(_dataDir, "burst");
        Directory.CreateDirectory(workDir);
        string burstCopy = Path.Combine(workDir, Path.GetFileName(_fix.BurstPath));
        File.Copy(_fix.BurstPath, burstCopy);

        var result = AutoExtractor.ProcessFile(burstCopy, moveOriginals: false, backupFolder: null, log: null);

        Assert.Equal(AutoExtractOutcome.Extracted, result.Outcome);
        Assert.True(result.FrameCount > 1);
        Assert.NotNull(result.OutputDir);
        Assert.True(Directory.Exists(result.OutputDir));
        var extracted = Directory.EnumerateFiles(result.OutputDir!, "*.CR3").ToList();
        Assert.Equal(result.FrameCount, extracted.Count);
        // moveOriginals=false leaves the source in place
        Assert.True(File.Exists(burstCopy));
    }

    [Fact]
    public void ProcessFile_BurstInput_WithMoveOriginals_MovesSourceToBackup()
    {
        string workDir = Path.Combine(_dataDir, "moveorig");
        Directory.CreateDirectory(workDir);
        string burstCopy = Path.Combine(workDir, Path.GetFileName(_fix.BurstPath));
        File.Copy(_fix.BurstPath, burstCopy);

        string backupDir = Path.Combine(_dataDir, "backup");

        var result = AutoExtractor.ProcessFile(burstCopy, moveOriginals: true, backupFolder: backupDir, log: null);

        Assert.Equal(AutoExtractOutcome.Extracted, result.Outcome);
        Assert.False(File.Exists(burstCopy));
        Assert.NotNull(result.MovedTo);
        Assert.True(File.Exists(result.MovedTo));
        Assert.Equal(backupDir, Path.GetDirectoryName(result.MovedTo));
    }

    [Fact]
    public void ProcessFile_SingleFrameInput_FirstReturnsSkippedThenCached()
    {
        // The DPP reference frames in TestBurst/ are single-frame CR3s.
        string singleFrameSource = Path.Combine(_fix.TestDir, $"{ExtractionFixture.PrimaryBurst}_01.CR3");
        Assert.True(File.Exists(singleFrameSource), $"Missing test asset: {singleFrameSource}");

        string workDir = Path.Combine(_dataDir, "single");
        Directory.CreateDirectory(workDir);
        string copy = Path.Combine(workDir, Path.GetFileName(singleFrameSource));
        File.Copy(singleFrameSource, copy);

        var first = AutoExtractor.ProcessFile(copy, moveOriginals: false, backupFolder: null, log: null);
        Assert.Equal(AutoExtractOutcome.SkippedNonBurst, first.Outcome);
        Assert.Equal(1, first.FrameCount);

        var second = AutoExtractor.ProcessFile(copy, moveOriginals: false, backupFolder: null, log: null);
        Assert.Equal(AutoExtractOutcome.Cached, second.Outcome);
    }
}
