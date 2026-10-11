using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace NightMode;

public sealed record Shortcut(string Action, string Keys, string? Note)
{
    public string[] Caps => Keys.Replace("Minus", "−").Replace("Plus", "+").Split(" + ");
    public bool Danger => Action.Contains("emergency");
}

public partial class App : Application
{
    public static App Me => (App)Current;

    public static readonly (string Name, int Dim)[] BuiltIn =
        { ("Evening", 30), ("Reading", 50), ("Dark room", 70), ("Pitch black", 90) };

    public Settings S { get; private set; } = null!;
    internal Engine Engine { get; private set; } = null!;
    public FlyoutWindow Flyout { get; private set; } = null!;
    public List<Shortcut> Shortcuts { get; } = new();

    Mutex? _single;
    WinForms.NotifyIcon? _tray;
    HwndSource? _keys;
    Window? _menuHost;
    SettingsWindow? _settings;
    readonly DispatcherTimer _schedule = new();
    readonly DispatcherTimer _ramp = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly DispatcherTimer _captureTimeout = new() { Interval = TimeSpan.FromSeconds(20) };
    readonly Dictionary<int, Action> _hotkeys = new();
    Native.WinEventProc? _onForeground; // held so the GC keeps the hook alive
    Osd? _osd;
    double _rampSet = -1;

