using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace NightMode;

/// Laptop panel backlight through WMI (late-bound COM, so no extra package).
/// Every WMI call runs on a pool thread: never on the UI thread, latest request wins.
sealed class Backlight
{
    [ThreadStatic] static object? t_wmi;
    readonly Settings _s; // RestoreBrightness lives in settings so it survives a crash
    readonly object _gate = new();
    double? _pending;
    bool _hasPending, _running;

    public bool Available { get; }

    public Backlight(Settings s)
    {
        _s = s;
        try { var t = Task.Run(Read); Available = t.Wait(3000) && t.Result >= 0; }
        catch (Exception e) { Settings.Log(e); }
    }

    static dynamic Wmi() => t_wmi ??=
        ((dynamic)Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!).ConnectServer(".", @"root\WMI");

    static int Read()
    {
        var set = Wmi().ExecQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
        // WMI property values are only reliable through Properties_, not as dispatch members
        return set.Count > 0 ? Convert.ToInt32(set.ItemIndex(0).Properties_.Item("CurrentBrightness").Value) : -1;
    }

    static void Write(int value)
    {
        var set = Wmi().ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
        if (set.Count == 0) return;
        var obj = set.ItemIndex(0);
        var args = obj.Methods_.Item("WmiSetBrightness").InParameters.SpawnInstance_();
        args.Properties_.Item("Timeout").Value = 1;
        args.Properties_.Item("Brightness").Value = (byte)Math.Clamp(value, 0, 100);
        obj.ExecMethod_("WmiSetBrightness", args);
    }

    /// fraction: share of the user's own level to show. null: give their own level back.
    public void Request(double? fraction)
    {
        lock (_gate)
        {
            _pending = fraction;
            _hasPending = true;
            if (_running) return;
            _running = true;
        }
        ThreadPool.QueueUserWorkItem(_ => Pump());
    }

    void Pump()
    {
        for (; ; )
        {
            double? f;
            lock (_gate)
            {
                if (!_hasPending) { _running = false; return; }
                f = _pending;
                _hasPending = false;
            }
            Apply(f);
        }
    }

    void Apply(double? fraction)
    {
        try
        {
            int original = _s.RestoreBrightness;
            if (fraction == null)
            {
                if (original < 0) return;
                Write(original);
                Remember(-1);
                return;
            }
            if (original < 0)
            {
                original = Read();
                if (original < 0) return;
                Remember(original);
            }
            Write((int)Math.Round(original * fraction.Value));
        }
        catch (Exception e) { Settings.Log(e); }
    }

    void Remember(int original)
    {
        _s.RestoreBrightness = original;
        Application.Current?.Dispatcher.BeginInvoke(_s.SaveNow);
    }

    /// Exit path: wait for in-flight work, then restore synchronously.
    public void RestoreNow()
    {
        lock (_gate) _hasPending = false;
        Task.Run(() =>
        {
            for (int i = 0; i < 100 && Volatile.Read(ref _running); i++) Thread.Sleep(10);
            Apply(null);
        }).Wait(3000);
    }
}

/// Fallback dimmer: a click-through, capture-excluded black layer over every monitor.
sealed class OverlayForm : WinForms.Form
{
    public OverlayForm()
    {
        FormBorderStyle = WinForms.FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = WinForms.FormStartPosition.Manual;
        BackColor = Color.Black;
        Bounds = WinForms.SystemInformation.VirtualScreen;
    }

    protected override bool ShowWithoutActivation => true;

    protected override WinForms.CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // LAYERED | TRANSPARENT (click-through) | TOOLWINDOW (no Alt+Tab) | NOACTIVATE | TOPMOST
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000 | 0x8;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowDisplayAffinity(Handle, 0x11); // WDA_EXCLUDEFROMCAPTURE: screenshots stay normal
    }

    public void KeepOnTop()
    {
        if (Visible) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }
}

