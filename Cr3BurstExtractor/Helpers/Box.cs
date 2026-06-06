namespace Cr3BurstExtractor.Helpers;

/// <summary>
/// One ISOBMFF box parsed from the source file.  RawOffset/RawSize describe the
/// byte range in the original file; Children is populated for container boxes.
/// </summary>
public record Box(string Type, int RawOffset, int RawSize, List<Box> Children)
{
    // DataOffset = first byte after the 8-byte (or 16-byte extended) header
    public int DataOffset => RawOffset + HeaderSize();

    public int DataSize => RawSize - HeaderSize();

    int HeaderSize()
    {
        // Extended size boxes have an extra 8-byte size field
        // We detect by checking if size32 == 1 stored at RawOffset
        // (caller would have to pass src, but we can't here)
        // In practice Canon CR3 never uses extended headers for containers,
        // so this is always 8.
        return 8;
    }
}