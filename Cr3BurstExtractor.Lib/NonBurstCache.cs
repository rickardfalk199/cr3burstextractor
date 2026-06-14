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
/// Stored in <c>%ProgramData%\Cr3BurstExtractor\non_burst_cache.json</c> so the
/// interactive tool and the Windows Service share the same cache; see
/// <see cref="SharedPaths"/>. Writes are atomic (tmp + rename) so concurrent
/// writers from both processes don't tear the file.
///
/// All IO is best-effort: cache failures never block the scan.
/// </summary>
public static class NonBurstCache
{
    sealed class Entry
    {
        public long Size { get; set; }
        public long Mtime { get; set; }
    }

    static Dictionary<string, Entry> _entries = Load();

    static Dictionary<string, Entry> Load()
    {
        try
        {
            SharedPaths.MigrateLegacyIfNeeded("non_burst_cache.json");

            if (File.Exists(SharedPaths.CacheFile))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, Entry>>(
                    File.ReadAllText(SharedPaths.CacheFile));
                if (dict != null) return new Dictionary<string, Entry>(dict, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { /* corrupt cache file — start fresh */ }
        return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Re-reads the cache from disk and replaces the in-memory snapshot.
    /// The service calls this before each per-file run so writes the form
    /// just made are visible (and vice versa).
    /// </summary>
    public static void Reload() => _entries = Load();

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
            SharedPaths.EnsureDir();
            string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = false });
            SharedPaths.AtomicWriteAllText(SharedPaths.CacheFile, json);
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
