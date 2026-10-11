using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace NightMode;

/// Crescent moon drawn at the tray's real pixel size: filled when dimming, outlined when off.
static class TrayIcon
{
    public static Icon Draw(bool on, bool lightTaskbar)
    {
        int sz = WinForms.SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(sz, sz);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var ink = lightTaskbar ? Color.FromArgb(28, 28, 28) : Color.White;
            float p = sz * 0.08f, d = sz - 2 * p, s = sz / 10f;
            using var brush = new SolidBrush(ink);
            using var moon = Crescent(p, p, d, d * 0.42f, -d * 0.2f);
            g.FillRegion(brush, moon);
            if (!on)
            {
                using var inner = Crescent(p + s, p + s, d - 2 * s, d * 0.42f - 2 * s, -d * 0.2f - 2 * s, s * 4);
                g.CompositingMode = CompositingMode.SourceCopy;
                using var clear = new SolidBrush(Color.Transparent);
                g.FillRegion(clear, inner);
            }
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);
        return icon;
    }

    static Region Crescent(float x, float y, float d, float dx, float dy, float grow = 0)
    {
        using var disc = new GraphicsPath();
        disc.AddEllipse(x, y, d, d);
        using var bite = new GraphicsPath();
        bite.AddEllipse(x + dx, y + dy, d + grow, d + grow);
        var r = new Region(disc);
        r.Exclude(bite);
        return r;
    }
}
