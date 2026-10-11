using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NightMode;

/// The tray panel: readout, light-meter scale, presets, then a quieter block.
public partial class FlyoutWindow : Window
{
    readonly App _app = App.Me;
    readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _sync = true;
    DateTime _hiddenAt;

    public FlyoutWindow()
    {
        InitializeComponent();
        _app.Engine.Changed += () => { if (IsVisible) Refresh(); };
        _countdown.Tick += (_, _) => Refresh();
        // turning the overlay on sends a stray deactivate while we stay in front; only hide for a real switch away
        Deactivated += (_, _) => { if (Native.GetForegroundWindow() != new WindowInteropHelper(this).Handle) HideNow(); };
        SizeChanged += (_, _) => Anchor();
        _sync = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.Dwm(hwnd, 33, 2); // DWMWA_WINDOW_CORNER_PREFERENCE: round
        void Border() => Native.Dwm(hwnd, 34, Native.ColorRef(Theme.Color("Nm.LineStrong"))); // DWMWA_BORDER_COLOR
        Border();
        Theme.Changed += Border;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) HideNow();
        base.OnKeyDown(e);
    }

    public void Toggle()
    {
        if (IsVisible) HideNow();
        // a tray click that just closed us via Deactivated shouldn't reopen us
        else if ((DateTime.Now - _hiddenAt).TotalMilliseconds > 300) ShowNear();
    }

    public void ShowNear()
    {
        Refresh();
        Show();
        Anchor();
        Activate();
        DimScale.Focus();
        if (SystemParameters.ClientAreaAnimation)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        }
    }

    void HideNow()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.Now;
        _countdown.Stop();
        Hide();
    }

    void Anchor()
    {
        if (!IsVisible) return;
        var wa = SystemParameters.WorkArea;
        MaxHeight = wa.Height - 24; // high scaling on a small screen: scroll instead of clipping
        Left = wa.Right - ActualWidth - 12;
        Top = wa.Bottom - ActualHeight - 12;
    }

    void Refresh()
    {
        _sync = true;
        var e = _app.Engine;
        var s = _app.S;
        bool bl = e.UsingBacklight;

        OnSwitch.IsChecked = e.On;
        StatusText.Text = !e.On ? "Screen at normal brightness"
            : e.Capturing ? "Undimmed for a screenshot"
            : e.AppPausedBy != null ? $"Paused while {e.AppPausedBy} is in front"
            : e.PausedUntil != null ? "Paused for now"
            : !bl ? "Dimming on screen"
            : s.Dim > DimMath.Boundary ? "Dimming below the laptop's minimum"
            : "Dimming with the backlight";

        BigNum.Text = $"{s.Dim:0}";
        var (hw, f) = DimMath.Map(s.Dim, bl);
        MetaBl.Text = bl ? $"{hw * 100:0}%" : "–";
        MetaSzLabel.Text = bl ? "Sub-zero" : "On screen";
        MetaSz.Text = f < 1 ? $"{(1 - f) * 100:0}%" : "–";
        MetaColour.Text = s.Greyscale ? "Grey" : s.Warmth > 0 ? $"Warm {s.Warmth:0}%" : "Natural";

        DimScale.Value = s.Dim;
        DimScale.Backlight = bl;
        DimScale.Active = e.Active;

        (s.Greyscale ? GreyRadio : s.Warmth > 0 ? WarmRadio : NaturalRadio).IsChecked = true;
        GreyRadio.IsEnabled = !s.UseOverlay;
        GreyRadio.ToolTip = s.UseOverlay ? "Grey works with the colour filter. Change the method in Settings." : null;
        WarmRow.Visibility = WarmRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WarmSlider.Value = s.Warmth;
        WarmValue.Text = $"{s.Warmth:0}%";

        bool paused = e.PausedUntil != null;
        // Always shown so the panel never jumps; only usable while dimming is on
        PauseRow.IsEnabled = e.Active;
        PauseRow.Opacity = e.Active ? 1 : 0.45;
        PausedRow.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        if (paused)
        {
            var left = e.PausedUntil!.Value - DateTime.Now;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            PausedLeft.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
            _countdown.Start();
        }
        else _countdown.Stop();

        ScheduleText.Text = s.ScheduleOn && App.TryTimes(s, out _, out _) ? $"{s.From} → {s.To}" : "Off";
        RulesText.Text = s.AppRules.Count switch { 0 => "None", 1 => "1 app", var n => $"{n} apps" };

        Intro.Visibility = s.SeenIntro ? Visibility.Collapsed : Visibility.Visible;
        ProblemBox.Visibility = e.Problem == null ? Visibility.Collapsed : Visibility.Visible;
        ProblemText.Text = e.Problem;

        BuildPresets();
        _sync = false;
    }

    void BuildPresets()
    {
        PresetPanel.Children.Clear();
        int now = (int)_app.S.Dim;
        foreach (var (name, dim) in App.BuiltIn) PresetPanel.Children.Add(KeyCap(name, dim, false, now));
        foreach (var dim in _app.S.Presets) PresetPanel.Children.Add(KeyCap("Saved", dim, true, now));

        bool known = App.BuiltIn.Any(p => p.Dim == now) || _app.S.Presets.Contains(now);
        bool canSave = !known && now > 0 && _app.S.Presets.Count < 4;
        // Always shown so the panel never jumps; greyed out when there is nothing to save
        SaveBtn.IsEnabled = canSave;
        SaveBtn.Opacity = canSave ? 1 : 0.5;
        SaveText.Text = known ? $"{now}% is already a preset"
            : _app.S.Presets.Count >= 4 ? "Preset slots are full (remove one to save)"
            : $"Save {now}% as a preset";
        AutomationProperties.SetName(SaveBtn, SaveText.Text);
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        int now = (int)_app.S.Dim;
        _app.S.Presets.Add(now);
        _app.S.Presets.Sort();
        _app.S.Save();
        Refresh();
    }

    Button KeyCap(string name, int dim, bool custom, int now)
    {
        bool current = _app.Engine.On && dim == now;
        var b = Tile(name, $"{dim}");
        if (current)
        {
            b.SetResourceReference(BorderBrushProperty, "Nm.Text");
            b.BorderThickness = new Thickness(2);
        }
        AutomationProperties.SetName(b, $"{name}, {dim} percent{(current ? ", in use" : "")}");
        b.Click += (_, _) => _app.ApplyPreset(dim);
        if (custom)
        {
            var remove = new MenuItem { Header = "Remove preset", Style = (Style)FindResource("MenuRow") };
            remove.Click += (_, _) => { _app.S.Presets.Remove(dim); _app.S.Save(); Refresh(); };
            b.ContextMenu = new ContextMenu { Items = { remove }, Style = (Style)FindResource("Menu") };
            b.ToolTip = "Right-click to remove";
        }
        return b;
    }

    /// Keycap: value in mono, name small underneath.
    Button Tile(string name, string value)
    {
        var top = new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = (FontFamily)FindResource("Mono") };
        var label = new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(ForegroundProperty, "Nm.Text2");
        return new Button { Style = (Style)FindResource("Key"), Content = new StackPanel { Children = { top, label } } };
    }

    void OnSwitch_Click(object sender, RoutedEventArgs e) { if (!_sync) _app.Engine.SetOn(OnSwitch.IsChecked == true); }

    void DimScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _app.Engine.SetDim(e.NewValue);
        if (!_app.Engine.On) _app.Engine.SetOn(true); // moving the slider means "show me"
        else if (_app.Engine.PausedUntil != null) _app.Engine.Resume();
    }

    void WarmSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_sync) _app.Engine.SetWarmth(e.NewValue);
    }

    void Colour_Checked(object sender, RoutedEventArgs e)
    {
        if (_sync) return;
        var engine = _app.Engine;
        if (sender == GreyRadio) { engine.SetWarmth(0); engine.SetGreyscale(true); return; }
        if (_app.S.Greyscale) engine.SetGreyscale(false);
        engine.SetWarmth(sender == WarmRadio ? (_app.S.Warmth > 0 ? _app.S.Warmth : 50) : 0);
        if (!IsVisible) return;
        Refresh(); // Warm opens its slider even if nothing else changed
    }

    void Pause_Click(object sender, RoutedEventArgs e) =>
        _app.Engine.Pause(TimeSpan.FromSeconds(int.Parse((string)((Button)sender).Tag)));

    void Resume_Click(object sender, RoutedEventArgs e) => _app.Engine.Resume();

    void Nav_Click(object sender, RoutedEventArgs e) => _app.OpenSettings((string)((Button)sender).Tag);

    void Intro_Click(object sender, RoutedEventArgs e)
    {
        _app.S.SeenIntro = true;
        _app.S.Save();
        Refresh();
    }
}
