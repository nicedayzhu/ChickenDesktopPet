using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Image = System.Windows.Controls.Image;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ChickenDesktopPet3D;

internal sealed class PetWindow : Window
{
    private sealed class Settings
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double Size { get; set; } = 360;
        public bool AlwaysOnTop { get; set; } = true;
        public bool Roam { get; set; } = true;
        public bool LowPower { get; set; }
        public float CameraAngle { get; set; } = 30;
        public string AppearanceId { get; set; } = PetCatalog.DefaultAppearanceId;
    }

    private readonly string settingsPath = Environment.GetEnvironmentVariable("CHICK_SETTINGS_PATH") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChickenDesktopPet", "settings3d.json");
    private readonly Settings settings;
    private readonly ChickRenderer renderer;
    // MSAA resolves the alpha-tested fur against transparent black, producing premultiplied color.
    // WPF must not multiply that color by alpha a second time when composing the layered window.
    private readonly WriteableBitmap bitmap = new(ChickRenderer.Resolution, ChickRenderer.Resolution, 96, 96, PixelFormats.Pbgra32, null);
    private readonly Image image = new() { Stretch = Stretch.Fill, RenderTransformOrigin = new Point(.5, .5) };
    private readonly Grid layout = new();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Random random = new();
    private readonly Forms.NotifyIcon tray;
    private IReadOnlyList<PetAppearance> appearances = [];
    private PetAppearance? selectedAppearance;
    private HashSet<string> availableActions = new(StringComparer.Ordinal);
    private byte[]? hitFrame;
    private string current = "idle";
    private double actionStarted;
    private double nextRoam;
    private double lastTick;
    private double walkRemaining;
    private int walkDirection = 1;
    private bool dragging;
    private Point pressPoint;
    private bool closing;
    private bool captured;
    private int framesReceived;
    private int framesThisSecond;
    private double nextFpsSample = 1;

    public PetWindow(ChickRenderer renderer)
    {
        this.renderer = renderer;
        settings = LoadSettings();
        Title = "CS2 小鸡桌宠 · 实时 3D";
        Width = Height = Math.Clamp(settings.Size, 240, 480);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = settings.AlwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea;
        var defaultLeft = area.Right - Width + 60;
        var defaultTop = area.Bottom - Height + 65;
        Left = settings.Left ?? defaultLeft;
        Top = settings.Top ?? defaultTop;
        var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visiblePart = Rect.Intersect(virtualScreen, new Rect(Left, Top, Width, Height));
        if (visiblePart.IsEmpty || visiblePart.Width < 100 || visiblePart.Height < 100)
        {
            Left = defaultLeft;
            Top = defaultTop;
        }
        Opacity = 0;
        image.Source = bitmap;
        var shadowBrush = new RadialGradientBrush();
        shadowBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(55, 0, 0, 0), 0));
        shadowBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 1));
        layout.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 120,
            Height = 22,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(-20, 0, 0, 59),
            Fill = shadowBrush,
            IsHitTestVisible = false,
        });
        layout.Children.Add(image);
        Content = layout;

        image.MouseLeftButtonDown += OnMouseDown;
        image.MouseLeftButtonUp += OnMouseUp;
        image.MouseMove += OnMouseMove;
        image.MouseRightButtonUp += (_, _) => OpenMenu();
        image.MouseWheel += (_, e) => { RotateCamera(e.Delta > 0 ? 15 : -15); e.Handled = true; };
        SourceInitialized += (_, _) => ((HwndSource)PresentationSource.FromVisual(this)).AddHook(HitTestHook);

        tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath
                ?? throw new InvalidOperationException("无法获取程序图标")),
            Text = "CS2 小鸡桌宠 · 实时 3D",
            Visible = true,
        };
        tray.MouseClick += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            if (e.Button == Forms.MouseButtons.Left) { Activate(); Play("react"); }
            else if (e.Button == Forms.MouseButtons.Right) OpenMenu();
        });

        renderer.FrameReady += OnFrame;
        renderer.AppearancesReady += models => Dispatcher.BeginInvoke(() =>
        {
            if (!closing) appearances = models;
        });
        renderer.AppearanceChanged += (model, actions) => Dispatcher.BeginInvoke(() =>
        {
            if (closing) return;
            selectedAppearance = model;
            availableActions = new HashSet<string>(actions, StringComparer.Ordinal);
            settings.AppearanceId = model.Id;
            current = "idle";
            walkRemaining = 0;
            Title = $"CS2 鸡桌宠 · {model.GroupLabel}";
            SaveSettings();
        });
        renderer.AppearanceFailed += (id, error) => Dispatcher.BeginInvoke(() =>
        {
            if (closing || selectedAppearance is null) return;
            System.Windows.MessageBox.Show(this, $"无法切换到 {id}：{error.Message}",
                "宠物外观不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
        renderer.Failed += ex => Dispatcher.BeginInvoke(() =>
        {
            if (closing) return;
            System.Windows.MessageBox.Show(this, ex.Message, "小鸡加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        });
        renderer.LowPower = settings.LowPower;
        if (settings.CameraAngle != 0) renderer.Orbit(settings.CameraAngle);
        renderer.SelectAppearance(settings.AppearanceId ?? PetCatalog.DefaultAppearanceId);
        ErrorLog.Trace("WPF window constructed");
        ScheduleRoam();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Closed += (_, _) =>
        {
            closing = true;
            timer.Stop();
            tray.Dispose();
            SaveSettings();
            renderer.Dispose();
        };
    }

    private Settings LoadSettings()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    private void SaveSettings()
    {
        try
        {
            settings.Left = Left;
            settings.Top = Top;
            settings.Size = Width;
            settings.AlwaysOnTop = Topmost;
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
        }
        catch (IOException ex) { ErrorLog.Write(ex); }
        catch (UnauthorizedAccessException ex) { ErrorLog.Write(ex); }
    }

    private void OnFrame(byte[] pixels)
    {
        if (hitFrame is null) ErrorLog.Trace("WPF frame received");
        if (Dispatcher.HasShutdownStarted) { renderer.FrameConsumed(); return; }
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (!closing)
                {
                    bitmap.WritePixels(new Int32Rect(0, 0, ChickRenderer.Resolution, ChickRenderer.Resolution),
                        pixels, ChickRenderer.Resolution * 4, 0);
                    hitFrame = pixels;
                    framesReceived++;
                    framesThisSecond++;
                    if (Opacity == 0) Opacity = 1;
                    var capturePath = Environment.GetEnvironmentVariable("CHICK_CAPTURE_FRAME");
                    if (!captured && framesReceived >= 30 && !string.IsNullOrWhiteSpace(capturePath))
                    {
                        captured = true;
                        Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                var preview = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
                                preview.Render(layout);
                                var encoder = new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(preview));
                                using var file = File.Create(capturePath);
                                encoder.Save(file);
                                ErrorLog.Trace("WPF preview saved");
                            }
                            catch (Exception ex) { ErrorLog.Write(ex); }
                        }, DispatcherPriority.ContextIdle);
                    }
                }
            }
            finally { renderer.FrameConsumed(); }
        }, DispatcherPriority.Render);
    }

    private nint HitTestHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != 0x84 || hitFrame is null) return 0;
        var packed = lParam.ToInt64();
        var point = PointFromScreen(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
        var x = (int)(point.X * ChickRenderer.Resolution / ActualWidth);
        var y = (int)(point.Y * ChickRenderer.Resolution / ActualHeight);
        if (image.RenderTransform is ScaleTransform { ScaleX: < 0 })
            x = ChickRenderer.Resolution - 1 - x;
        if (x < 0 || y < 0 || x >= ChickRenderer.Resolution || y >= ChickRenderer.Resolution ||
            hitFrame[(y * ChickRenderer.Resolution + x) * 4 + 3] < 18)
        {
            handled = true;
            return -1;
        }
        return 0;
    }

    private void Play(string name)
    {
        if (!availableActions.Contains(name)) return;
        current = name;
        actionStarted = clock.Elapsed.TotalSeconds;
        renderer.Play(name, name is "idle" or "idle2" or "squat" or "walk" or "sleep");
    }

    private void PlayOneOf(params string[] choices)
    {
        var playable = choices.Where(availableActions.Contains).ToArray();
        if (playable.Length > 0) Play(playable[random.Next(playable.Length)]);
    }

    private void Tick()
    {
        var now = clock.Elapsed.TotalSeconds;
        if (now >= nextFpsSample)
        {
            if (Environment.GetEnvironmentVariable("CHICK_FPS_LOG") is { Length: > 0 } logPath)
            {
                try { File.AppendAllText(logPath, $"{now:F1},{current},{framesThisSecond}\n"); }
                catch (IOException) { }
            }
            framesThisSecond = 0;
            nextFpsSample = now + 1;
        }
        var dt = Math.Clamp(now - lastTick, 0, .1);
        lastTick = now;
        if (walkRemaining > 0 && !dragging)
        {
            var area = SystemParameters.WorkArea;
            var step = Math.Min(walkRemaining, dt * 45);
            Left = Math.Clamp(Left + walkDirection * step, area.Left - 40, area.Right - Width + 90);
            walkRemaining -= step;
            if (walkRemaining < .01) { walkRemaining = 0; Play("idle"); SaveSettings(); }
        }
        else if (current is "react" or "react2" or "trick" or "trick2" or "feed")
        {
            if (now - actionStarted > 4.5) Play("idle");
        }
        else if (settings.Roam && availableActions.Contains("walk") && current == "idle" && now >= nextRoam)
        {
            walkDirection = random.Next(2) == 0 ? -1 : 1;
            image.RenderTransform = new ScaleTransform(walkDirection, 1);
            walkRemaining = random.Next(65, 170);
            Play("walk");
            ScheduleRoam();
        }
        else if (current == "idle" && availableActions.Contains("idle2") && now - actionStarted > 12 && random.NextDouble() < dt * .04)
            Play("idle2");
        else if (current == "idle2" && now - actionStarted > 8)
            Play("idle");
    }

    private void ScheduleRoam() => nextRoam = clock.Elapsed.TotalSeconds + random.Next(25, 55);

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        pressPoint = e.GetPosition(this);
        dragging = false;
        if (e.ClickCount >= 2)
        {
            walkRemaining = 0;
            PlayOneOf("trick", "trick2");
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragging) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - pressPoint.X) + Math.Abs(point.Y - pressPoint.Y) < 8) return;
        dragging = true;
        walkRemaining = 0;
        try { DragMove(); }
        catch (InvalidOperationException) { }
        finally { SaveSettings(); }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging && e.ClickCount == 1)
        {
            walkRemaining = 0;
            PlayOneOf("react", "react2");
        }
        dragging = false;
    }

    private void OpenMenu()
    {
        Activate();
        BuildMenu().IsOpen = true;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        MenuItem Add(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        var appearanceMenu = new MenuItem { Header = "选择宠物" };
        foreach (var group in appearances.GroupBy(item => item.ModelId))
        {
            var groupItem = new MenuItem { Header = group.First().GroupLabel };
            foreach (var appearance in group)
            {
                var option = new MenuItem
                {
                    Header = appearance.Label,
                    IsCheckable = true,
                    IsChecked = selectedAppearance?.Id == appearance.Id,
                };
                option.Click += (_, _) => renderer.SelectAppearance(appearance.Id);
                groupItem.Items.Add(option);
            }
            appearanceMenu.Items.Add(groupItem);
        }
        appearanceMenu.IsEnabled = appearances.Count > 0;
        menu.Items.Add(appearanceMenu);
        menu.Items.Add(new Separator());

        Add("喂食", () => { walkRemaining = 0; Play("feed"); }).IsEnabled = availableActions.Contains("feed");
        Add("表演", () => { walkRemaining = 0; PlayOneOf("trick", "trick2"); }).IsEnabled =
            availableActions.Contains("trick") || availableActions.Contains("trick2");
        Add("睡觉", () => { walkRemaining = 0; Play("sleep"); }).IsEnabled = availableActions.Contains("sleep");
        Add("叫醒", () => PlayOneOf("react", "react2")).IsEnabled =
            availableActions.Contains("react") || availableActions.Contains("react2");
        menu.Items.Add(new Separator());
        Add(settings.Roam ? "停止散步" : "允许散步", () => { settings.Roam = !settings.Roam; walkRemaining = 0; Play("idle"); SaveSettings(); })
            .IsEnabled = availableActions.Contains("walk");
        Add(settings.LowPower ? "关闭省电模式" : "开启省电模式", () => { settings.LowPower = !settings.LowPower; renderer.LowPower = settings.LowPower; SaveSettings(); });
        Add(Topmost ? "取消置顶" : "始终置顶", () => { Topmost = !Topmost; SaveSettings(); });
        Add("缩小", () => ResizePet(-32));
        Add("放大", () => ResizePet(32));
        Add("视角左转", () => RotateCamera(-30));
        Add("视角右转", () => RotateCamera(30));
        menu.Items.Add(new Separator());
        Add("作者：niceday_zhu · GitHub", () => OpenUrl("https://github.com/nicedayzhu"));
        Add("Powered by Source 2 Viewer / VRF", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://s2v.app") { UseShellExecute = true }));
        Add("开源许可与第三方声明", ShowLicense);
        Add("退出", Close);
        return menu;
    }

    private void ShowLicense()
    {
        static string ReadResource(string name)
        {
            using var stream = typeof(PetWindow).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidDataException($"未找到内置文档：{name}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        var details = $"CS2 鸡桌宠 · 作者 niceday_zhu\nhttps://github.com/nicedayzhu\n\n"
            + ReadResource("ProjectLicense") + "\n\n"
            + ReadResource("ThirdPartyNotices") + "\n\n"
            + "ValveResourceFormat 许可原文\n\n" + ReadResource("ValveResourceFormatLicense");
        var dialog = new Window
        {
            Title = "开源许可与第三方声明",
            Owner = this,
            Width = 660,
            Height = 560,
            MinWidth = 420,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new System.Windows.Controls.TextBox
            {
                Text = details,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16),
            },
        };
        dialog.ShowDialog();
    }

    private static void OpenUrl(string url) => System.Diagnostics.Process.Start(
        new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    private void ResizePet(int amount)
    {
        Width = Height = Math.Clamp(Width + amount, 240, 480);
        SaveSettings();
    }

    private void RotateCamera(float degrees)
    {
        settings.CameraAngle = (settings.CameraAngle + degrees) % 360;
        renderer.Orbit(degrees);
        SaveSettings();
    }
}
