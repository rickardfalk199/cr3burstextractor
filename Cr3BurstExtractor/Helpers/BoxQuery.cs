namespace Cr3BurstExtractor.Helpers;

/// <summary>
/// Helpers for navigating a parsed box tree and extracting raw byte ranges.
/// All methods are pure functions over an already-parsed tree.
/// </summary>
public static class BoxQuery
{
    public static Box? FindFirst(List<Box> boxes, Func<Box, bool> pred)
    {
        foreach (var b in boxes)
        {
            if (pred(b)) return b;
            var r = FindFirst(b.Children, pred);
            if (r != null) return r;
        }

        return null;
    }

    public static IEnumerable<Box> FindAll(List<Box> boxes, Func<Box, bool> pred)
    {
        foreach (var b in boxes)
        {
            if (pred(b)) yield return b;
            foreach (var c in FindAll(b.Children, pred)) yield return c;
        }
    }

    public static Box? GetStbl(Box trak) =>
        FindFirst(trak.Children, b => b.Type == "mdia")
            ?.Children.Find(b => b.Type == "minf")
            ?.Children.Find(b => b.Type == "stbl");

    public static List<(long Offset, long Size)> CollectMdat(List<Box> boxes)
    {
        var result = new List<(long, long)>();
        foreach (var b in boxes)
        {
            if (b.Type == "mdat") result.Add((b.RawOffset + 8, b.RawSize - 8));
            else result.AddRange(CollectMdat(b.Children));
        }

        return result;
    }

    /// <summary>Returns the exact raw bytes of this box (header + payload).</summary>
    public static byte[] GetRawBox(byte[] src, Box b)
        => src.AsSpan(b.RawOffset, b.RawSize).ToArray();

    public static byte[] ReadSlice(byte[] src, long offset, long size)
        => src.AsSpan((int)offset, (int)size).ToArray();
}