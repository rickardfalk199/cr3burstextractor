using System.Text;
using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor.Managers;

/// <summary>
/// Assembles a single-frame CR3 byte array.  Pulls the chosen frame's sample
/// bytes out of each track, asks <see cref="MoovBuilder"/> for a patched moov,
/// asks <see cref="PrvwBuilder"/> for a per-frame PRVW, and writes
///   ftyp | moov | top-level uuids | mdat
/// with CTBO and chunk offsets re-pointed to the new layout.
/// </summary>
public static class FrameBuilder
{
    public static byte[] Build(
        byte[] src,
        Box? ftypBox,
        Box moovBox,
        List<Box> traks,
        List<Box> topUuids,
        long origMdatBoxOffset,
        int frameIdx)
    {
        // ---------- 1. Collect each track's sample bytes for this frame ----------
        // We keep them in track order so we can write them sequentially into mdat.
        var frameSamples = new List<(byte[] Data, int TrakIdx)>();
        for (int ti = 0; ti < traks.Count; ti++)
        {
            var stbl = BoxQuery.GetStbl(traks[ti]);
            if (stbl == null)
            {
                frameSamples.Add((Array.Empty<byte>(), ti));
                continue;
            }

            var offsets = SampleTableReader.ReadCo64(src, stbl) ?? SampleTableReader.ReadStco(src, stbl);
            var sizes = SampleTableReader.ReadStsz(src, stbl);

            if (offsets == null || sizes == null || frameIdx >= offsets.Count)
            {
                frameSamples.Add((Array.Empty<byte>(), ti));
                continue;
            }

            long off = offsets[frameIdx];
            long size = sizes[frameIdx];

            byte[] data = size > 0 ? BoxQuery.ReadSlice(src, off, size) : Array.Empty<byte>();
            frameSamples.Add((data, ti));
        }

        // ---------- 2. Output layout ----------
        // We write:  ftyp (24) | moov (variable) | top-level uuids (verbatim) | mdat
        // The top-level uuids (XMP / PRVW preview / CMTA) are copied unchanged, and
        // the CTBO table inside moov is re-pointed to their new positions and to the
        // new (single-frame) mdat.

        // ---------- 3. Clone moov with per-frame sample tables ----------
        byte[] patchedMoov = MoovBuilder.BuildPatched(
            src,
            moovBox,
            frameSamples,
            frameIdx,
            out long moovSize);

        // Grab verbatim bytes of every top-level uuid box.
        var uuidBytes = topUuids.Select(u => BoxQuery.GetRawBox(src, u)).ToList();

        // Replace the roll-level PRVW preview with one wrapping this frame's JPEG.
        // Without this, every extracted file inherits frame 0's preview and they all
        // look identical in file browsers / thumbnail viewers.
        byte[]? frameJpeg = PrvwBuilder.FindJpegSample(frameSamples);
        if (frameJpeg != null)
        {
            for (int i = 0; i < topUuids.Count; i++)
            {
                if (PrvwBuilder.IsPrvwUuid(src, topUuids[i]))
                    uuidBytes[i] = PrvwBuilder.BuildWithJpeg(uuidBytes[i], frameJpeg);
            }
        }

        // ---------- 4. Compute the new file offsets of everything after moov ----------
        long ftypSize = 24; // always 24 bytes for the ftyp we write

        // Map each carried-over top-level box's ORIGINAL offset -> (new offset, new size)
        // so we can re-point the CTBO records (which store original offsets).
        var offsetMap = new Dictionary<long, (long Off, long Size)>();

        long cursor = ftypSize + moovSize;
        for (int i = 0; i < topUuids.Count; i++)
        {
            offsetMap[topUuids[i].RawOffset] = (cursor, uuidBytes[i].Length);
            cursor += uuidBytes[i].Length;
        }

        long mdatBoxOffset = cursor;
        long mdatHeaderSize = 8;
        long mdatPayloadOffset = mdatBoxOffset + mdatHeaderSize;

        long mdatPayloadSize = 0;
        foreach (var (data, _) in frameSamples) mdatPayloadSize += data.Length;
        long mdatBoxSize = mdatPayloadSize + mdatHeaderSize;

        offsetMap[origMdatBoxOffset] = (mdatBoxOffset, mdatBoxSize);

        // ---------- 5. Patch co64/stco (chunk offsets) and CTBO (top-level table) ----------
        BoxPatcher.PatchOffsets(patchedMoov, frameSamples, mdatPayloadOffset);
        BoxPatcher.PatchCtbo(patchedMoov, offsetMap);
        BoxPatcher.PatchCctp(patchedMoov); // mark the container single-image (roll flag 2 -> 1)

        // ---------- 6. Assemble output ----------
        using var ms = new MemoryStream();

        // ftyp: brand='crx ', version=1, compatible=['crx ','isom']
        SampleTableWriter.WriteFtyp(ms);

        // moov
        ms.Write(patchedMoov);

        // top-level uuids (XMP / PRVW preview / CMTA), verbatim
        foreach (var ub in uuidBytes) ms.Write(ub);

        // mdat
        BinaryHelpers.WriteUInt32BE(ms, (uint)mdatBoxSize);
        ms.Write(Encoding.ASCII.GetBytes("mdat"));
        foreach (var (data, _) in frameSamples) ms.Write(data);

        return ms.ToArray();
    }
}