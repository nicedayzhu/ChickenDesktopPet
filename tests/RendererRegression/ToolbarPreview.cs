using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ChickenDesktopPet3D;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;

internal static class ToolbarPreview
{
    public static int Run(string? output)
    {
        var directory = Path.GetFullPath(output ?? "research/panorama-preview");
        Directory.CreateDirectory(directory);
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var game = GameFolderLocator.FindSteamGameByAppId(730);
        var vpk = Environment.GetEnvironmentVariable("CHICK_CS2_VPK") ?? Path.Combine(game!.Value.GamePath, "game/csgo/pak01_dir.vpk");
        using var package = new Package(); package.Read(vpk);
        using var loader = new GameFileLoader(package, vpk);
        var resources = PetUiResources.Load(package, loader);
        var catalog = new PetCatalog(package, loader, resources);
        var icons = new PetIconStore(); icons.Load(resources);
        Console.WriteLine($"Official UI: {resources.Activities.Count} activities, {resources.GraphClips.Count} graph mappings, {icons.OfficialIconCount}/{resources.Icons.Count} icons");
        foreach (var error in icons.DecodeErrors) Console.Error.WriteLine($"{error.Key}: {error.Value}");
        if (resources.Activities.Count != 7 || resources.GraphClips.Count < 7 || icons.OfficialIconCount < 7) return 1;

        var available = resources.Activities.Select(item => item.Id).Concat(new[] { "idle", "feed", "trick", "sleep", "react" }).ToHashSet();
        var bar = new QuickActionBar(icons); bar.SetResources(resources); bar.SetActions(available, false);
        bar.Visibility = Visibility.Visible;
        var clicked = new List<string>(); bar.ActionRequested += clicked.Add;
        var primary = (StackPanel)((StackPanel)bar.Child).Children[1];
        foreach (var index in new[] { 0, 2, 3, 4 }) ((Button)primary.Children[index]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!clicked.SequenceEqual(new[] { "feed", "sleep", "inspect", "photo" })) return 1;
        ((Button)primary.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var named = (UniformGrid)((StackPanel)bar.Child).Children[2];
        clicked.Clear();
        foreach (Button button in named.Children) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!clicked.SequenceEqual(resources.Activities.Select(item => item.Id))) return 1;
        bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        bar.Arrange(new Rect(bar.DesiredSize)); bar.UpdateLayout(); Save(bar, Path.Combine(directory, "toolbar.png"));
        bar.SetState("fly");
        if (((Button)primary.Children[0]).IsEnabled || named.Children.Cast<Button>().Any(button => button.IsEnabled)) return 1;
        bar.SetState("sleep"); clicked.Clear();
        ((Button)primary.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!clicked.SequenceEqual(new[] { "wake" })) return 1;

        var sample = CaptureSample();
        var oldPhotos = Environment.GetEnvironmentVariable("CHICK_PHOTO_PATH");
        var oldSettings = Environment.GetEnvironmentVariable("CHICK_SETTINGS_PATH");
        Environment.SetEnvironmentVariable("CHICK_PHOTO_PATH", Path.Combine(directory, "photos"));
        Environment.SetEnvironmentVariable("CHICK_SETTINGS_PATH", Path.Combine(directory, "test-settings.json"));
        try
        {
            var library = new PhotoLibrary();
            var inspect = new InspectWindow(sample, icons, resources.Activities, library) { ShowActivated = false, Opacity = 0 };
            inspect.SetCatalog(catalog.Appearances); inspect.SetAppearance(catalog.Find("chick")!, available, "小鸡");
            inspect.Show(); inspect.UpdateLayout();
            Save((FrameworkElement)inspect.Content, Path.Combine(directory, "inspector.png"));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            VerifyInspector(inspect, sample, flags);
            var backgrounds = (ComboBox)typeof(InspectWindow).GetField("backgrounds", flags)!.GetValue(inspect)!;
            typeof(InspectWindow).GetMethod("SelectPanel", flags)!.Invoke(inspect, new object[] { 1 });
            inspect.UpdateLayout(); Save((FrameworkElement)inspect.Content, Path.Combine(directory, "inspector-photo.png"));
            inspect.Width = 820; inspect.Height = 620; inspect.UpdateLayout();
            Save((FrameworkElement)inspect.Content, Path.Combine(directory, "inspector-small.png"));
            inspect.Width = 1080; inspect.Height = 800; inspect.UpdateLayout();
            backgrounds.SelectedIndex = 3;
            var transparent = inspect.CapturePhoto();
            var bytes = new byte[512 * 512 * 4]; transparent.CopyPixels(bytes, 512 * 4, 0);
            if (bytes[3] != 0 || !bytes.Where((_, i) => i % 4 == 3).Any(value => value > 240)) return 1;
            var file = library.SaveAsync(transparent, "chick", "小鸡", "透明 PNG").GetAwaiter().GetResult();
            backgrounds.SelectedIndex = 1;
            inspect.UpdateLayout(); Save((FrameworkElement)inspect.Content, Path.Combine(directory, "inspector-light.png"));
            var solid = inspect.CapturePhoto(); solid.CopyPixels(bytes, 512 * 4, 0);
            if (bytes[3] != 255 || !File.Exists(file) || library.ReadRecent().Count == 0) return 1;
            var saveName = (Button)typeof(InspectWindow).GetField("saveName", flags)!.GetValue(inspect)!;
            var name = (TextBox)typeof(InspectWindow).GetField("nameEntry", flags)!.GetValue(inspect)!;
            string? renamed = null; inspect.NameRequested += value => renamed = value;
            name.Text = "测试小鸡"; saveName.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (renamed != "测试小鸡") return 1;
            var basics = (Dictionary<string, Button>)typeof(InspectWindow).GetField("basicButtons", flags)!.GetValue(inspect)!;
            inspect.SetAppearance(catalog.Find("chicknegg")!, new HashSet<string> { "trick", "sleep", "idle" }, "破壳蛋");
            string? eggAction = null; inspect.ActionRequested += value => eggAction = value;
            basics["feed"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (eggAction != "trick") return 1;
            inspect.Close();

            using var renderer = new ChickRenderer();
            var window = new PetWindow(renderer) { ShowActivated = false };
            window.Show();
            ((DispatcherTimer)typeof(PetWindow).GetField("timer", flags)!.GetValue(window)!).Stop();
            var strip = (QuickActionBar)typeof(PetWindow).GetField("quickActions", flags)!.GetValue(window)!;
            var popup = (Popup)typeof(PetWindow).GetField("actionPopup", flags)!.GetValue(window)!;
            strip.SetActions(available, false); strip.Opacity = 0;
            foreach (var size in new[] { 240, 480 })
            {
                foreach (var corner in new[] { 0, 1, 2, 3 })
                {
                    window.Width = window.Height = size;
                    var area = SystemParameters.WorkArea;
                    window.Left = corner % 2 == 0 ? area.Left - 80 : area.Right - size + 90;
                    window.Top = corner < 2 ? area.Top - 60 : area.Bottom - size + 65;
                    strip.Visibility = Visibility.Visible;
                    typeof(PetWindow).GetMethod("PositionQuickActions", flags)!.Invoke(window, null);
                    popup.IsOpen = true; Pump(); strip.UpdateLayout();
                    var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(window).Handle).WorkingArea;
                    var origin = strip.PointToScreen(new Point());
                    var end = strip.PointToScreen(new Point(strip.ActualWidth, strip.ActualHeight));
                    if (origin.X < screen.Left || end.X > screen.Right + 1 || origin.Y < screen.Top || end.Y > screen.Bottom + 1)
                    { Console.Error.WriteLine($"Popup outside work area: {origin} {end}"); return 1; }
                    popup.IsOpen = false;
                }
            }
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CHICK_PHOTO_PATH", oldPhotos);
            Environment.SetEnvironmentVariable("CHICK_SETTINGS_PATH", oldSettings);
        }
        Console.WriteLine("PASS official assets, named UI actions, sleep/wake, egg actions, photos, naming, and 8 screen-edge placements");
        return 0;
    }

