using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChickenDesktopPet3D;

internal static class ToolbarPreview
{
    public static int Run(string? output)
    {
        var directory = Path.GetFullPath(output ?? "research/toolbar-preview");
        Directory.CreateDirectory(directory);
        var bar = new QuickActionBar();
        bar.SetActions(new HashSet<string> { "feed", "trick", "sleep", "react" }, false);
        bar.Visibility = Visibility.Visible;
        var grid = new Grid { Width = 240, Height = 64 };
        grid.Children.Add(bar);
        grid.Measure(new Size(240, 64)); grid.Arrange(new Rect(0, 0, 240, 64)); grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap(240, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(directory, "toolbar.png"))) encoder.Save(file);
        var clicked = new List<string>();
        bar.ActionRequested += clicked.Add;
        foreach (Button button in ((StackPanel)bar.Child).Children) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!clicked.SequenceEqual(new[] { "feed", "trick", "sleep", "wake" })) return 1;
        bar.SetActions(new HashSet<string> { "trick" }, true);
        if (((StackPanel)bar.Child).Children.Count != 1) return 1;
        bar.SetActions(new HashSet<string>(), false);
        if (bar.HasActions || bar.Visibility != Visibility.Collapsed) return 1;

        // Exercise placement in a real layered HWND, including an old saved
        // position extending beyond the desktop edge. Keep user settings isolated.
        var oldSettings = Environment.GetEnvironmentVariable("CHICK_SETTINGS_PATH");
        Environment.SetEnvironmentVariable("CHICK_SETTINGS_PATH", Path.Combine(directory, "test-settings.json"));
        using var renderer = new ChickRenderer();
        var window = new PetWindow(renderer) { ShowActivated = false };
        try
        {
            window.Show(); // Opacity remains zero because no render thread is started.
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var strip = (QuickActionBar)typeof(PetWindow).GetField("quickActions", flags)!.GetValue(window)!;
            strip.SetActions(new HashSet<string> { "feed", "trick", "sleep", "react" }, false);
            foreach (var size in new[] { 240, 480 })
            {
                window.Width = window.Height = size;
                window.Left = SystemParameters.WorkArea.Right - size + 90;
                window.Top = SystemParameters.WorkArea.Bottom - size + 65;
                strip.Visibility = Visibility.Visible;
                typeof(PetWindow).GetMethod("PositionQuickActions", flags)!.Invoke(window, null);
                window.UpdateLayout();
                var origin = strip.TranslatePoint(new Point(), window);
                if (window.Left + origin.X < SystemParameters.WorkArea.Left ||
                    window.Left + origin.X + strip.ActualWidth > SystemParameters.WorkArea.Right + 1 ||
                    window.Top + origin.Y < SystemParameters.WorkArea.Top ||
                    window.Top + origin.Y + strip.ActualHeight > SystemParameters.WorkArea.Bottom + 1)
                    return 1;
            }
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("CHICK_SETTINGS_PATH", oldSettings);
        }
        Console.WriteLine("PASS toolbar actions and screen-edge placement");
        return 0;
    }
}
