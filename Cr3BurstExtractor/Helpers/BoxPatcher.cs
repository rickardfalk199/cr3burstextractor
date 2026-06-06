using System.Buffers.Binary;
using System.Text;

namespace Cr3BurstExtractor.Helpers;

public enum DurField
{
    Mvhd,
    Tkhd,
    Mdhd
}

/// <summary>
/// In-place patches applied to already-serialised moov bytes:
///   - CTBO: re-point top-level box offsets after the new file layout is known
///   - CCTP: switch the roll's image-collection flag from "roll" to "single image"
///   - co64/stco: rewrite chunk offsets to point at the new mdat
///   - duration: rewrite the duration field inside an mvhd/tkhd/mdhd copy
/// </summary>
public static class BoxPatcher
{
    // ----------------------------------------------------------
    // Re-point the CTBO table (inside the moov 85c0b687 uuid) to the new layout.
    // Each 20-byte record is (index:u32, offset:u64, size:u64) and stores the
    // ORIGINAL file offset of a top-level box.  We rewrite any record whose offset
    // matches a box we carried over; index==4 (offset/size == 0) is left untouched.
    // ----------------------------------------------------------
    public static void PatchCtbo(byte[] moovBytes, Dictionary<long, (long Off, long Size)> offsetMap)
    {
        for (int p = 0; p + 8 <= moovBytes.Length; p++)
        {
            if (moovBytes[p + 4] != (byte)'C' || moovBytes[p + 5] != (byte)'T' ||
                moovBytes[p + 6] != (byte)'B' || moovBytes[p + 7] != (byte)'O')
                continue;

            int dataStart = p + 8;
            if (dataStart + 4 > moovBytes.Length) return;
            uint count = BinaryPrimitives.ReadUInt32BigEndian(moovBytes.AsSpan(dataStart));
            int rec = dataStart + 4;

            for (int i = 0; i < count; i++)
            {
                int rp = rec + i * 20;
                if (rp + 20 > moovBytes.Length) break;

                long oldOff = (long)BinaryPrimitives.ReadUInt64BigEndian(moovBytes.AsSpan(rp + 4));
                if (offsetMap.TryGetValue(oldOff, out var nv))
                {
                    BinaryPrimitives.WriteUInt64BigEndian(moovBytes.AsSpan(rp + 4), (ulong)nv.Off);
                    BinaryPrimitives.WriteUInt64BigEndian(moovBytes.AsSpan(rp + 12), (ulong)nv.Size);
                }
            }

            return; // only one CTBO
        }
    }

    // ----------------------------------------------------------
    // CCTP (inside moov's 85c0b687 uuid) describes the image tracks.  Its layout is
    //   size(4) 'CCTP' version(4) flag(4) ccdtCount(4) [CCDT ...]
    // The `flag` field is 2 in a raw-burst roll and 1 in a single still — Canon DPP
    // sets it to 1 when it extracts a frame.  We do the same so Adobe Camera Raw /
    // Lightroom treat the file as a normal single image (the roll value is rejected
    // with the generic "unsupported file" error).
    //
    // NOTE: CNCV is deliberately left untouched — Canon's own extracted single-frame
    // files keep the roll's CNCV (".../01.00.00"), so it is not the gate.
    // ----------------------------------------------------------
    public static void PatchCctp(byte[] moovBytes)
    {
        for (int p = 0; p + 12 <= moovBytes.Length; p++)
        {
            if (moovBytes[p] != (byte)'C' || moovBytes[p + 1] != (byte)'C' ||
                moovBytes[p + 2] != (byte)'T' || moovBytes[p + 3] != (byte)'P')
                continue;

            // p points at 'CCTP'; version at p+4, flag field at p+8
            BinaryPrimitives.WriteUInt32BigEndian(moovBytes.AsSpan(p + 8), 1u);
            return; // only one CCTP
        }
    }

    public static void PatchOffsets(
        byte[] moovBytes,
        List<(byte[] Data, int TrakIdx)> frameSamples,
        long mdatPayloadOffset)
    {
        // Reset the per-call sample index. _patchCursor is static, so without this
        // it stays at frameSamples.Count after the first frame, causing
        // NextSampleSizeForCursor to return 0 forever — every subsequent frame's
        // co64/stco offsets then collapse onto mdatPayloadOffset (the JPEG), which
        // makes the preview decode but the CRAW data unreadable.
        _patchCursor = 0;

        // Accumulate the write position within mdat for each track's sample
        // Tracks appear in the same order they were written
        long cursor = mdatPayloadOffset;

        PatchOffsetBoxes(moovBytes, 8, moovBytes.Length, ref cursor, frameSamples);
    }

    static void PatchOffsetBoxes(
        byte[] buf,
        int start,
        int limit,
        ref long cursor,
        List<(byte[] Data, int TrakIdx)> frameSamples)
    {
        int pos = start;
        while (pos + 8 <= limit)
        {
            uint size32 = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(pos));
            string type = Encoding.ASCII.GetString(buf, pos + 4, 4);
            int boxSize = (int)size32;
            if (boxSize < 8 || pos + boxSize > limit) break;

            if (type is "moov" or "trak" or "mdia" or "minf" or "stbl")
            {
                PatchOffsetBoxes(buf, pos + 8, pos + boxSize, ref cursor, frameSamples);
            }
            else if (type == "co64")
            {
                // version(1)+flags(3)+count(4) = 8 bytes, then 8-byte offsets
                int dataStart = pos + 8; // skip box header
                // version+flags = 4 bytes, count = 4 bytes, then offset at +8
                BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(dataStart + 8), (ulong)cursor);
                cursor += NextSampleSizeForCursor(buf, pos, frameSamples);
            }
            else if (type == "stco")
            {
                int dataStart = pos + 8;
                BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(dataStart + 8), (uint)cursor);
                cursor += NextSampleSizeForCursor(buf, pos, frameSamples);
            }

            pos += boxSize;
        }
    }

    static int _patchCursor = 0;

    static long NextSampleSizeForCursor(byte[] _, int __, List<(byte[] Data, int TrakIdx)> samples)
    {
        // Called once per co64/stco box in track order
        if (_patchCursor >= samples.Count) return 0;
        long sz = samples[_patchCursor].Data.Length;
        _patchCursor++;
        return sz;
    }

    // ----------------------------------------------------------
    // Overwrite the duration field (32- or 64-bit depending on version) in a verbatim
    // copy of an mvhd / tkhd / mdhd box, returning the same-length byte array.
    // ----------------------------------------------------------
    public static byte[] PatchDuration(byte[] box, DurField kind, uint newDuration)
    {
        byte version = box[8]; // full-box version byte
        int d = 8 + 4; // skip box header(8) + ver/flags(4)
        int durOff;
        bool wide = version == 1;

        switch (kind)
        {
            case DurField.Mvhd: // cre,mod,timescale,duration
            case DurField.Mdhd:
                durOff = wide ? d + 8 + 8 + 4 : d + 4 + 4 + 4;
                break;
            default: // Tkhd: cre,mod,track_id,reserved,duration
                durOff = wide ? d + 8 + 8 + 4 + 4 : d + 4 + 4 + 4 + 4;
                break;
        }

        if (wide)
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(durOff), newDuration);
        else
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(durOff), newDuration);

        return box;
    }
}