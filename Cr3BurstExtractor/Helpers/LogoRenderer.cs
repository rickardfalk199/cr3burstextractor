using System.Drawing.Drawing2D;

namespace Cr3BurstExtractor.Helpers;

/// <summary>
/// Resolution-independent renderer for the app's burst-stack logo. Used by both
/// the splash screen panel and the form icon so they stay visually consistent.
/// </summary>
public static class LogoRenderer
{
    static readonly Color[] Palette =
    {
        Color.FromArgb(255, 30, 110, 200),
        Color.FromArgb(255, 60, 140, 220),
        Color.FromArgb(255, 95, 170, 240),
        Color.FromArgb(255, 130, 200, 250),
    };

    public static void Draw(Graphics g, int width, int height)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int n = Palette.Length;
        int min = Math.Min(width, height);
        int offset = Math.Max(1, min * 8 / 100);
        int squareSize = Math.Max(4, min - (n - 1) * offset - 2);
        int radius = Math.Max(1, squareSize / 8);
        float strokeWidth = Math.Max(1f, min / 40f);

        int totalSpan = squareSize + (n - 1) * offset;
        int startX = (width - totalSpan) / 2;
        int startY = (height - totalSpan) / 2;

        for (int i = 0; i < n; i++)
        {
            int x = startX + i * offset;
            int y = startY + (n - 1 - i) * offset;
            var rect = new Rectangle(x, y, squareSize, squareSize);
            using var brush = new SolidBrush(Palette[i]);
            using var pen = new Pen(Color.White, strokeWidth);
            using var path = RoundedPath(rect, radius);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
    }

    public static Icon CreateIcon(int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            Draw(g, size, size);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}