using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace NightMode;

/// Graphite colour tokens for the "Instrument" look. Follows Windows light/dark; the accent comes from
/// Fluent's AccentFillColorDefaultBrush, which already tracks the Windows accent colour.
static class Theme
{
    public static bool Dark { get; private set; }

    public static void Init()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(Apply);
    }

    static void Apply()
    {
        Dark = (Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                    ?.GetValue("AppsUseLightTheme") as int?) == 0;
        var r = Application.Current.Resources;
        void Set(string key, string dark, string light)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Dark ? dark : light));
            b.Freeze();
            r[key] = b;
        }
        Set("Nm.Bg", "#121316", "#F6F6F7");
        Set("Nm.Surface", "#18191D", "#FFFFFF");
        Set("Nm.Raised", "#202227", "#F0F1F3");
        Set("Nm.Line", "#2B2D33", "#E1E2E5");
        Set("Nm.LineStrong", "#41444C", "#C4C6CB");
        Set("Nm.Text", "#ECEDEE", "#16171A");
        Set("Nm.Text2", "#A6A9B0", "#50535A");
        Set("Nm.Text3", "#858991", "#63666D");
        Set("Nm.Danger", "#FF8F86", "#B42318");
        Set("Nm.Hatch", "#2E3037", "#D9DBDF");
        Changed?.Invoke();
    }

    public static event System.Action? Changed;

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static Brush Accent => (Brush)Application.Current.FindResource("AccentFillColorDefaultBrush");
    public static Color Color(string key) => ((SolidColorBrush)Brush(key)).Color;
}
