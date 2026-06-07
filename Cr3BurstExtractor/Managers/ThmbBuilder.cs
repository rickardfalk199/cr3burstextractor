using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor.Managers;

/// <summary>
/// Rebuilds the THMB box (the small thumbnail inside the moov.uuid wrapper)
/// per-frame so Lightroom and Explorer (and DPP itself) show this frame's
/// image rather than the roll's frame-0 thumbnail.
///
/// THMB layout, verified against a Canon EOS R6 Mark II burst and DPP-extracted
/// single-frame output:
///
///   +00  size      (4, big-endian)
///   +04  'THMB'    (4)
///   +08  version/flags (4, zeros)
///   +12  width     (2, big-endian, 0x00A0 = 160)
///   +14  height    (2, big-endian, 0x0078 = 120)
///   +16  jpegSize  (4, big-endian)
///   +20  constant  (4, observed 00 01 00 00 in both burst and DPP output)
///   +24  JPEG payload
///
/// We decode the per-frame track-1 preview JPEG and re-encode it at 160x120 so
/// the payload size (~3–8 KB) matches what DPP produces. Stuffing the full
/// ~100 KB track-1 JPEG into THMB worked for Lightroom but made DPP refuse the
/// file with a "?" placeholder.
/// </summary>
public static class ThmbBuilder
{
    const int HeaderTotal = 24;
    const int TargetWidth  = 160;
    const int TargetHeight = 120;
    const long JpegQuality = 80;

    public static byte[] BuildWithJpeg(byte[] origThmb, byte[] sourceJpeg)
    {
        if (origThmb.Length < HeaderTotal) return origThmb;

        byte[] payload;
        try
        {
            payload = ResizeJpeg(sourceJpeg, TargetWidth, TargetHeight);
            // Strip JFIF / EXIF APP markers. Canon's THMB JPEGs go straight
            // SOI -> DQT (no FF E0 / FF E1 segment), and DPP appears to reject
            // any other marker order in THMB, falling back to RAW decoding
            // which makes folder-thumbnail rendering crawl.
            payload = StripAppMarkers(payload);
        }
        catch
        {
            // If resize fails (corrupt source JPEG, no codec, etc.), fall back
            // to the original roll-level THMB rather than producing a broken file.
            return origThmb;
        }

        uint newBoxSize  = (uint)(HeaderTotal + payload.Length);
        uint newJpegSize = (uint)payload.Length;

        using var ms = new MemoryStream();
        BinaryHelpers.WriteUInt32BE(ms, newBoxSize);  // +00 size
        ms.Write(origThmb, 4, 12);                     // +04..+15  'THMB' + version/flags + width + height
        BinaryHelpers.WriteUInt32BE(ms, newJpegSize); // +16 jpegSize
        ms.Write(origThmb, 20, 4);                     // +20..+23  constant
        ms.Write(payload);                             // +24.. JPEG payload
        return ms.ToArray();
    }

    /// <summary>
    /// Removes APPn marker segments (FF E0 .. FF EF — i.e. JFIF, EXIF, etc.)
    /// from the start of a JPEG, leaving SOI followed immediately by the
    /// first non-APP marker. Matches Canon's THMB JPEG byte pattern.
    /// </summary>
    static byte[] StripAppMarkers(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return jpeg;

        int p = 2;
        while (p + 3 < jpeg.Length)
        {
            if (jpeg[p] != 0xFF) break;
            byte marker = jpeg[p + 1];
            // FF E0..FF EF = APPn. Stop at any other marker (DQT, SOF, etc.).
            if (marker < 0xE0 || marker > 0xEF) break;

            int segLen = (jpeg[p + 2] << 8) | jpeg[p + 3];
            if (segLen < 2 || p + 2 + segLen > jpeg.Length) break; // malformed — bail
            p += 2 + segLen;
        }

        if (p == 2) return jpeg; // nothing stripped

        var result = new byte[2 + (jpeg.Length - p)];
        result[0] = 0xFF; result[1] = 0xD8;
        Array.Copy(jpeg, p, result, 2, jpeg.Length - p);
        return result;
    }

    static byte[] ResizeJpeg(byte[] sourceJpeg, int targetWidth, int targetHeight)
    {
        using var srcStream = new MemoryStream(sourceJpeg);
        using var src = Image.FromStream(srcStream);
        using var bmp = new Bitmap(targetWidth, targetHeight);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode      = SmoothingMode.HighQuality;
            g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode    = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, targetWidth, targetHeight);
        }

        ImageCodecInfo jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .First(e => e.MimeType == "image/jpeg");
        var pars = new EncoderParameters(1);
        pars.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);

        using var outStream = new MemoryStream();
        bmp.Save(outStream, jpegEncoder, pars);
        return outStream.ToArray();
    }
}
