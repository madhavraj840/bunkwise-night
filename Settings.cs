using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace NightMode;

public sealed class Settings
{
    public double Dim { get; set; } = 50;
    public double Warmth { get; set; }
    public bool UseBacklight { get; set; } = true;
    public bool UseOverlay { get; set; }
    public bool ScheduleOn { get; set; }
    public string From { get; set; } = "22:00";
    public string To { get; set; } = "06:30";
    public double ScheduleDim { get; set; } = 60;
    /// Minutes the schedule takes to reach its level (0 = at once).
    public int EaseMinutes { get; set; } = 20;
    public bool Greyscale { get; set; }
    /// Turn back on after a restart if it was on when Windows shut down.
    public bool RememberOn { get; set; }
    public bool WasOn { get; set; }
    /// Process names (no .exe) that pause dimming while in front.
    public List<string> AppRules { get; set; } = new();
    public List<int> Presets { get; set; } = new();
    /// Backlight % from before we dimmed. >= 0 means we still owe a restore (survives a crash).
    public int RestoreBrightness { get; set; } = -1;
    public bool SeenIntro { get; set; }

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NightMode");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    DispatcherTimer? _debounce;

    public static Settings Load()
    {
        // JavaScriptSerializer (built into .NET Framework) runs the constructor first, so missing keys keep their defaults
        try { return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    /// Coalesces slider drags into one write.
    public void Save()
    {
        if (_debounce == null)
        {
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _debounce.Tick += (_, _) => SaveNow();
        }
        _debounce.Stop();
        _debounce.Start();
    }

    public void SaveNow()
    {
        _debounce?.Stop();
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(this));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
        }
        catch { /* a failed save must never take the dimmer down */ }
    }

    public static void Log(object error)
    {
        try { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "errors.log"), $"{DateTime.Now:u} {error}\n\n"); }
        catch { }
    }
}
