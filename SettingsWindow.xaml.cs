using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace NightMode;

public partial class SettingsWindow : Window
{
    readonly App _app = App.Me;
    bool _sync = true;

    static readonly (string Label, int Minutes)[] EaseChoices = { ("Off", 0), ("10 min", 10), ("20 min", 20), ("45 min", 45) };

    public SettingsWindow()
    {
        InitializeComponent();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
        var s = _app.S;
        var e = _app.Engine;

        BacklightSwitch.IsEnabled = e.HasBacklight;
        BacklightSwitch.IsChecked = e.UsingBacklight;
        BacklightHelp.Text = e.HasBacklight
            ? "The first 40% of the scale turns the real backlight down, which also saves battery. The rest dims on screen, below the laptop's minimum."
            : "Apps can't control this screen's backlight, so Night mode dims on screen only.";
        (s.UseOverlay ? OverlayRadio : FilterRadio).IsChecked = true;
        RememberSwitch.IsChecked = s.RememberOn;
        StartupSwitch.IsChecked = _app.StartsWithWindows;

        ScheduleSwitch.IsChecked = s.ScheduleOn;
        FromBox.Text = s.From;
        ToBox.Text = s.To;
        foreach (var (label, minutes) in EaseChoices)
        {
            var r = new RadioButton
            {
                Content = label, GroupName = "Ease", Style = (Style)FindResource("SegChoice"),
                IsChecked = minutes == s.EaseMinutes, Tag = minutes,
            };
            AutomationProperties.SetName(r, minutes == 0 ? "Ease in off" : $"Ease in over {minutes} minutes");
            r.Checked += Schedule_Changed;
            EasePanel.Children.Add(r);
        }
        if (EasePanel.Children.OfType<RadioButton>().All(r => r.IsChecked != true)) ((RadioButton)EasePanel.Children[2]).IsChecked = true;
        LevelSlider.Value = s.ScheduleDim;
        LevelValue.Text = $"{s.ScheduleDim:0}%";
        DrawStrip();

        ShowRules();
        ShortcutList.ItemsSource = _app.Shortcuts;
        VersionText.Text = $"Night mode {Assembly.GetEntryAssembly()!.GetName().Version!.ToString(3)}";
        _sync = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        void Caption()
        {
            Native.Dwm(hwnd, 20, Theme.Dark ? 1 : 0);                          // DWMWA_USE_IMMERSIVE_DARK_MODE
            Native.Dwm(hwnd, 35, Native.ColorRef(Theme.Color("Nm.Bg")));      // DWMWA_CAPTION_COLOR
            Native.Dwm(hwnd, 36, Native.ColorRef(Theme.Color("Nm.Text2")));   // DWMWA_TEXT_COLOR
        }
        Caption();
        Theme.Changed += Caption;
        Closed += (_, _) => Theme.Changed -= Caption;
    }

    public void ShowPage(string page) =>
        Pages.SelectedItem = Pages.Items.OfType<TabItem>().FirstOrDefault(t => (string)t.Tag == page) ?? Pages.Items[0];

    // ---------- dimming ----------

    void Backlight_Click(object sender, RoutedEventArgs e)
    {
        if (_sync) return;
        _app.S.UseBacklight = BacklightSwitch.IsChecked == true;
        _app.S.Save();
        _app.Engine.Reapply();
    }

    void Method_Checked(object sender, RoutedEventArgs e)
    {
        if (!_sync) _app.Engine.SetOverlay(OverlayRadio.IsChecked == true);
    }

    void Remember_Click(object sender, RoutedEventArgs e)
    {
        if (_sync) return;
        _app.S.RememberOn = RememberSwitch.IsChecked == true;
        _app.S.Save();
    }

    void Startup_Click(object sender, RoutedEventArgs e) { if (!_sync) _app.StartsWithWindows = StartupSwitch.IsChecked == true; }

    // ---------- schedule ----------

    void Time_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Time_Commit(sender, e);
    }

    void Time_Commit(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        var t = DimMath.ParseTime(box.Text);
        if (t == null)
        {
            TimeError.Text = $"\"{box.Text}\" isn't a time. Use the 24-hour clock, like 22:00.";
            TimeError.Visibility = Visibility.Visible;
            return;
        }
        box.Text = t;
        Schedule_Changed(sender, e);
    }

    void Schedule_Changed(object sender, RoutedEventArgs e)
    {
        if (_sync) return;
        var s = _app.S;
        s.ScheduleOn = ScheduleSwitch.IsChecked == true;
        s.From = DimMath.ParseTime(FromBox.Text) ?? s.From;
        s.To = DimMath.ParseTime(ToBox.Text) ?? s.To;
        s.ScheduleDim = LevelSlider.Value;
        s.EaseMinutes = (int)(EasePanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag ?? 20);
        LevelValue.Text = $"{s.ScheduleDim:0}%";
        bool same = s.From == s.To;
        TimeError.Text = "Start and end can't be the same time.";
        TimeError.Visibility = same ? Visibility.Visible : Visibility.Collapsed;
        s.Save();
        _app.ApplySchedule(true);
        DrawStrip();
    }

    void DrawStrip()
    {
        var s = _app.S;
        TimeSpan.TryParse(s.From, out var from);
        TimeSpan.TryParse(s.To, out var to);
        Strip.Show(from, to, s.EaseMinutes, s.ScheduleOn);
    }

    // ---------- app rules ----------

    void ShowRules()
    {
        RuleList.ItemsSource = _app.S.AppRules.Select(a => new
        {
            Name = a.Length > 0 ? char.ToUpper(a[0]) + a[1..] : a,
            Exe = a + ".exe",
            Key = a,
            Mono = a.Length >= 2 ? char.ToUpper(a[0]) + a[1..2] : a.ToUpper(),
            RemoveName = $"Remove {a}",
        }).ToList();
        NoRules.Visibility = _app.S.AppRules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var last = _app.LastApp;
        bool can = last != null && !_app.S.AppRules.Contains(last, StringComparer.OrdinalIgnoreCase);
        AddRule.Content = last == null ? "Add the app you were using" : $"Add {last}, the app you were using";
        AddRule.IsEnabled = can;
    }

    void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (_app.LastApp is { } app) _app.SetRule(app, true);
        ShowRules();
    }

    void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        _app.SetRule((string)((Button)sender).Tag, false);
        ShowRules();
    }
}
