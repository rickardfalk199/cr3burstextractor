using System.Text;

namespace Cr3BurstExtractor.Helpers;

/// <summary>
/// Box serialisers for the small fixed-shape boxes we emit when rebuilding a
/// single-frame sample table: ftyp, stsz, stts, stsc, co64, stco.
/// </summary>
public static class SampleTableWriter
{
    public static void WriteFtyp(Stream s)
    {
        // 4 size + 4 "ftyp" + 4 brand + 4 version + 4 compat1 + 4 compat2 = 24
        BinaryHelpers.WriteUInt32BE(s, 24);
        s.Write(Encoding.ASCII.GetBytes("ftyp"));
        s.Write(Encoding.ASCII.GetBytes("crx "));
        BinaryHelpers.WriteUInt32BE(s, 1);
        s.Write(Encoding.ASCII.GetBytes("crx "));
        s.Write(Encoding.ASCII.GetBytes("isom"));
    }

    public static void WriteStsz(Stream s, uint sampleSize)
    {
        // Fixed-size form, matching Canon DPP: a single sample whose size is carried in
        // the sample_size field (no per-entry array).
        // Box: size(4) + "stsz"(4) + version/flags(4) + sampleSize(4) + count(4) = 20
        BinaryHelpers.WriteUInt32BE(s, 20);
        s.Write(Encoding.ASCII.GetBytes("stsz"));
        BinaryHelpers.WriteUInt32BE(s, 0); // version+flags
        BinaryHelpers.WriteUInt32BE(s, sampleSize); // sample_size: all samples are this size
        BinaryHelpers.WriteUInt32BE(s, 1); // sample count = 1
    }

    public static void WriteStts(Stream s, uint sampleDelta)
    {
        // 1 entry: sample_count=1, sample_delta = one frame's duration
        BinaryHelpers.WriteUInt32BE(s, 24);
        s.Write(Encoding.ASCII.GetBytes("stts"));
        BinaryHelpers.WriteUInt32BE(s, 0); // version+flags
        BinaryHelpers.WriteUInt32BE(s, 1); // entry count
        BinaryHelpers.WriteUInt32BE(s, 1); // sample count
        BinaryHelpers.WriteUInt32BE(s, sampleDelta); // sample delta
    }

    public static void WriteStsc(Stream s)
    {
        // 1 entry: first_chunk=1, samples_per_chunk=1, sample_desc_index=1
        BinaryHelpers.WriteUInt32BE(s, 28);
        s.Write(Encoding.ASCII.GetBytes("stsc"));
        BinaryHelpers.WriteUInt32BE(s, 0); // version+flags
        BinaryHelpers.WriteUInt32BE(s, 1); // entry count
        BinaryHelpers.WriteUInt32BE(s, 1); // first chunk
        BinaryHelpers.WriteUInt32BE(s, 1); // samples per chunk
        BinaryHelpers.WriteUInt32BE(s, 1); // sample description index
    }

    public static void WriteCo64(Stream s, ulong offset)
    {
        // size(4) + "co64"(4) + version/flags(4) + count(4) + offset(8) = 24
        BinaryHelpers.WriteUInt32BE(s, 24);
        s.Write(Encoding.ASCII.GetBytes("co64"));
        BinaryHelpers.WriteUInt32BE(s, 0); // version+flags
        BinaryHelpers.WriteUInt32BE(s, 1); // entry count
        BinaryHelpers.WriteUInt64BE(s, offset);
    }

    public static void WriteStco(Stream s, uint offset)
    {
        // size(4) + "stco"(4) + version/flags(4) + count(4) + offset(4) = 20
        BinaryHelpers.WriteUInt32BE(s, 20);
        s.Write(Encoding.ASCII.GetBytes("stco"));
        BinaryHelpers.WriteUInt32BE(s, 0); // version+flags
        BinaryHelpers.WriteUInt32BE(s, 1); // entry count
        BinaryHelpers.WriteUInt32BE(s, offset);
    }
}