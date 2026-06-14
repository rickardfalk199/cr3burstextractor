using System.Buffers.Binary;

namespace Cr3BurstExtractor.Helpers;

public static class BinaryHelpers
{
    public static void WriteUInt16BE(Stream s, ushort v)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buf, v);
        s.Write(buf);
    }

    public static void WriteUInt32BE(Stream s, uint v)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, v);
        s.Write(buf);
    }

    public static void WriteUInt64BE(Stream s, ulong v)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buf, v);
        s.Write(buf);
    }
}