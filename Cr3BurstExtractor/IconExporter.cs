using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor;

/// <summary>
/// One-shot helper that bakes the burst-stack logo (drawn by <see cref="LogoRenderer"/>)
/// into a multi-resolution .ico file. The .ico is referenced by the project's
/// &lt;ApplicationIcon&gt; property so the published .exe shows the custom icon in Explorer.
///
/// Trigger via: Cr3BurstExtractor.exe --generate-icon [path]   (default path: app.ico)
/// </summary>
internal static class IconExporter
{
    public static void WriteMultiResIcon(string path, int[] sizes)
    {
        // Render each size as a PNG-encoded image (Windows Vista+ supports PNG-in-ICO).
        var images = new (int Width, int Height, byte[] Png)[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            using var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                LogoRenderer.Draw(g, sz, sz);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            images[i] = (sz, sz, ms.ToArray());
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR
        bw.Write((ushort)0);                // reserved
        bw.Write((ushort)1);                // type: ICO
        bw.Write((ushort)images.Length);    // image count

        // ICONDIRENTRY for each image. Data offsets are computed after the directory.
        int dataOffset = 6 + 16 * images.Length;
        foreach (var img in images)
        {
            bw.Write((byte)(img.Width  >= 256 ? 0 : img.Width));   // 0 means 256
            bw.Write((byte)(img.Height >= 256 ? 0 : img.Height));
            bw.Write((byte)0);              // colour palette count (0 for true-colour)
            bw.Write((byte)0);              // reserved
            bw.Write((ushort)1);            // colour planes
            bw.Write((ushort)32);           // bits per pixel
            bw.Write((uint)img.Png.Length); // image data size
            bw.Write((uint)dataOffset);
            dataOffset += img.Png.Length;
        }

        // Image data, in the same order as the directory entries.
        foreach (var img in images)
            bw.Write(img.Png);
    }
}
