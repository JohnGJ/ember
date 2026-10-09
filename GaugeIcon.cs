using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Ember;

static class GaugeIcon
{
    const int Oversample = 4;

    // The ring is the weekly limit; the core fills bottom-up with the session.
    public static Bitmap Render(int size, double ring, double core)
    {
        // GDI+ anti-aliasing alone looks muddy at 16px, so draw large and shrink.
        var big = size * Oversample;
        using var canvas = new Bitmap(big, big, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Draw(g, big, (float)Math.Clamp(ring, 0, 1), (float)Math.Clamp(core, 0, 1));
        }

        var icon = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(icon))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(canvas, new Rectangle(0, 0, size, size));
        }
        return icon;
    }

    static void Draw(Graphics g, float s, float ring, float core)
    {
        var centre = s / 2;
        var outer = s * 0.47f;
        var width = s * 0.17f;
        var gap = s * 0.07f;
        var inner = outer - width - gap;

        using var ground = new SolidBrush(Palette.Clay);
        using var fill = new SolidBrush(Palette.Orange);
        using var track = new SolidBrush(Palette.Cinder);

        using var square = RoundedRect(new RectangleF(0, 0, s, s), s * 0.14f);
        g.FillPath(ground, square);

        var ringRect = Circle(centre, outer);
        g.FillEllipse(track, ringRect);
        if (ring > 0) g.FillPie(fill, ringRect, 90, 360 * ring);
        g.FillEllipse(ground, Circle(centre, outer - width));

        var coreRect = Circle(centre, inner);
        g.FillEllipse(track, coreRect);
        if (core > 0)
        {
            var level = coreRect.Bottom - coreRect.Height * core;
            g.SetClip(new RectangleF(coreRect.X, level, coreRect.Width, coreRect.Bottom - level));
            g.FillEllipse(fill, coreRect);
            g.ResetClip();
        }
    }

    static RectangleF Circle(float centre, float radius) =>
        new(centre - radius, centre - radius, radius * 2, radius * 2);

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