    private static void VerifyInspector(InspectWindow inspect, BitmapSource sample, BindingFlags flags)
    {
        var pixels = new byte[512 * 512 * 4]; sample.CopyPixels(pixels, 512 * 4, 0);
        var opaque = Enumerable.Range(0, 512 * 512).First(i => pixels[i * 4 + 3] > 240);
        if (inspect.HitsPet(new Point(0, 0)) || !inspect.HitsPet(new Point(opaque % 512, opaque / 512)))
            throw new Exception("Model orbit and empty window drag surfaces overlap");
        var lastZoom = 1f; inspect.ZoomRequested += value => lastZoom = value;
        var zoomOut = (Button)typeof(InspectWindow).GetField("zoomOut", flags)!.GetValue(inspect)!;
        var zoomIn = (Button)typeof(InspectWindow).GetField("zoomIn", flags)!.GetValue(inspect)!;
        zoomOut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Math.Abs(lastZoom - .9f) > .001) throw new Exception("Zoom out did not decrease magnification");
        zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Math.Abs(lastZoom - 1f) > .001) throw new Exception("Zoom in did not increase magnification");
        typeof(InspectWindow).GetMethod("SetZoom", flags)!.Invoke(inspect, new object[] { .1f });
        if (zoomOut.IsEnabled || !zoomIn.IsEnabled) throw new Exception("Lower zoom limit has wrong controls");
        typeof(InspectWindow).GetMethod("SetZoom", flags)!.Invoke(inspect, new object[] { 3f });
        if (!zoomOut.IsEnabled || zoomIn.IsEnabled) throw new Exception("Upper zoom limit has wrong controls");
        typeof(InspectWindow).GetMethod("SetZoom", flags)!.Invoke(inspect, new object[] { 1f });
        var hwnd = new System.Windows.Interop.WindowInteropHelper(inspect).Handle;
        var title = inspect.PointToScreen(new Point(130, 40));
        if (NativeHit(hwnd, title) != 2) throw new Exception("Title does not expose a native window drag surface");
        if (!GetClientRect(hwnd, out var client) || !GetWindowRect(hwnd, out var window) ||
            Math.Abs((window.Bottom - window.Top) - (client.Bottom - client.Top)) > 2)
            throw new Exception("Native frame still consumes space above the client area");
        Console.WriteLine("PASS native caption dragging, full client frame, model hit area, zoom directions and limits");
    }

    private static long NativeHit(nint hwnd, Point point)
    {
        var packed = unchecked((int)(((ushort)(int)point.Y << 16) | (ushort)(int)point.X));
        return SendMessage(hwnd, 0x0084, 0, (nint)packed).ToInt64();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static BitmapSource CaptureSample()
    {
        using var renderer = new ChickRenderer();
        BitmapSource? result = null;
        Exception? error = null;
        var frames = 0;
        using var timeout = new System.Threading.Timer(_ => renderer.Dispose(), null, TimeSpan.FromSeconds(60), System.Threading.Timeout.InfiniteTimeSpan);
        renderer.Failed += ex => { error = ex; renderer.Dispose(); };
        renderer.FrameReady += pixels =>
        {
            try
            {
                if (++frames != 20) return;
                result = BitmapSource.Create(512, 512, 96, 96, PixelFormats.Pbgra32, null, pixels, 512 * 4);
                result.Freeze(); renderer.Dispose();
            }
            finally { renderer.FrameConsumed(); }
        };
        renderer.RunOnCurrentThread();
        return result ?? throw new InvalidOperationException("Could not capture a live chick", error);
    }

    private static void Save(FrameworkElement element, string file)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); png.Save(stream);
    }
}
