using System.Buffers.Binary;
using System.Text;

namespace Cr3BurstExtractor.Helpers;

/// <summary>
/// ISOBMFF box parser — stores raw byte range from src so callers can re-emit
/// any box verbatim later.
/// </summary>
public static class BoxParser
{
    public static List<Box> ParseLevel(byte[] src, int start, int limit)
    {
        var boxes = new List<Box>();
        int pos = start;
        while (pos + 8 <= limit)
        {
            uint size32 = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(pos));
            string type = Encoding.ASCII.GetString(src, pos + 4, 4);

            int boxSize;
            if (size32 == 1)
            {
                // Extended 64-bit size
                ulong s64 = BinaryPrimitives.ReadUInt64BigEndian(src.AsSpan(pos + 8));
                boxSize = (int)Math.Min(s64, (ulong)(limit - pos));
            }
            else if (size32 == 0)
            {
                boxSize = limit - pos;
            }
            else
            {
                boxSize = (int)size32;
            }

            if (boxSize < 8 || pos + boxSize > limit) break;

            var children = new List<Box>();
            if (IsContainer(type))
            {
                int childStart = pos + 8;
                // uuid boxes: skip the 16-byte uuid value before children
                if (type == "uuid") childStart += 16;
                children = ParseLevel(src, childStart, pos + boxSize);
            }

            boxes.Add(new Box(type, pos, boxSize, children));
            pos += boxSize;
        }

        return boxes;
    }

    static bool IsContainer(string t) => t is
        "moov" or "trak" or "mdia" or "minf" or "stbl" or "dinf" or
        "edts" or "udta" or "meta" or "mvex" or "traf" or "moof";
    // Note: uuid is handled specially above (skip 16 bytes), but we do descend into it.
}