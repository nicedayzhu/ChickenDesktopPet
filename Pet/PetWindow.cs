using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using Forms = System.Windows.Forms;

namespace ChickenDesktopPet;

internal sealed class PetWindow : Window
{
    private sealed record SpriteClip(int Fps, int Frames, bool Loop);
    private sealed class Settings
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double Size { get; set; } = 256;
        public bool AlwaysOnTop { get; set; } = true;
        public bool Roam { get; set; } = true;
    }

    private readonly string spriteRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChickenDesktopPet", "settings.json");
    private readonly Dictionary<string, SpriteClip> clips;
    private readonly Dictionary<string, BitmapImage> frameCache = new();
    private readonly Queue<string> cacheOrder = new();
    private readonly System.Windows.Controls.Image image = new() { Stretch = Stretch.Uniform, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5) };
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Random random = new();
    private readonly string? diagnosticsPath = Environment.GetEnvironmentVariable("CHICK_PET_FPS_LOG");
    private readonly Forms.NotifyIcon tray;
    private readonly Settings settings;
    private string current = "idle";
    private double clipStart;
    private int frameIndex = -1;
    private double walkRemaining;
    private double lastTick;
    private int walkDirection = 1;
    private bool dragging;
    private System.Windows.Point pressPoint;
    private double nextRoam;
    private double nextDiagnostics = 1;
    private int framesPresented;

    public PetWindow()
    {
        var manifestPath = Path.Combine(spriteRoot, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("找不到动画资源。请从 publish 文件夹运行程序。", manifestPath);
        clips = JsonSerializer.Deserialize<Dictionary<string, SpriteClip>>(File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        settings = LoadSettings();

        Title = "CS2 小鸡桌宠";
        Width = Height = settings.Size;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = settings.AlwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea;
        Left = settings.Left ?? area.Right - Width - 30;
        Top = settings.Top ?? area.Bottom - Height - 20;
        Content = image;

        image.MouseLeftButtonDown += OnMouseDown;
        image.MouseLeftButtonUp += OnMouseUp;
        image.MouseMove += OnMouseMove;
        image.MouseRightButtonUp += (_, _) => BuildMenu().IsOpen = true;
        tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "chick.ico")),
            Text = "CS2 小鸡桌宠",
            Visible = true
        };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Dispatcher.Invoke(() => { Activate(); Play("react"); });
            else if (e.Button == Forms.MouseButtons.Right)
                Dispatcher.Invoke(() => BuildMenu().IsOpen = true);
        };
        Closed += (_, _) => { timer.Stop(); tray.Dispose(); SaveSettings(); };
        Play("idle");
        ScheduleRoam();
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    private Settings LoadSettings()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new Settings();
        }
        catch { return new Settings(); }
    }

    private void SaveSettings()
    {
        settings.Left = Left;
        settings.Top = Top;
        settings.Size = Width;
        settings.AlwaysOnTop = Topmost;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
    }

    private void Play(string name)
    {
        if (!clips.ContainsKey(name)) return;
        current = name;
        clipStart = clock.Elapsed.TotalSeconds;
        frameIndex = -1;
        ShowFrame(0);
    }

    private void ShowFrame(int index)
    {
        if (index == frameIndex) return;
        frameIndex = index;
        var path = Path.Combine(spriteRoot, current, $"{index:000}.png");
        if (!frameCache.TryGetValue(path, out var bitmap))
        {
            using var stream = File.OpenRead(path);
            bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            frameCache[path] = bitmap;
            cacheOrder.Enqueue(path);
            while (cacheOrder.Count > 72)
                frameCache.Remove(cacheOrder.Dequeue());
        }
        image.Source = bitmap;
        framesPresented++;
    }

    private void Tick()
    {
        var now = clock.Elapsed.TotalSeconds;
        var elapsed = Math.Clamp(now - lastTick, 0, 0.1);
        lastTick = now;
        if (diagnosticsPath is not null && now >= nextDiagnostics)
        {
            try { File.AppendAllText(diagnosticsPath, $"{now:F2},{current},{framesPresented}\n"); }
            catch (IOException) { }
            framesPresented = 0;
            nextDiagnostics = now + 1;
        }
        var clip = clips[current];
        var frame = (int)((now - clipStart) * clip.Fps);
        if (frame >= clip.Frames)
        {
            if (clip.Loop) frame %= clip.Frames;
            else { Play("idle"); return; }
        }
        ShowFrame(frame);

        if (walkRemaining > 0 && !dragging)
        {
            var area = SystemParameters.WorkArea;
            var step = Math.Min(walkRemaining, elapsed * 45);
            Left = Math.Clamp(Left + walkDirection * step, area.Left, area.Right - Width);
            walkRemaining -= step;
            if (walkRemaining < 0.01) { walkRemaining = 0; Play("idle"); SaveSettings(); }
        }
        else if (settings.Roam && now >= nextRoam && current == "idle")
        {
            walkDirection = random.Next(2) == 0 ? -1 : 1;
            image.RenderTransform = new ScaleTransform(walkDirection, 1);
            walkRemaining = random.Next(63, 180);
            Play("walk");
            ScheduleRoam();
        }
        else if (current == "idle" && now - clipStart > 13 && random.NextDouble() < elapsed * 0.08)
            Play("idle2");
    }

    private void ScheduleRoam() => nextRoam = clock.Elapsed.TotalSeconds + random.Next(25, 55);

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        pressPoint = e.GetPosition(this);
        dragging = false;
        if (e.ClickCount >= 2)
        {
            walkRemaining = 0;
            Play(random.Next(2) == 0 ? "trick" : "trick2");
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
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
            Play(random.Next(2) == 0 ? "react" : "react2");
        }
        dragging = false;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        void Add(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("喂食", () => { walkRemaining = 0; Play("feed"); });
        Add("表演", () => { walkRemaining = 0; Play(random.Next(2) == 0 ? "trick" : "trick2"); });
        Add("睡觉", () => { walkRemaining = 0; Play("sleep"); });
        Add("叫醒", () => Play("react"));
        menu.Items.Add(new Separator());
        Add(settings.Roam ? "停止散步" : "允许散步", () => { settings.Roam = !settings.Roam; walkRemaining = 0; Play("idle"); SaveSettings(); });
        Add(Topmost ? "取消置顶" : "始终置顶", () => { Topmost = !Topmost; SaveSettings(); });
        Add("缩小", () => ResizePet(-32));
        Add("放大", () => ResizePet(32));
        menu.Items.Add(new Separator());
        Add("退出", Close);
        return menu;
    }

    private void ResizePet(int delta)
    {
        Width = Height = Math.Clamp(Width + delta, 128, 384);
        SaveSettings();
    }
}
