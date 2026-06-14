using System.Buffers.Binary;
using System.Text;
using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor.Managers;

/// <summary>
/// PRVW preview UUID (eaf42b5e-1c98-4b88-b9fb-b7dc406e4d16).  The roll has a
/// single roll-level preview; this class rebuilds it per-frame so each extracted
/// CR3 shows its own embedded JPEG instead of frame 0's.
/// </summary>
public static class PrvwBuilder
{
    public static readonly byte[] PrvwUuid =
    {
        0xea, 0xf4, 0x2b, 0x5e, 0x1c, 0x98, 0x4b, 0x88,
        0xb9, 0xfb, 0xb7, 0xdc, 0x40, 0x6e, 0x4d, 0x16
    };

    public static bool IsPrvwUuid(byte[] src, Box uuid)
    {
        if (uuid.Type != "uuid" || uuid.RawSize < 8 + 16) return false;
        for (int i = 0; i < 16; i++)
            if (src[uuid.RawOffset + 8 + i] != PrvwUuid[i])
                return false;
        return true;
    }

    // Find the first sample that looks like a JPEG (SOI marker).  In Canon CR3
    // trak 1 is the embedded JPEG preview; we identify it by its bytes rather
    // than by trak index so this is robust to schemas that reorder the tracks.
    public static byte[]? FindJpegSample(List<(byte[] Data, int TrakIdx)> samples)
    {
        foreach (var (data, _) in samples)
        {
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return data;
        }

        return null;
    }

    // Build a PRVW uuid wrapping a new JPEG payload.  Layout (per lclevy/canon_cr3):
    //   8  outer size + 'uuid'
    //   16 UUID
    //   4  zero
    //   4  PRVW box size
    //   4  'PRVW'
    //   4  version/flags (0)
    //   2  zero
    //   2  width
    //   2  height
    //   2  one
    //   4  jpegSize
    //   .. jpeg bytes
    // Width/height are carried over from the original — they describe the JPEG's
    // pixel dimensions, which are the same for every frame in a burst.
    public static byte[] BuildWithJpeg(byte[] origUuidBytes, byte[] newJpeg)
    {
        const int widthOff = 8 + 16 + 4 + 4 + 4 + 4 + 2; // = 42
        ushort width = BinaryPrimitives.ReadUInt16BigEndian(origUuidBytes.AsSpan(widthOff));
        ushort height = BinaryPrimitives.ReadUInt16BigEndian(origUuidBytes.AsSpan(widthOff + 2));

        uint jpegSize = (uint)newJpeg.Length;
        uint prvwBoxSize = 24 + jpegSize; // 'PRVW' box: 4+4+4+2+2+2+2+4 + jpeg
        uint outerSize = 8 + 16 + 4 + prvwBoxSize; // outer uuid box

        using var ms = new MemoryStream();
        BinaryHelpers.WriteUInt32BE(ms, outerSize);
        ms.Write(Encoding.ASCII.GetBytes("uuid"));
        ms.Write(PrvwUuid);
        BinaryHelpers.WriteUInt32BE(ms, 0); // 4 zero before PRVW box
        BinaryHelpers.WriteUInt32BE(ms, prvwBoxSize);
        ms.Write(Encoding.ASCII.GetBytes("PRVW"));
        BinaryHelpers.WriteUInt32BE(ms, 0); // version/flags
        BinaryHelpers.WriteUInt16BE(ms, 0);
        BinaryHelpers.WriteUInt16BE(ms, width);
        BinaryHelpers.WriteUInt16BE(ms, height);
        BinaryHelpers.WriteUInt16BE(ms, 1);
        BinaryHelpers.WriteUInt32BE(ms, jpegSize);
        ms.Write(newJpeg);
        return ms.ToArray();
    }
}