/// Owns everything that touches the screen: backlight, colour filter, overlay.
sealed class Engine
{
    readonly Settings _s;
    readonly Backlight _bl;
    readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromMilliseconds(15) };
    readonly DispatcherTimer _pause = new();
    readonly DispatcherTimer _later = new() { Interval = TimeSpan.FromSeconds(1.5) };
    bool _mag;
    OverlayForm? _overlay;
    double _f = 1, _w, _fromF, _fromW, _toF, _toW;
    bool _grey;
    DateTime _fadeStart;

    public bool On { get; private set; }
    public DateTime? PausedUntil { get; private set; }
    /// Set while an app from the App rules list is in front.
    public string? AppPausedBy { get; private set; }
    /// Clean screenshot in progress: on-screen filter off, backlight left as is.
    public bool Capturing { get; private set; }
    public bool Active => On && PausedUntil == null && AppPausedBy == null;
    public bool HasBacklight => _bl.Available;
    public bool UsingBacklight => _bl.Available && _s.UseBacklight;
    public string? Problem { get; private set; }
    public event Action? Changed;

    public Engine(Settings s)
    {
        _s = s;
        _bl = new Backlight(s);
        _mag = Native.MagInitialize();
        _fade.Tick += (_, _) => FadeStep();
        _pause.Tick += (_, _) => Resume();
        _later.Tick += (_, _) => { _later.Stop(); Apply(false); };
        // Windows resets the backlight on resume, charger plug/unplug, display changes and unlock.
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode != PowerModes.Suspend) Soon(); };
        SystemEvents.DisplaySettingsChanged += (_, _) => Soon();
        SystemEvents.SessionSwitch += (_, e) => { if (e.Reason == SessionSwitchReason.SessionUnlock) Soon(); };
        Apply(false); // also hands back a backlight left dimmed by a crash
    }

    void Soon() => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        if (_overlay != null) _overlay.Bounds = WinForms.SystemInformation.VirtualScreen;
        _later.Stop();
        _later.Start();
    });

    public void SetOn(bool on)
    {
        if (On == on && PausedUntil == null) return;
        On = on;
        StopPause();
        _s.WasOn = on;
        _s.Save();
        Apply(true);
    }

    public void SetGreyscale(bool grey)
    {
        _s.Greyscale = grey;
        _s.Save();
        Apply(true);
    }

    public void SetAppPause(string? app)
    {
        if (AppPausedBy == app) return;
        AppPausedBy = app;
        Apply(true);
    }

    public void SetCapturing(bool capturing)
    {
        if (Capturing == capturing) return;
        Capturing = capturing;
        Apply(false);
    }

    public void OnForeground() => _overlay?.KeepOnTop();

    public void SetDim(double dim, bool fade = false)
    {
        _s.Dim = Math.Clamp(Math.Round(dim), 0, 100);
        _s.Save();
        Apply(fade);
    }

    public void SetWarmth(double warmth)
    {
        _s.Warmth = Math.Clamp(Math.Round(warmth), 0, 100);
        _s.Save();
        Apply(false);
    }

    public void Pause(TimeSpan span)
    {
        if (!On) return;
        PausedUntil = DateTime.Now + span;
        _pause.Stop();
        _pause.Interval = span;
        _pause.Start();
        Apply(true);
    }

    public void Resume()
    {
        StopPause();
        Apply(true);
    }

    void StopPause()
    {
        _pause.Stop();
        PausedUntil = null;
    }

    /// Instant, no fade: the user may be looking at a black screen.
    public void Emergency()
    {
        On = false;
        StopPause();
        _s.WasOn = false;
        _s.Save();
        Apply(false);
    }

    public void SetOverlay(bool overlay)
    {
        if (overlay) Identity();
        else _mag = Native.MagInitialize();
        _s.UseOverlay = overlay;
        _s.Save();
        Problem = null;
        Apply(false);
    }

    public void Reapply() => Apply(false);

    void Apply(bool fade)
    {
        bool hw = Active && UsingBacklight;
        var (level, factor) = Active ? DimMath.Map(_s.Dim, hw) : (-1, 1.0);
        _bl.Request(hw ? level : null); // null also hands back a backlight left dimmed by a crash

        double warmth = Active ? _s.Warmth : 0;
        _grey = Active && _s.Greyscale;
        if (Capturing) (factor, warmth, _grey, fade) = (1.0, 0.0, false, false);
        if (fade && SystemParameters.ClientAreaAnimation)
        {
            (_fromF, _fromW, _toF, _toW, _fadeStart) = (_f, _w, factor, warmth, DateTime.Now);
            _fade.Start();
        }
        else
        {
            _fade.Stop();
            Show(factor, warmth);
        }
        Changed?.Invoke();
    }

    void FadeStep()
    {
        double t = Math.Min(1, (DateTime.Now - _fadeStart).TotalMilliseconds / 150);
        Show(_fromF + (_toF - _fromF) * t, _fromW + (_toW - _fromW) * t);
        if (t >= 1) _fade.Stop();
    }

    void Show(double f, double w)
    {
        _f = f;
        _w = w;
        if (_mag && !_s.UseOverlay)
        {
            _overlay?.Hide();
            var e = new Native.MAGCOLOREFFECT { m = DimMath.Matrix(f, w, _grey) };
            if (Native.MagSetFullscreenColorEffect(ref e)) return;
            _mag = false;
            Problem = "Windows didn't allow the colour filter, so Night mode switched to Overlay. Check that Windows colour filters and Magnifier are off, then choose Colour filter again in Settings.";
            Changed?.Invoke();
        }

        if (f > 0.999 && w <= 0) { _overlay?.Hide(); return; }
        _overlay ??= new OverlayForm(); // overlay can only darken and tint; greyscale needs the colour filter
        _overlay.BackColor = Color.FromArgb((int)(1.1 * w), (int)(0.45 * w), 0); // dark amber when warm
        _overlay.Opacity = Math.Max(1 - f, 0.0025 * w);
        if (!_overlay.Visible) Native.ShowWindow(_overlay.Handle, 4); // SW_SHOWNOACTIVATE: Form.Show() took focus and closed the panel
        _overlay.KeepOnTop();
    }

    void Identity()
    {
        if (!_mag) return;
        var e = new Native.MAGCOLOREFFECT { m = DimMath.Matrix(1, 0) };
        Native.MagSetFullscreenColorEffect(ref e);
    }

    /// Hand the screen back exactly as we found it.
    public void Shutdown()
    {
        _fade.Stop();
        _bl.RestoreNow();
        _s.SaveNow();
        try
        {
            Identity();
            if (_mag) Native.MagUninitialize();
            _overlay?.Close();
        }
        catch { /* process exit clears both anyway */ }
    }
}