    /// The last app in front that wasn't Night mode itself, as a process name.
    public string? LastApp { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, "NightMode.Single", out bool first);
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "NightMode.Show");
        if (!first)
        {
            showSignal.Set(); // the running copy opens its panel
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(() => Flyout.ShowNear()), null, -1, false);

        DispatcherUnhandledException += (_, a) => { Settings.Log(a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            Settings.Log(a.ExceptionObject);
            try { Engine?.Shutdown(); } catch { }
        };
        SessionEnding += (_, _) => Engine.Shutdown();

        base.OnStartup(e);
        Theme.Init();
        S = Settings.Load();
        Engine = new Engine(S);
        Flyout = new FlyoutWindow();
        Engine.Changed += UpdateTray;

        CreateTray();
        RegisterHotkeys();
        _schedule.Tick += (_, _) => ScheduleDue();
        _ramp.Tick += (_, _) => RampStep();
        _captureTimeout.Tick += (_, _) => EndCapture();
        SystemEvents.TimeChanged += (_, _) => Dispatcher.BeginInvoke(() => ApplySchedule(false));
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(() => { _icons.Clear(); UpdateTray(); });
        _onForeground = (_, _, hwnd, _, _, _, _) => OnForeground(hwnd);
        Native.SetWinEventHook(3, 3, IntPtr.Zero, _onForeground, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND
        ApplySchedule(true);
        if (S.RememberOn && S.WasOn && !Engine.On) Engine.SetOn(true);
        OnForeground(Native.GetForegroundWindow());
        UpdateTray();

        if (!S.SeenIntro && !e.Args.Contains("--startup")) Flyout.ShowNear();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Engine?.Shutdown();
        if (_tray != null) _tray.Visible = false;
        _tray?.Dispose();
        base.OnExit(e);
    }

    public void Quit() => Shutdown();

    // ---------- actions shared by panel, tray menu and shortcuts ----------

    public void Toggle() => Engine.SetOn(!Engine.On);

    public void ApplyPreset(int dim)
    {
        Engine.SetDim(dim, fade: true);
        Engine.SetOn(true);
    }

    void Step(int delta)
    {
        if (!Engine.On) { if (delta > 0) Engine.SetOn(true); return; }
        if (Engine.PausedUntil != null) Engine.Resume();
        Engine.SetDim(S.Dim + delta);
    }

    public void Emergency()
    {
        Engine.Emergency();
        _tray?.ShowBalloonTip(4000, "Full brightness restored", "Night mode is off. Turn it on again from the tray or with Ctrl + Alt + D.", WinForms.ToolTipIcon.None);
    }

    void Flash() => (_osd ??= new Osd()).Flash(Engine.On, S.Dim, Engine.UsingBacklight);

    // ---------- app rules: pause while a listed app is in front ----------

    // Taskbar, tray overflow, Start, search and the snipping overlay pass through on the way to something
    // else; treating them as "the app in front" would flicker app rules on every tray click.
    static readonly HashSet<string> TransientClasses = new() { "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow" };
    static readonly HashSet<string> TransientApps = new(StringComparer.OrdinalIgnoreCase) { "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "ScreenClippingHost", "SnippingTool", "LockApp" };

    void OnForeground(IntPtr hwnd)
    {
        Engine.OnForeground();
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == Environment.ProcessId) return; // our own panel doesn't count
        var cls = new System.Text.StringBuilder(256);
        Native.GetClassName(hwnd, cls, cls.Capacity);
        if (TransientClasses.Contains(cls.ToString())) return;
        string name;
        try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
        catch { return; } // process already gone
        if (TransientApps.Contains(name)) return;
        LastApp = name;
        CheckRules();
    }

    void CheckRules() =>
        Engine.SetAppPause(LastApp != null && S.AppRules.Contains(LastApp, StringComparer.OrdinalIgnoreCase) ? LastApp : null);

    public void SetRule(string app, bool paused)
    {
        S.AppRules.RemoveAll(a => a.Equals(app, StringComparison.OrdinalIgnoreCase));
        if (paused) S.AppRules.Add(app);
        S.Save();
        CheckRules();
    }

    // ---------- clean screenshot: filter off, Snipping Tool, back on when the snip lands ----------

    public async void CleanScreenshot()
    {
        if (Engine.Capturing) { EndCapture(); return; }
        bool dimmed = Engine.Active;
        if (dimmed)
        {
            Engine.SetCapturing(true);
            await System.Threading.Tasks.Task.Delay(150); // let the compositor show an undimmed frame first
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }); }
        catch (Exception e) { Settings.Log(e); EndCapture(); return; }
        if (dimmed) { _captureTimeout.Stop(); _captureTimeout.Start(); }
    }

    void EndCapture()
    {
        _captureTimeout.Stop();
        Engine.SetCapturing(false);
    }

    public void OpenSettings(string page = "dimming")
    {
        Flyout.Hide();
        if (_settings is not { IsVisible: true }) { _settings = new SettingsWindow(); _settings.Show(); }
        _settings.ShowPage(page);
        _settings.Activate();
    }

    // ---------- start with Windows ----------

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool StartsWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("NightMode") != null;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("NightMode", $"\"{Environment.ProcessPath}\" --startup");
            else k.DeleteValue("NightMode", false);
        }
    }

    // ---------- schedule: one timer aimed at the next on/off moment ----------

    public static bool TryTimes(Settings s, out TimeSpan on, out TimeSpan off)
    {
        bool ok = TimeSpan.TryParse(s.From, out on) & TimeSpan.TryParse(s.To, out off);
        return ok && on != off && on < TimeSpan.FromDays(1) && off < TimeSpan.FromDays(1);
    }

    /// enforce: turn on right away when we're already inside the window (start-up, schedule just edited).
    public void ApplySchedule(bool enforce)
    {
        _schedule.Stop();
        if (!S.ScheduleOn || !TryTimes(S, out var on, out var off)) return;
        var now = DateTime.Now;
        if (enforce && DimMath.InWindow(now.TimeOfDay, on, off)) StartScheduled(on);
        _schedule.Interval = DimMath.NextChange(now, on, off) - now + TimeSpan.FromSeconds(1);
        _schedule.Start();
    }

    void ScheduleDue()
    {
        if (TryTimes(S, out var on, out var off))
        {
            if (DimMath.InWindow(DateTime.Now.TimeOfDay, on, off)) StartScheduled(on);
            else { _ramp.Stop(); Engine.SetOn(false); }
        }
        ApplySchedule(false);
    }

    /// Turn on at the scheduled level, easing in from 0 so the change goes unnoticed.
    void StartScheduled(TimeSpan on)
    {
        _ramp.Stop();
        double p = DimMath.EaseProgress(DateTime.Now, on, S.EaseMinutes);
        Engine.SetDim(S.ScheduleDim * p);
        _rampSet = S.Dim;
        Engine.SetOn(true);
        if (p < 1) _ramp.Start();
    }

    void RampStep()
    {
        // the user moved the level or switched off: they're in charge now
        if (!Engine.On || S.Dim != _rampSet || !TryTimes(S, out var on, out _)) { _ramp.Stop(); return; }
        double p = DimMath.EaseProgress(DateTime.Now, on, S.EaseMinutes);
        Engine.SetDim(S.ScheduleDim * p);
        _rampSet = S.Dim;
        if (p >= 1) _ramp.Stop();
    }

    // ---------- global shortcuts ----------

    void RegisterHotkeys()
    {
        _keys = new HwndSource(new HwndSourceParameters("NightModeKeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _keys.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == 0x0312 && _hotkeys.TryGetValue(wParam.ToInt32(), out var act)) { act(); handled = true; }
            if (msg == 0x031D && Engine.Capturing) EndCapture(); // WM_CLIPBOARDUPDATE: the snip landed
            return IntPtr.Zero;
        });
        Native.AddClipboardFormatListener(_keys.Handle);

        const uint Ctrl = 0x2, Alt = 0x1, NoRepeat = 0x4000;
        Add("Turn on or off", "Ctrl + Alt + D", () => { Toggle(); Flash(); }, (Ctrl | Alt | NoRepeat, 0x44));
        Add("Darker", "Ctrl + Alt + Minus", () => { Step(5); Flash(); }, (Ctrl | Alt, 0xBD), (Ctrl | Alt, 0x6D));
        Add("Brighter", "Ctrl + Alt + Plus", () => { Step(-5); Flash(); }, (Ctrl | Alt, 0xBB), (Ctrl | Alt, 0x6B));
        Add("Screenshot at normal brightness", "Ctrl + Alt + S", CleanScreenshot, (Ctrl | Alt | NoRepeat, 0x53));
        Add("Full brightness (emergency)", "Ctrl + Alt + Backspace", () => { Emergency(); Flash(); }, (Ctrl | Alt | NoRepeat, 0x08));
    }

    void Add(string action, string keys, Action act, params (uint mods, uint vk)[] combos)
    {
        bool ok = false;
        foreach (var (mods, vk) in combos)
        {
            int id = _hotkeys.Count + 1;
            if (Native.RegisterHotKey(_keys!.Handle, id, mods, vk)) { _hotkeys[id] = act; ok = true; }
            else _hotkeys[id] = () => { };
        }
        Shortcuts.Add(new Shortcut(action, keys, ok ? null : "Another app is already using this shortcut."));
    }

    // ---------- tray ----------

    readonly Dictionary<(bool, bool), System.Drawing.Icon> _icons = new();

    void CreateTray()
    {
        _tray = new WinForms.NotifyIcon { Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) Flyout.Toggle();
            else if (e.Button == WinForms.MouseButtons.Right) ShowMenu();
            else if (e.Button == WinForms.MouseButtons.Middle) { Toggle(); Flash(); }
        };
    }

    void UpdateTray()
    {
        if (_tray == null) return;
        bool lightTaskbar = (Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                                ?.GetValue("SystemUsesLightTheme") as int?) == 1;
        var key = (Engine.Active, lightTaskbar);
        if (!_icons.TryGetValue(key, out var icon)) _icons[key] = icon = TrayIcon.Draw(key.Item1, lightTaskbar);
        _tray.Icon = icon;
        _tray.Text = Engine.AppPausedBy != null ? "Night mode: paused for " + Engine.AppPausedBy
                   : Engine.PausedUntil != null ? "Night mode: paused"
                   : Engine.On ? $"Night mode: on, {S.Dim:0}%"
                   : "Night mode: off";
    }

    void ShowMenu()
    {
        Flyout.Hide();
        var m = new ContextMenu { Placement = PlacementMode.MousePoint, Style = (Style)FindResource("Menu") };
        Item(m, Engine.On ? "Turn off" : "Turn on", Toggle, "Ctrl Alt D");
        Line(m);
        foreach (var (name, dim) in BuiltIn)
            Item(m, name, () => ApplyPreset(dim), $"{dim}%").IsChecked = Engine.On && (int)S.Dim == dim;
        Line(m);
        Item(m, "Greyscale", () => Engine.SetGreyscale(!S.Greyscale)).IsChecked = S.Greyscale;
        if (LastApp is { } app)
        {
            bool ruled = S.AppRules.Contains(app, StringComparer.OrdinalIgnoreCase);
            Item(m, $"Don't dim while {app} is in front", () => SetRule(app, !ruled)).IsChecked = ruled;
        }
        Line(m);
        Item(m, "Clean screenshot", CleanScreenshot, "Ctrl Alt S");
        if (Engine.PausedUntil != null) Item(m, "Resume dimming", Engine.Resume);
        else if (Engine.On) Item(m, "Pause for 1 minute", () => Engine.Pause(TimeSpan.FromMinutes(1)));
        Item(m, "Full brightness", Emergency, "Ctrl Alt ⌫").SetResourceReference(Control.ForegroundProperty, "Nm.Danger");
        Line(m);
        Item(m, "Settings", () => OpenSettings());
        Item(m, "Quit Night mode", Quit);

        // A context menu only closes on outside clicks when its owner is the foreground window.
        _menuHost ??= new Window
        {
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
            Width = 1, Height = 1, Left = -32000, Top = -32000, Topmost = true,
        };
        _menuHost.Show();
        Native.SetForegroundWindow(new WindowInteropHelper(_menuHost).Handle);
        m.Closed += (_, _) => _menuHost.Hide();
        m.IsOpen = true;
    }

    void Line(ContextMenu m) => m.Items.Add(new Separator { Style = (Style)FindResource("MenuLine") });

    MenuItem Item(ContextMenu m, string header, Action act, string? gesture = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "", Style = (Style)FindResource("MenuRow") };
        mi.Click += (_, _) => act();
        m.Items.Add(mi);
        return mi;
    }
}
