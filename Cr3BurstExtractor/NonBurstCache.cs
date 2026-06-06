using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Cr3BurstExtractor;

/// <summary>
/// Persists the set of .CR3 files we've already inspected and confirmed are NOT bursts
/// (i.e. contain exactly one frame). Subsequent scans can skip these without re-reading
/// the file and re-parsing its box tree.
///
/// Cache key is the absolute file path. The entry stores file size + last-write time
/// (UTC ticks) so we can detect if a file has changed since the last check — a changed
/// file is treated as a cache miss and re-inspected from scratch.
///
/// All IO is best-effort: cache failures never block the scan.
/// </summary>
internal static class NonBurstCache
{
    static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cr3BurstExtractor");

    static string CacheFile => Path.Combine(CacheDir, "non_burst_cache.json");

    sealed class Entry
    {
        public long Size { get; set; }
        public long Mtime { get; set; }
    }

    static readonly Dictionary<string, Entry> _entries = Load();

    static Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(CacheFile))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(CacheFile));
                if (dict != null) return new Dictionary<string, Entry>(dict, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { /* corrupt cache file — start fresh */ }
        return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if <paramref name="path"/> is in the cache AND its size + mtime
    /// still match — meaning we're sure the file is single-frame without touching it.
    /// </summary>
    public static bool IsKnownNonBurst(string path, FileInfo info)
    {
        if (!_entries.TryGetValue(NormalizePath(path), out var entry)) return false;
        return entry.Size == info.Length
            && entry.Mtime == info.LastWriteTimeUtc.Ticks;
    }

    public static void MarkNonBurst(string path, FileInfo info)
    {
        _entries[NormalizePath(path)] = new Entry
        {
            Size = info.Length,
            Mtime = info.LastWriteTimeUtc.Ticks
        };
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(
                CacheFile,
                JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch { /* best-effort */ }
    }

    public static int Count => _entries.Count;

    static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
