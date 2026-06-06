using System.Text;
using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor.Managers;

/// <summary>
/// THMB (small thumbnail) box rebuilder. Canon CR3 burst rolls have a single
/// roll-level THMB inside moov; copying it verbatim means every extracted frame
/// inherits frame 0's thumbnail. Lightroom (and Windows Explorer) display this
/// tiny thumbnail first because it loads almost instantly, then switch to the
/// correct PRVW preview once the larger embedded preview decodes — producing
/// the "wrong picture for a moment, then the right one" flicker the user sees.
///
/// Replacing THMB's JPEG payload per-frame fixes that. Width/height are kept
/// from the original (readers use the JPEG's own SOF dimensions in practice,
/// so the slight mismatch doesn't matter).
///
/// THMB layout (Canon EOS R6 Mark II, matches lclevy/canon_cr3 docs):
///    4  box size
///    4  'THMB'
///    4  version/flags
///    2  width
///    2  height
///    2  reserved (zero)
///    4  jpegSize
///    .. jpeg bytes
/// </summary>
public static class ThmbBuilder
{
    const int HeaderBytes = 22;          // size + 'THMB' + vers + w + h + reserved + jpegSize
    const int PreserveStart = 4;         // skip the outer size field
    const int PreserveLength = 14;       // 'THMB' + vers + w + h + reserved

    public static byte[] BuildWithJpeg(byte[] origThmb, byte[] newJpeg)
    {
        uint newBoxSize = (uint)(HeaderBytes + newJpeg.Length);
        uint newJpegSize = (uint)newJpeg.Length;

        using var ms = new MemoryStream();
        BinaryHelpers.WriteUInt32BE(ms, newBoxSize);
        // Preserve 'THMB' + version/flags + width + height + reserved
        ms.Write(origThmb, PreserveStart, PreserveLength);
        BinaryHelpers.WriteUInt32BE(ms, newJpegSize);
        ms.Write(newJpeg);
        return ms.ToArray();
    }
}
