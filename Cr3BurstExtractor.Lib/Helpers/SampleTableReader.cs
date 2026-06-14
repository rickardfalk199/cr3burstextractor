using System.Buffers.Binary;

namespace Cr3BurstExtractor.Helpers;

/// <summary>
/// Readers for the sample-table boxes inside stbl, plus the timescale/duration
/// fields shared by mvhd / mdhd.
/// </summary>
public static class SampleTableReader
{
    public static List<long>? ReadCo64(byte[] src, Box stbl)
    {
        var box = stbl.Children.Find(b => b.Type == "co64");
        if (box == null) return null;
        int p = box.RawOffset + 8 + 4; // skip header + version/flags
        uint count = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p));
        p += 4;
        var list = new List<long>((int)count);
        for (int i = 0; i < count; i++, p += 8)
            list.Add((long)BinaryPrimitives.ReadUInt64BigEndian(src.AsSpan(p)));
        return list;
    }

    public static List<long>? ReadStco(byte[] src, Box stbl)
    {
        var box = stbl.Children.Find(b => b.Type == "stco");
        if (box == null) return null;
        int p = box.RawOffset + 8 + 4;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p));
        p += 4;
        var list = new List<long>((int)count);
        for (int i = 0; i < count; i++, p += 4)
            list.Add(BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)));
        return list;
    }

    public static List<long>? ReadStsz(byte[] src, Box stbl)
    {
        var box = stbl.Children.Find(b => b.Type == "stsz");
        if (box == null) return null;
        int p = box.RawOffset + 8 + 4; // skip version/flags
        uint fixedSize = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p));
        p += 4;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p));
        p += 4;
        var list = new List<long>((int)count);
        for (int i = 0; i < count; i++, p += 4)
            list.Add(fixedSize > 0 ? fixedSize : BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(p)));
        return list;
    }

    // mvhd and mdhd share the same field layout up to the timescale.
    // v0:  ver/flags(4) cre(4) mod(4) timescale(4) duration(4)
    // v1:  ver/flags(4) cre(8) mod(8) timescale(4) duration(8)
    public static uint ReadMvhdMdhdTimescale(byte[] src, Box box)
    {
        int d = box.RawOffset + 8; // start of full-box payload
        byte version = src[d];
        int tsOff = version == 1 ? d + 4 + 8 + 8 : d + 4 + 4 + 4;
        return BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(tsOff));
    }

    public static uint ReadFirstSttsDelta(byte[] src, Box stbl)
    {
        var box = stbl.Children.Find(b => b.Type == "stts");
        if (box == null) return 0;
        // header(8) ver/flags(4) entry_count(4) [sample_count(4) sample_delta(4)]...
        uint count = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(box.RawOffset + 12));
        if (count == 0) return 0;
        return BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(box.RawOffset + 20));
    }
}