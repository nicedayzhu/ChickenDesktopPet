using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChickenDesktopPet3D;

internal static class PanoramaTheme
{
    public static readonly Brush Panel = Frozen("#F52F3133");
    public static readonly Brush Border = Frozen("#545657");
    public static readonly Brush Text = Frozen("#F0F0EE");
    public static readonly Brush Muted = Frozen("#B0B2B0");
    public static readonly Brush Accent = Frozen("#E7D8A4");
    public static readonly FontFamily Font = new("Bahnschrift, Microsoft YaHei UI, Segoe UI");

    public static ResourceDictionary Create() => new()
    {
        Source = new Uri("/ChickenDesktopPet3D;component/PanoramaTheme.xaml", UriKind.Relative),
    };

    public static void Apply(FrameworkElement control) => control.Resources.MergedDictionaries.Add(Create());

    public static TextBlock Label(string text, double size = 13, bool muted = false) => new()
    {
        Text = text, FontSize = size, FontFamily = Font, Foreground = muted ? Muted : Text,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Button IconButton(PetIconStore icons, string icon, string tip, Action clicked, double size = 38)
    {
        var button = new Button { Content = icons.Create(icon), Width = size, Height = size,
            Padding = new Thickness(5), ToolTip = tip, Focusable = false };
        button.Click += (_, _) => clicked();
        return button;
    }

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze(); return brush;
    }
}
