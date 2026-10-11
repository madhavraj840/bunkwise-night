using System;

namespace NightMode;

/// Pure maths, no Windows calls, so checks/ can run it.
public static class DimMath
{
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    /// Dim % at which the laptop backlight reaches its hardware minimum.
    public const double Boundary = 40;

    /// Darkest the on-screen filter goes: 4% of normal light, never pure black.
    public const double Floor = 0.04;

    /// hw: backlight as a fraction of the user's own level (-1 = backlight not used).
    /// factor: on-screen light multiplier.
    public static (double hw, double factor) Map(double dim, bool backlight)
    {
        dim = Clamp(dim, 0, 100);
        if (!backlight) return (-1, Math.Pow(Floor, dim / 100));
        if (dim <= Boundary) return (1 - dim / Boundary, 1);
        // exponential so each step past the hardware minimum looks equally darker
        return (0, Math.Pow(Floor, (dim - Boundary) / (100 - Boundary)));
    }

    /// 5x5 colour matrix for MagSetFullscreenColorEffect (row vector [r g b a 1] times M).
    /// Greyscale first (luma weights), then dim and warm each output channel.
    public static float[] Matrix(double factor, double warmth, bool grey = false)
    {
        double w = Clamp(warmth, 0, 100) / 100;
        var d = new[] { factor, factor * (1 - 0.18 * w), factor * (1 - 0.6 * w) };
        var m = new float[25];
        for (int i = 0; i < 3; i++)       // input channel (row)
            for (int j = 0; j < 3; j++)   // output channel (column)
                m[i * 5 + j] = (float)((grey ? Luma[i] : i == j ? 1 : 0) * d[j]);
        m[18] = 1; // alpha
        m[24] = 1;
        return m;
    }

    /// Typed schedule time: accepts 22:00, 22.00, 2200, 930, 22 and 7:5. Returns HH:mm, or null if it isn't a time.
    public static string? ParseTime(string text)
    {
        var t = text.Trim().Replace('.', ':');
        if (t.IndexOf(':') < 0) t = t.Length <= 2 ? t + ":00" : t.Length is 3 or 4 ? t.Substring(0, t.Length - 2) + ":" + t.Substring(t.Length - 2) : t;
        var parts = t.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], System.Globalization.NumberStyles.None, null, out int h)
            && int.TryParse(parts[1], System.Globalization.NumberStyles.None, null, out int m) && h < 24 && m < 60)
            return $"{h:00}:{m:00}";
        return null;
    }

    static readonly double[] Luma = { 0.2126, 0.7152, 0.0722 };

    /// How far into a schedule's ease-in we are, 0..1. The window started at the most recent `on`.
    public static double EaseProgress(DateTime now, TimeSpan on, double minutes)
    {
        if (minutes <= 0) return 1;
        var start = now.Date + on;
        if (start > now) start = start.AddDays(-1);
        return Clamp((now - start).TotalMinutes / minutes, 0, 1);
    }

    /// True when `now` falls inside the on..off window, including windows that cross midnight.
    public static bool InWindow(TimeSpan now, TimeSpan on, TimeSpan off) =>
        on <= off ? now >= on && now < off : now >= on || now < off;

    /// The next moment the schedule flips, strictly after `now`.
    public static DateTime NextChange(DateTime now, TimeSpan on, TimeSpan off)
    {
        DateTime Next(TimeSpan t) { var d = now.Date + t; return d > now ? d : d.AddDays(1); }
        var a = Next(on);
        var b = Next(off);
        return a < b ? a : b;
    }
}
