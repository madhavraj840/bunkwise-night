using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NightMode;

/// Small level indicator shown for 1.5 s when a shortcut changes the level. Never takes focus or clicks.
sealed class Osd : Window
{
    const double BarWidth = 180;
    static Brush Hex(string c) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)); b.Freeze(); return b; }
    readonly TextBlock _num = new() { Width = 56, FontSize = 18, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _zone = new() { FontSize = 11, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Hex("#A6A9B0") };
    readonly Border _fill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _tick = new() { Width = 1, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, Background = Hex("#8A8E96") };
    readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    public Osd()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;

        // always the dark pill, in both themes, so it reads the same over any content
        var mono = (FontFamily)Application.Current.Resources["Mono"];
        _num.FontFamily = _zone.FontFamily = mono;
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Hex("#34363D") };
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        var bar = new Grid { Width = BarWidth, VerticalAlignment = VerticalAlignment.Center, Children = { track, _fill, _tick } };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { _num, bar, _zone } };
        var card = new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 16, 10), Child = row,
            MinWidth = 320, Background = Hex("#F0121316"), BorderBrush = Hex("#34363D"),
        };
        card.SetValue(TextElement.ForegroundProperty, Hex("#ECEDEE"));
        Content = card;

        _hide.Tick += (_, _) => { _hide.Stop(); Hide(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        const int GWL_EXSTYLE = -20;
        Native.SetWindowLongPtr(hwnd, GWL_EXSTYLE, Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE) | 0x20 | 0x80 | 0x08000000); // click-through, no Alt+Tab, no focus
    }

    public void Flash(bool on, double dim, bool backlight)
    {
        _num.Text = on ? $"{dim:0}%" : "Off";
        _fill.Width = on ? BarWidth * dim / 100 : 0;
        _tick.Visibility = backlight ? Visibility.Visible : Visibility.Collapsed;
        _tick.Margin = new Thickness(BarWidth * DimMath.Boundary / 100, 0, 0, 0);
        _zone.Text = !on ? "FULL" : !backlight ? "DIMMED" : dim > DimMath.Boundary ? "SUB-ZERO" : "BACKLIGHT";
        System.Windows.Automation.AutomationProperties.SetName(this, $"Night mode {_num.Text}, {_zone.Text.ToLower()}");

        Show();
        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Bottom - ActualHeight - 56;
        _hide.Stop();
        _hide.Start();
    }
}
