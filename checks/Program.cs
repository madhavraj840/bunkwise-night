using NightMode;

static void Check(bool ok, string what) { if (!ok) throw new Exception("FAILED: " + what); Console.WriteLine("ok  " + what); }
static TimeSpan T(string s) => TimeSpan.Parse(s);

// backlight stage is a fraction of the user's own level, so it never gets brighter than it
Check(DimMath.Map(0, true) == (1.0, 1.0), "dim 0 keeps the original backlight");
Check(DimMath.Map(20, true) == (0.5, 1.0), "half-way to the boundary halves the backlight");
Check(DimMath.Map(40, true) == (0.0, 1.0), "boundary = hardware minimum, no filter yet");
Check(DimMath.Map(100, true).factor is > 0.039 and < 0.041, "dim 100 = 4% floor");
Check(DimMath.Map(70, true).factor < DimMath.Map(60, true).factor, "darker past the minimum");
Check(DimMath.Map(150, true) == DimMath.Map(100, true) && DimMath.Map(-5, true) == DimMath.Map(0, true), "input clamped");
Check(DimMath.Map(0, false) == (-1, 1.0) && DimMath.Map(100, false).factor < 0.041, "no backlight: filter covers the whole range");

var m = DimMath.Matrix(0.5, 100);
Check(m[0] == 0.5f && m[6] < m[0] && m[12] < m[6] && m[18] == 1 && m[24] == 1, "warm matrix cuts blue most, keeps alpha");
Check(m[1] == 0 && m[5] == 0, "colour matrix keeps channels separate");
var g = DimMath.Matrix(1, 0, grey: true);
Check(Math.Abs(g[0] + g[5] + g[10] - 1) < 1e-6 && g[0] == g[1] && g[1] == g[2], "greyscale: equal output per input, white stays white");

// schedule ease-in
var on22 = T("22:00");
Check(DimMath.EaseProgress(new DateTime(2026, 10, 11, 22, 10, 0), on22, 20) == 0.5, "10 of 20 minutes = half way");
Check(DimMath.EaseProgress(new DateTime(2026, 10, 12, 1, 0, 0), on22, 20) == 1, "after midnight the ease is long done");
Check(DimMath.EaseProgress(new DateTime(2026, 10, 11, 22, 10, 0), on22, 0) == 1, "ease off = full level at once");

// schedule
Check(DimMath.InWindow(T("23:00"), T("22:00"), T("06:30")), "overnight window includes 23:00");
Check(DimMath.InWindow(T("03:00"), T("22:00"), T("06:30")), "overnight window includes 03:00");
Check(!DimMath.InWindow(T("12:00"), T("22:00"), T("06:30")), "overnight window excludes noon");
Check(DimMath.InWindow(T("10:00"), T("09:00"), T("17:00")) && !DimMath.InWindow(T("17:00"), T("09:00"), T("17:00")), "day window, end exclusive");
var now = new DateTime(2026, 10, 11, 23, 0, 0);
Check(DimMath.NextChange(now, T("22:00"), T("06:30")) == new DateTime(2026, 10, 12, 6, 30, 0), "next change after 23:00 is 06:30 tomorrow");
Check(DimMath.NextChange(new DateTime(2026, 10, 11, 22, 0, 0), T("22:00"), T("06:30")) == new DateTime(2026, 10, 12, 6, 30, 0), "exactly at on-time looks ahead");
// typed schedule times
Check(DimMath.ParseTime("22:00") == "22:00" && DimMath.ParseTime("2200") == "22:00" && DimMath.ParseTime("930") == "09:30", "times with and without a colon");
Check(DimMath.ParseTime("7") == "07:00" && DimMath.ParseTime("7.5") == "07:05" && DimMath.ParseTime(" 06:30 ") == "06:30", "short and dotted times");
Check(DimMath.ParseTime("24:00") == null && DimMath.ParseTime("12:60") == null && DimMath.ParseTime("abc") == null && DimMath.ParseTime("") == null && DimMath.ParseTime("-1:00") == null, "rejects non-times");
Console.WriteLine("all checks passed");
