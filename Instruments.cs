using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NightMode;

static class Draw
{
    public static void Text(DrawingContext dc, Visual v, string text, double x, double y, double size, string brush,
                            bool mono = false, TextAlignment align = TextAlignment.Left)
    {
        var family = (FontFamily)Application.Current.Resources[mono ? "Mono" : "Ui"];
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size,
            Theme.Brush(brush), VisualTreeHelper.GetDpi(v).PixelsPerDip) { TextAlignment = align };
        dc.DrawText(ft, new Point(x, y));
    }

    public static Pen Pen(Brush b, double w) { var p = new Pen(b, w); p.Freeze(); return p; }

    /// 45° hatching inside a rectangle.
    public static void Hatch(DrawingContext dc, Rect r)
    {
        var pen = Pen(Theme.Brush("Nm.Hatch"), 2);
        dc.PushClip(new RectangleGeometry(r));
        for (double x = r.Left - r.Height; x < r.Right; x += 6)
            dc.DrawLine(pen, new Point(x, r.Bottom), new Point(x + r.Height, r.Top));
        dc.Pop();
    }
}

/// The dim control drawn as a light-meter scale: ticks 0-100, a hatched "sub-zero" band past the
/// laptop minimum and a needle. It is a Slider underneath, so keys and screen readers work as usual.
sealed class Scale : Slider
{
    const double Pad = 7;
    bool _backlight = true, _active = true;

    public bool Backlight { get => _backlight; set { _backlight = value; InvalidateVisual(); } }
    public bool Active { get => _active; set { _active = value; InvalidateVisual(); } }

    public Scale()
    {
        Template = new ControlTemplate(typeof(Scale)) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        Background = Brushes.Transparent;
        Minimum = 0; Maximum = 100; SmallChange = 1; LargeChange = 10;
        IsSnapToTickEnabled = true; TickFrequency = 1;
        Height = 74;
        Cursor = Cursors.SizeWE;
        Focusable = true;
        FocusVisualStyle = ScaleFocus;
        Theme.Changed += InvalidateVisual;
    }

    // ring around the scale only, clear of the keys below
    static readonly Style ScaleFocus = new()
    {
        Setters = { new Setter(Control.TemplateProperty, new ControlTemplate { VisualTree = Ring() }) },
    };
    static FrameworkElementFactory Ring()
    {
        var r = new FrameworkElementFactory(typeof(System.Windows.Shapes.Rectangle));
        r.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 2.0);
        r.SetValue(System.Windows.Shapes.Rectangle.RadiusXProperty, 4.0);
        r.SetValue(System.Windows.Shapes.Rectangle.RadiusYProperty, 4.0);
        r.SetValue(FrameworkElement.MarginProperty, new Thickness(-4, -4, -4, 0));
        r.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Nm.Text");
        return r;
    }

    double X(double v) => Pad + (ActualWidth - 2 * Pad) * v / 100;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // whole area takes the mouse
        var major = Draw.Pen(Theme.Brush("Nm.Text2"), 1);
        var minor = Draw.Pen(Theme.Brush("Nm.LineStrong"), 1);
        var accent = Active ? Theme.Accent : Theme.Brush("Nm.Text3");
        double b = DimMath.Boundary;

        if (Backlight) Draw.Hatch(dc, new Rect(new Point(X(b), 8), new Point(X(100), 34)));
        for (int v = 0; v <= 100; v += 2)
        {
            double x = Math.Round(X(v)) + 0.5;
            dc.DrawLine(v % 10 == 0 ? major : minor, new Point(x, v % 10 == 0 ? 14 : 22), new Point(x, 34));
        }
        dc.DrawLine(minor, new Point(X(0), 34.5), new Point(X(100), 34.5));
        if (Active && Value > 0)
        {
            var fill = new Pen(accent, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(fill, new Point(X(0), 34), new Point(X(Value), 34));
        }
        if (Backlight) dc.DrawLine(Draw.Pen(Theme.Brush("Nm.Text2"), 1.5), new Point(X(b), 2), new Point(X(b), 40));

        foreach (var v in new[] { 0, 20, 40, 60, 80, 100 })
            Draw.Text(dc, this, v.ToString(), X(v), 40, 10, "Nm.Text3", mono: true, align: TextAlignment.Center);
        if (Backlight)
        {
            Draw.Text(dc, this, "Backlight", X(0), 56, 11, "Nm.Text2");
            Draw.Text(dc, this, "Sub-zero, below laptop minimum", X(b) + 6, 56, 11, "Nm.Text2");
        }
        else Draw.Text(dc, this, "Dimmed on screen", X(0), 56, 11, "Nm.Text2");

        double n = Math.Round(X(Value));
        dc.DrawRectangle(accent, null, new Rect(n - 1, 4, 2, 30));
        dc.DrawRoundedRectangle(accent, null, new Rect(n - 6, 0, 12, 8), 2, 2);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
    }

    void SetFrom(MouseEventArgs e) =>
        Value = Math.Round(Math.Clamp((e.GetPosition(this).X - Pad) / (ActualWidth - 2 * Pad) * 100, 0, 100));

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        SetFrom(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) SetFrom(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        e.Handled = true;
    }
}

/// The schedule as a 24-hour strip: the dimmed span filled with the accent, the ease-in hatched, and "now".
sealed class Timeline : FrameworkElement
{
    TimeSpan _from, _to;
    int _ease;
    bool _on;

    public Timeline()
    {
        Height = 54;
        Theme.Changed += InvalidateVisual;
    }

    public void Show(TimeSpan from, TimeSpan to, int easeMinutes, bool on)
    {
        (_from, _to, _ease, _on) = (from, to, easeMinutes, on);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double X(double hours) => w * hours / 24;
        var fill = _on ? Theme.Accent : Theme.Brush("Nm.LineStrong");

        void Span(double a, double b) // hours, may wrap past midnight
        {
            if (b < a) { Span(a, 24); Span(0, b); return; }
            dc.DrawRectangle(fill, null, new Rect(new Point(X(a), 10), new Point(X(b), 28)));
        }
        double f = _from.TotalHours, t = _to.TotalHours;
        if (f != t) Span(f, t);
        if (_ease > 0 && f != t)
        {
            double e = Math.Min(_ease / 60.0, (t - f + 24) % 24);
            double end = (f + e) % 24;
            if (end < f) { Draw.Hatch(dc, new Rect(new Point(X(f), 10), new Point(w, 28))); Draw.Hatch(dc, new Rect(new Point(0, 10), new Point(X(end), 28))); }
            else Draw.Hatch(dc, new Rect(new Point(X(f), 10), new Point(X(end), 28)));
        }
        dc.DrawRectangle(null, Draw.Pen(Theme.Brush("Nm.LineStrong"), 1), new Rect(0.5, 10.5, w - 1, 17));

        double now = Math.Round(X(DateTime.Now.TimeOfDay.TotalHours)) + 0.5;
        dc.DrawLine(Draw.Pen(Theme.Brush("Nm.Text"), 1.5), new Point(now, 4), new Point(now, 34));

        for (int h = 0; h <= 24; h += 3)
            Draw.Text(dc, this, $"{h % 24:00}:00", X(h), 36, 10, "Nm.Text3", mono: true,
                      align: h == 0 ? TextAlignment.Left : h == 24 ? TextAlignment.Right : TextAlignment.Center);
    }
}
