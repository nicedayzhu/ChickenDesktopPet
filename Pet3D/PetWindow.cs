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
        public bool ShowQuickActions { get; set; } = true;
        public float CameraAngle { get; set; } = 30;
        public string AppearanceId { get; set; } = PetCatalog.DefaultAppearanceId;
        public Dictionary<string, string> PetNames { get; set; } = new(StringComparer.Ordinal);
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
    private readonly PetIconStore icons = new();
    private readonly QuickActionBar quickActions;
    private readonly Popup actionPopup = new() { AllowsTransparency = true, StaysOpen = true,
        Placement = PlacementMode.Relative, PopupAnimation = PopupAnimation.Fade };
    private readonly PhotoLibrary photos = new();
    private IReadOnlyList<PetActivity> activities = PetUiResources.DefaultActivities;
    private InspectWindow? inspector;
    private bool startupInspectorRequested = Environment.GetCommandLineArgs().Contains("--inspect") ||
        Environment.GetEnvironmentVariable("CHICK_OPEN_INSPECTOR") == "1";
    private double hoverStarted = -1;
    private double lastHover;
    private bool menuOpen;
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
        settings.PetNames ??= new(StringComparer.Ordinal);
        quickActions = new QuickActionBar(icons);
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
        actionPopup.PlacementTarget = layout;
        actionPopup.Child = quickActions;
        quickActions.ActionRequested += RunQuickAction;
        quickActions.SizeChangedByContent += () => { if (actionPopup.IsOpen) PositionQuickActions(); };
        Content = layout;

        image.MouseLeftButtonDown += OnMouseDown;
        image.MouseLeftButtonUp += OnMouseUp;
        image.MouseMove += OnMouseMove;
        image.MouseRightButtonUp += (_, _) => OpenMenu();
        image.MouseWheel += (_, e) => { RotateCamera(e.Delta > 0 ? 15 : -15); e.Handled = true; };
        image.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            RunQuickAction("feed");
            e.Handled = true;
        };
        SourceInitialized += (_, _) => ((HwndSource)PresentationSource.FromVisual(this)).AddHook(HitTestHook);
        LocationChanged += (_, _) => { if (actionPopup.IsOpen) PositionQuickActions(); };

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
        renderer.UiResourcesReady += resources => Dispatcher.BeginInvoke(() =>
        {
            if (closing) return;
            icons.Load(resources);
            activities = resources.Activities;
            quickActions.SetResources(resources);
        });
        renderer.AppearancesReady += models => Dispatcher.BeginInvoke(() =>
        {
            if (!closing) { appearances = models; inspector?.SetCatalog(models); }
        });
        renderer.AppearanceChanged += (model, actions) => Dispatcher.BeginInvoke(() =>
        {
            if (closing) return;
            selectedAppearance = model;
            availableActions = new HashSet<string>(actions, StringComparer.Ordinal);
            quickActions.SetActions(availableActions, model.Kind == PetKind.Egg);
            quickActions.SetTitle(DisplayName);
            settings.AppearanceId = model.Id;
            current = "idle";
            quickActions.SetState(current);
            walkRemaining = 0;
            Title = $"CS2 鸡桌宠 · {model.GroupLabel}";
            inspector?.SetAppearance(model, availableActions, DisplayName);
            inspector?.SetState(current);
            SaveSettings();
        });
        renderer.ActionChanged += action => Dispatcher.BeginInvoke(() =>
        {
            if (closing) return;
            current = action;
            actionStarted = clock.Elapsed.TotalSeconds;
            quickActions.SetState(action);
            inspector?.SetState(action);
        });
        renderer.AppearanceFailed += (id, error) => Dispatcher.BeginInvoke(() =>
        {
            if (closing || selectedAppearance is null) return;
            inspector?.SetAppearance(selectedAppearance, availableActions, DisplayName);
            System.Windows.MessageBox.Show(inspector is null ? this : inspector, $"无法切换到 {id}：{error.Message}",
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
            actionPopup.IsOpen = false;
            inspector?.Close();
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
                    Opacity = inspector is null || inspector.WindowState == WindowState.Minimized ? 1 : 0;
                    if (startupInspectorRequested && framesReceived >= 3)
                    {
                        startupInspectorRequested = false;
                        OpenInspector();
                    }
                    var capturePath = Environment.GetEnvironmentVariable("CHICK_CAPTURE_FRAME");
                    if (!captured && framesReceived >= 30 && !string.IsNullOrWhiteSpace(capturePath))
                    {
                        captured = true;
                        Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                var target = inspector?.Content as FrameworkElement ?? layout;
                                target.UpdateLayout();
                                var preview = new RenderTargetBitmap((int)target.ActualWidth, (int)target.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                                preview.Render(target);
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
        if (IsQuickActionArea(point)) return 0;
        if (!IsPetPixel(point))
        {
            handled = true;
            return -1;
        }
        return 0;
    }

    private bool IsPetPixel(Point point)
    {
        if (hitFrame is null || ActualWidth <= 0 || ActualHeight <= 0) return false;
        var x = (int)(point.X * ChickRenderer.Resolution / ActualWidth);
        var y = (int)(point.Y * ChickRenderer.Resolution / ActualHeight);
        if (image.RenderTransform is ScaleTransform { ScaleX: < 0 })
            x = ChickRenderer.Resolution - 1 - x;
        return point.X >= 0 && point.Y >= 0 && x >= 0 && y >= 0 && x < ChickRenderer.Resolution && y < ChickRenderer.Resolution &&
            hitFrame[(y * ChickRenderer.Resolution + x) * 4 + 3] >= 18;
    }

    private bool IsQuickActionArea(Point point)
    {
        if (!actionPopup.IsOpen || PresentationSource.FromVisual(quickActions) is null) return false;
        var upper = quickActions.PointToScreen(new Point());
        var lower = quickActions.PointToScreen(new Point(quickActions.ActualWidth, quickActions.ActualHeight));
        var area = new Rect(upper, lower);
        area.Inflate(12, 22);
        return area.Contains(PointToScreen(point));
    }

    private void RunQuickAction(string action)
    {
        walkRemaining = 0;
        ScheduleRoam();
        switch (action)
        {
            case "inspect": OpenInspector(); break;
            case "photo": OpenInspector(true); break;
            case "trick": PlayOneOf("trick", "trick2"); break;
            case "wake":
                if (availableActions.Contains("react") || availableActions.Contains("react2")) PlayOneOf("react", "react2");
                else Play("idle");
                break;
            default: Play(action); break;
        }
    }

    private void UpdateQuickActions(double now)
    {
        if (!settings.ShowQuickActions || !quickActions.HasActions || dragging || menuOpen || inspector is not null)
        {
            HideQuickActions();
            hoverStarted = -1;
            return;
        }
        var cursor = Forms.Cursor.Position;
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        if (IsPetPixel(point) || IsQuickActionArea(point))
        {
            if (hoverStarted < 0) hoverStarted = now;
            lastHover = now;
            if (now - hoverStarted >= .45)
            {
                if (!actionPopup.IsOpen)
                {
                    quickActions.Visibility = Visibility.Visible;
                    PositionQuickActions();
                    actionPopup.IsOpen = true;
                }
                if (current == "walk") { walkRemaining = 0; Play("idle"); }
            }
        }
        else
        {
            hoverStarted = -1;
            if (now - lastHover > .65) HideQuickActions();
        }
    }

    private void PositionQuickActions()
    {
        quickActions.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = quickActions.DesiredSize;
        var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var upper = PointFromScreen(new Point(screen.Left, screen.Top));
        var lower = PointFromScreen(new Point(screen.Right, screen.Bottom));
        var pet = new Rect(Width * .25, Height * .2, Width * .5, Height * .6);
        if (hitFrame is not null)
        {
            var minX = ChickRenderer.Resolution; var minY = minX; var maxX = 0; var maxY = 0;
            for (var row = 0; row < ChickRenderer.Resolution; row += 4)
                for (var column = 0; column < ChickRenderer.Resolution; column += 4)
                {
                    if (hitFrame[(row * ChickRenderer.Resolution + column) * 4 + 3] < 80) continue;
                    minX = Math.Min(minX, column); maxX = Math.Max(maxX, column);
                    minY = Math.Min(minY, row); maxY = Math.Max(maxY, row);
                }
            if (minX < maxX && minY < maxY)
            {
                if (image.RenderTransform is ScaleTransform { ScaleX: < 0 })
                    (minX, maxX) = (ChickRenderer.Resolution - maxX, ChickRenderer.Resolution - minX);
                pet = new Rect(minX * Width / ChickRenderer.Resolution, minY * Height / ChickRenderer.Resolution,
                    (maxX - minX) * Width / ChickRenderer.Resolution, (maxY - minY) * Height / ChickRenderer.Resolution);
            }
        }
        var x = pet.X + pet.Width * .5 - size.Width * .5;
        var y = pet.Bottom + 8;
        if (y + size.Height > lower.Y - 8) y = pet.Top - size.Height - 8;
        if (y < upper.Y + 8) { x = pet.Right + 8; y = pet.Top; }
        actionPopup.HorizontalOffset = Math.Clamp(x, upper.X + 8, Math.Max(upper.X + 8, lower.X - size.Width - 8));
        actionPopup.VerticalOffset = Math.Clamp(y, upper.Y + 8, Math.Max(upper.Y + 8, lower.Y - size.Height - 8));
    }

    private void HideQuickActions()
    {
        actionPopup.IsOpen = false;
        quickActions.Visibility = Visibility.Collapsed;
        quickActions.CollapseActivities();
    }

    private string DisplayName => selectedAppearance is not null &&
        settings.PetNames.TryGetValue(selectedAppearance.ModelId, out var name) && !string.IsNullOrWhiteSpace(name)
        ? name : selectedAppearance?.GroupLabel ?? "小鸡";

    private void OpenInspector(bool takePhoto = false)
    {
        if (selectedAppearance is null || hitFrame is null || closing) return;
        HideQuickActions();
        walkRemaining = 0;
        if (current == "walk") Play("idle");
        if (inspector is not null)
        {
            if (inspector.WindowState == WindowState.Minimized) inspector.WindowState = WindowState.Normal;
            inspector.Activate();
            if (takePhoto) _ = inspector.TakePhotoAsync();
            return;
        }
        var originalAngle = settings.CameraAngle;
        var window = new InspectWindow(bitmap, icons, activities, photos) { Owner = this };
        window.SetCatalog(appearances);
        window.SetAppearance(selectedAppearance, availableActions, DisplayName);
        window.SetState(current);
        window.ActionRequested += RunQuickAction;
        window.AppearanceRequested += id => renderer.SelectAppearance(id);
        window.NameRequested += name =>
        {
            if (selectedAppearance is null) return;
            if (string.IsNullOrWhiteSpace(name)) settings.PetNames.Remove(selectedAppearance.ModelId);
            else settings.PetNames[selectedAppearance.ModelId] = name.Length > 20 ? name[..20] : name;
            SaveSettings(); quickActions.SetTitle(DisplayName); window.UpdateName(DisplayName);
        };
        window.OrbitRequested += RotateCamera;
        window.ZoomRequested += renderer.SetZoom;
        window.ResetViewRequested += () => RotateCamera(originalAngle - settings.CameraAngle);
        window.MoreSettingsRequested += OpenMenu;
        window.StateChanged += (_, _) =>
        {
            var visible = window.WindowState != WindowState.Minimized;
            renderer.InspectionMode = visible;
            Opacity = visible ? 0 : 1;
        };
        window.Closed += (_, _) =>
        {
            inspector = null;
            renderer.InspectionMode = false;
            renderer.SetZoom(1);
            if (closing) return;
            RotateCamera(originalAngle - settings.CameraAngle);
            if (current != "sleep") Play("idle");
            Opacity = 1;
            ScheduleRoam();
        };
        inspector = window;
        renderer.InspectionMode = true;
        Opacity = 0;
        window.Show();
        if (takePhoto) _ = window.TakePhotoAsync();
    }

    private void Play(string name)
    {
        if (!availableActions.Contains(name)) return;
        current = name;
        actionStarted = clock.Elapsed.TotalSeconds;
        renderer.Play(name, PetActions.Loops(name));
    }

    private void PlayOneOf(params string[] choices)
    {
        var playable = choices.Where(availableActions.Contains).ToArray();
        if (playable.Length > 0) Play(playable[random.Next(playable.Length)]);
    }

    private void Tick()
    {
        var now = clock.Elapsed.TotalSeconds;
        UpdateQuickActions(now);
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
        else if (settings.Roam && inspector is null && !actionPopup.IsOpen && availableActions.Contains("walk") && current == "idle" && now >= nextRoam)
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
        HideQuickActions();
        hoverStarted = -1;
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
        if (inspector is not null) inspector.Activate(); else Activate();
        HideQuickActions();
        menuOpen = true;
        var menu = BuildMenu();
        menu.Closed += (_, _) => { menuOpen = false; hoverStarted = -1; };
        menu.IsOpen = true;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        PanoramaTheme.Apply(menu);
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
        Add("检视与摄影棚", () => OpenInspector());
        Add("拍照", () => OpenInspector(true));
        Add("照片库", () => new PhotoLibraryWindow(photos) { Owner = inspector is null ? this : inspector,
            WindowStartupLocation = WindowStartupLocation.CenterScreen }.ShowDialog());
        menu.Items.Add(new Separator());

        Add("喂食", () => { walkRemaining = 0; Play("feed"); }).IsEnabled = availableActions.Contains("feed");
        Add("表演", () => { walkRemaining = 0; PlayOneOf("trick", "trick2"); }).IsEnabled =
            availableActions.Contains("trick") || availableActions.Contains("trick2");
        var named = new MenuItem { Header = "选择动作", IsEnabled = selectedAppearance?.Kind == PetKind.Chicken };
        foreach (var activity in activities)
        {
            var item = new MenuItem { Header = activity.Label, Icon = icons.Create(activity.Icon, 20),
                IsEnabled = availableActions.Contains(activity.Id) };
            item.Click += (_, _) => RunQuickAction(activity.Id);
            named.Items.Add(item);
        }
        menu.Items.Add(named);
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
        Add(settings.ShowQuickActions ? "隐藏悬停互动栏" : "显示悬停互动栏", () =>
        {
            settings.ShowQuickActions = !settings.ShowQuickActions;
            HideQuickActions();
            SaveSettings();
        });
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
        PanoramaTheme.Apply(dialog);
        dialog.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 33, 35));
        dialog.ShowDialog();
    }

    private static void OpenUrl(string url) => System.Diagnostics.Process.Start(
        new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    private void ResizePet(int amount)
    {
        Width = Height = Math.Clamp(Width + amount, 240, 480);
        HideQuickActions();
        SaveSettings();
    }

    private void RotateCamera(float degrees)
    {
        settings.CameraAngle = (settings.CameraAngle + degrees) % 360;
        renderer.Orbit(degrees);
        SaveSettings();
    }
}
