using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace ChickenDesktopPet3D;

internal sealed class InspectWindow : Window
{
    private readonly PetIconStore icons;
    private readonly PhotoLibrary library;
    private readonly BitmapSource preview;
    private readonly IReadOnlyList<PetActivity> activities;
    private readonly Grid photoCanvas = new() { Width = 512, Height = 512, ClipToBounds = true };
    private readonly Image modelImage;
    private readonly TextBlock heading = PanoramaTheme.Label("小鸡", 24);
    private readonly TextBlock subtitle = PanoramaTheme.Label("检视与摄影棚", 12, true);
    private readonly TextBlock state = PanoramaTheme.Label("自在休息", 12, true);
    private readonly TextBlock notice = PanoramaTheme.Label("照片保存到“图片 / CS2 鸡桌宠”", 11, true);
    private readonly TextBlock zoomText = PanoramaTheme.Label("100%", 12, true);
    private readonly ComboBox models = new() { DisplayMemberPath = "GroupLabel", Margin = new Thickness(0, 0, 0, 9) };
    private readonly ComboBox skins = new() { DisplayMemberPath = "Label" };
    private readonly TextBox nameEntry = new() { MaxLength = 20, Margin = new Thickness(0, 0, 0, 7) };
    private readonly ComboBox backgrounds = new() { ItemsSource = new[] { "摄影棚", "浅色", "深色", "透明 PNG" }, SelectedIndex = 0 };
    private readonly Dictionary<string, Button> actionButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> basicButtons = new(StringComparer.Ordinal);
    private readonly StackPanel actionRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button saveName;
    private IReadOnlyList<PetAppearance> appearances = [];
    private IReadOnlySet<string> available = new HashSet<string>();
    private PetAppearance? appearance;
    private string petName = "小鸡";
    private string current = "idle";
    private bool updating;
    private bool selecting;
    private bool takingPhoto;
    private Point? dragPoint;
    private float zoom = 1;

    public event Action<string>? ActionRequested;
    public event Action<string>? AppearanceRequested;
    public event Action<string>? NameRequested;
    public event Action<float>? OrbitRequested;
    public event Action<float>? ZoomRequested;
    public event Action? ResetViewRequested;
    public event Action? MoreSettingsRequested;

    public InspectWindow(BitmapSource preview, PetIconStore icons, IReadOnlyList<PetActivity> activities, PhotoLibrary library)
    {
        this.preview = preview; this.icons = icons; this.activities = activities; this.library = library;
        Title = "CS2 鸡桌宠 · 检视与摄影棚";
        Width = Math.Min(980, SystemParameters.WorkArea.Width - 48);
        Height = Math.Min(790, SystemParameters.WorkArea.Height - 48);
        MinWidth = 780; MinHeight = 620;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(31, 33, 35));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        PanoramaTheme.Apply(this);

        var frame = new Border { Background = Background, BorderBrush = PanoramaTheme.Border,
            BorderThickness = new Thickness(1), Padding = new Thickness(22, 16, 22, 12) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel(); title.Children.Add(heading); subtitle.Margin = new Thickness(1, 5, 0, 0); title.Children.Add(subtitle);
        heading.MaxWidth = 480; heading.TextTrimming = TextTrimming.CharacterEllipsis;
        heading.HorizontalAlignment = HorizontalAlignment.Left;
        header.Children.Add(title);
        var chrome = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        chrome.Children.Add(PanoramaTheme.IconButton(icons, "shrink_video", "最小化", () => WindowState = WindowState.Minimized, 34));
        chrome.Children.Add(PanoramaTheme.IconButton(icons, "close", "回到桌面 · Esc", Close, 34));
        Grid.SetColumn(chrome, 1); header.Children.Add(chrome);
        header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) { try { DragMove(); } catch (InvalidOperationException) { } } };
        root.Children.Add(header);

        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(244) });
        Grid.SetRow(body, 1); root.Children.Add(body);
        var stage = new Grid { Margin = new Thickness(0, 0, 22, 0) };
        stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        modelImage = new Image { Source = preview, Width = 512, Height = 512, Stretch = Stretch.Fill };
        photoCanvas.Children.Add(modelImage);
        var viewer = new Viewbox { Child = photoCanvas, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 12), ClipToBounds = true };
        stage.Children.Add(viewer);
        photoCanvas.Focusable = true;
        photoCanvas.MouseLeftButtonDown += (_, e) => { photoCanvas.Focus(); dragPoint = e.GetPosition(photoCanvas); photoCanvas.CaptureMouse(); e.Handled = true; };
        photoCanvas.MouseMove += (_, e) =>
        {
            if (dragPoint is not { } previous || e.LeftButton != MouseButtonState.Pressed) return;
            var next = e.GetPosition(photoCanvas); OrbitRequested?.Invoke((float)(next.X - previous.X) * .5f);
            dragPoint = next; e.Handled = true;
        };
        photoCanvas.MouseLeftButtonUp += (_, e) => { dragPoint = null; photoCanvas.ReleaseMouseCapture(); e.Handled = true; };
        photoCanvas.LostMouseCapture += (_, _) => dragPoint = null;
        photoCanvas.MouseWheel += (_, e) => { SetZoom(zoom + (e.Delta > 0 ? .1f : -.1f)); e.Handled = true; };

        foreach (var activity in activities)
        {
            var content = new StackPanel(); content.Children.Add(icons.Create(activity.Icon));
            var label = PanoramaTheme.Label(activity.Label, 11); label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 5, 0, 0); content.Children.Add(label);
            var button = new Button { Content = content, Width = 61, Height = 60, Padding = new Thickness(4), Tag = activity.Id };
            button.Click += (_, _) => ActionRequested?.Invoke(activity.Id);
            ToolTipService.SetShowOnDisabled(button, true); actionButtons[activity.Id] = button; actionRow.Children.Add(button);
        }
        var actions = new Border { Child = actionRow, CornerRadius = new CornerRadius(8), Background = PanoramaTheme.Panel,
            BorderBrush = PanoramaTheme.Border, BorderThickness = new Thickness(1), Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(actions, 1); stage.Children.Add(actions);
        var camera = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        camera.Children.Add(PanoramaTheme.IconButton(icons, "left", "向左旋转", () => OrbitRequested?.Invoke(-15)));
        camera.Children.Add(PanoramaTheme.IconButton(icons, "right", "向右旋转", () => OrbitRequested?.Invoke(15)));
        camera.Children.Add(PanoramaTheme.IconButton(icons, "reset", "重置视角", () => { SetZoom(1); ResetViewRequested?.Invoke(); }));
        camera.Children.Add(PanoramaTheme.IconButton(icons, "shrink_video", "拉远", () => SetZoom(zoom - .1f)));
        zoomText.Width = 48; zoomText.TextAlignment = TextAlignment.Center; camera.Children.Add(zoomText);
        camera.Children.Add(PanoramaTheme.IconButton(icons, "expand_video", "拉近", () => SetZoom(zoom + .1f)));
        var hint = PanoramaTheme.Label("拖动旋转 · 滚轮缩放", 11, true); hint.Margin = new Thickness(14, 0, 0, 0); camera.Children.Add(hint);
        Grid.SetRow(camera, 2); stage.Children.Add(camera);
        body.Children.Add(stage);

        var settings = new StackPanel { Margin = new Thickness(16) };
        Section(settings, "宠物外观"); settings.Children.Add(models); settings.Children.Add(skins);
        Section(settings, "宠物名称"); settings.Children.Add(nameEntry);
        saveName = new Button { Content = "保存名称", HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderBrush = PanoramaTheme.Border, Padding = new Thickness(8, 7, 8, 7) };
        saveName.Click += (_, _) => SaveName(); nameEntry.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SaveName(); e.Handled = true; } };
        settings.Children.Add(saveName);
        Section(settings, "日常互动");
        var basic = new UniformGrid { Columns = 3 };
        AddBasic(basic, "喂食", "feed"); AddBasic(basic, "睡觉", "sleep"); AddBasic(basic, "停止", "idle"); settings.Children.Add(basic);
        state.Margin = new Thickness(0, 9, 0, 0); settings.Children.Add(state);
        Section(settings, "摄影棚"); settings.Children.Add(backgrounds);
        var photoRow = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        photoRow.ColumnDefinitions.Add(new ColumnDefinition()); photoRow.ColumnDefinitions.Add(new ColumnDefinition());
        var capture = new Button { Content = "拍照", Background = new SolidColorBrush(Color.FromRgb(84, 86, 78)),
            Margin = new Thickness(0, 0, 4, 0), ToolTip = "保存当前姿态为 PNG" };
        capture.Click += async (_, _) => await TakePhotoAsync(); photoRow.Children.Add(capture);
        var album = new Button { Content = "照片库", BorderBrush = PanoramaTheme.Border, Margin = new Thickness(4, 0, 0, 0) };
        album.Click += (_, _) => OpenLibrary(); Grid.SetColumn(album, 1); photoRow.Children.Add(album); settings.Children.Add(photoRow);
        notice.TextWrapping = TextWrapping.Wrap; notice.LineHeight = 17; notice.Margin = new Thickness(0, 10, 0, 0); settings.Children.Add(notice);
        var more = new Button { Content = "更多桌宠设置", Margin = new Thickness(0, 18, 0, 0), BorderBrush = PanoramaTheme.Border };
        more.Click += (_, _) => MoreSettingsRequested?.Invoke(); settings.Children.Add(more);
        var sidebar = new Border { Background = PanoramaTheme.Panel, CornerRadius = new CornerRadius(8), BorderBrush = PanoramaTheme.Border,
            BorderThickness = new Thickness(1), Child = new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        Grid.SetColumn(sidebar, 1); body.Children.Add(sidebar);

        var footer = new DockPanel { Margin = new Thickness(1, 10, 0, 0) };
        var author = new Button { Content = "niceday_zhu · GitHub", Padding = new Thickness(4, 3, 4, 3), FontSize = 11 };
        author.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/nicedayzhu") { UseShellExecute = true });
        DockPanel.SetDock(author, Dock.Right); footer.Children.Add(author);
        footer.Children.Add(PanoramaTheme.Label("CS2 鸡桌宠  /  Powered by Source 2 Viewer · VRF", 11, true));
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        frame.Child = root; Content = frame;

        models.SelectionChanged += (_, _) =>
        {
            if (updating || models.SelectedItem is not PetAppearance model) return;
            var selected = appearances.FirstOrDefault(item => item.ModelId == model.ModelId && item.Skin is null)
                ?? appearances.First(item => item.ModelId == model.ModelId);
            RequestAppearance(selected.Id);
        };
        skins.SelectionChanged += (_, _) => { if (!updating && skins.SelectedItem is PetAppearance selected) RequestAppearance(selected.Id); };
        backgrounds.SelectionChanged += (_, _) => UpdateBackground();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Space && Keyboard.FocusedElement is not (TextBox or ComboBox))
            { ActionRequested?.Invoke(current == "sleep" ? "wake" : "idle"); e.Handled = true; }
        };
        UpdateBackground(); SetState("idle");
    }

    private static void Section(StackPanel parent, string title)
    {
        var text = PanoramaTheme.Label(title, 12, true);
        text.Margin = new Thickness(0, parent.Children.Count == 0 ? 0 : 19, 0, 9); parent.Children.Add(text);
    }

    private void AddBasic(UniformGrid row, string label, string action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(2, 8, 2, 8), Tag = action };
        button.Click += (_, _) =>
        {
            var selected = (string)button.Tag;
            ActionRequested?.Invoke(selected == "sleep" && current == "sleep" ? "wake" : selected);
        };
        ToolTipService.SetShowOnDisabled(button, true); basicButtons[action] = button; row.Children.Add(button);
    }

    public void SetCatalog(IReadOnlyList<PetAppearance> catalog)
    {
        appearances = catalog;
        updating = true;
        models.ItemsSource = catalog.DistinctBy(item => item.ModelId).ToArray();
        updating = false;
    }

    public void SetAppearance(PetAppearance selected, IReadOnlySet<string> actions, string name)
    {
        appearance = selected; available = actions; petName = name;
        heading.Text = name; Title = $"{name} · 检视与摄影棚";
        subtitle.Text = selected.GroupLabel + (selected.Skin is null ? "" : " · " + selected.Label) + "  /  检视与摄影棚";
        updating = true;
        models.SelectedItem = models.Items.Cast<PetAppearance>().FirstOrDefault(item => item.ModelId == selected.ModelId);
        skins.ItemsSource = appearances.Where(item => item.ModelId == selected.ModelId).ToArray(); skins.SelectedItem = selected;
        skins.IsEnabled = skins.Items.Count > 1;
        nameEntry.Text = name;
        updating = false;
        SetSelecting(false); SetState(current);
        var hatch = selected.Kind == PetKind.Egg;
        ((Border)actionRow.Parent).Visibility = selected.Kind == PetKind.Chicken ? Visibility.Visible : Visibility.Collapsed;
        // Eggs keep their own reaction/hatching controls instead of chicken tricks.
        if (basicButtons.TryGetValue("feed", out var feed))
        {
            feed.Content = hatch ? "破壳" : "喂食";
            feed.Tag = hatch ? "trick" : "feed";
        }
    }

    public void SetSelecting(bool value)
    {
        selecting = value;
        models.IsEnabled = !value;
        skins.IsEnabled = !value && skins.Items.Count > 1;
        nameEntry.IsEnabled = saveName.IsEnabled = !value;
        if (value) state.Text = "正在切换宠物…";
        SetState(current);
    }

    public void SetState(string action)
    {
        current = action;
        var busy = !PetActions.Loops(action);
        var label = appearance?.Kind == PetKind.Egg && action is "trick" or "trick2" ? "破壳" : PetActions.Label(action);
        state.Text = selecting ? "正在切换宠物…" : action == "sleep" ? "睡梦中 · 点击叫醒" : busy ? "正在" + label : "自在休息";
        state.Foreground = busy ? PanoramaTheme.Accent : PanoramaTheme.Muted;
        foreach (var activity in activities)
        {
            var button = actionButtons[activity.Id];
            var supported = available.Contains(activity.Id);
            button.IsEnabled = supported && !selecting && !busy;
            button.Background = action == activity.Id ? new SolidColorBrush(Color.FromRgb(92, 91, 78)) : Brushes.Transparent;
            button.ToolTip = !supported ? $"当前宠物不支持{activity.Label}" : busy ? "当前动作播放完即可选择" : activity.Label;
        }
        foreach (var (id, button) in basicButtons)
        {
            var key = id == "feed" && appearance?.Kind == PetKind.Egg ? "trick" : id;
            button.IsEnabled = !selecting && (key == "idle" || available.Contains(key)) && (!busy || key == "idle");
            button.ToolTip = key == "idle" ? "停止当前动作 · 空格" : available.Contains(key) ? PetActions.Label(key) : "当前宠物没有这个动作";
            if (id == "sleep") button.Content = action == "sleep" ? "叫醒" : "睡觉";
        }
    }

    public void UpdateName(string name)
    {
        petName = name; heading.Text = name; Title = $"{name} · 检视与摄影棚";
        nameEntry.Text = name; notice.Text = "名称已保存";
    }

    private void RequestAppearance(string id)
    {
        if (id == appearance?.Id) return;
        SetSelecting(true); AppearanceRequested?.Invoke(id);
    }

    private void SaveName() => NameRequested?.Invoke(nameEntry.Text.Trim());

    private void SetZoom(float value)
    {
        zoom = Math.Clamp(value, .7f, 1.45f); zoomText.Text = $"{zoom * 100:F0}%"; ZoomRequested?.Invoke(zoom);
    }

    private void UpdateBackground()
    {
        photoCanvas.Background = backgrounds.SelectedIndex switch
        {
            1 => new SolidColorBrush(Color.FromRgb(222, 225, 224)),
            2 => new SolidColorBrush(Color.FromRgb(19, 21, 23)),
            3 => Brushes.Transparent,
            _ => new RadialGradientBrush(Color.FromRgb(97, 99, 94), Color.FromRgb(44, 46, 47))
            { GradientOrigin = new Point(.45, .4), Center = new Point(.45, .4), RadiusX = .65, RadiusY = .7 },
        };
    }

    internal BitmapSource CapturePhoto()
    {
        if (backgrounds.SelectedIndex == 3) { var transparent = preview.Clone(); transparent.Freeze(); return transparent; }
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(photoCanvas.Background, null, new Rect(0, 0, 512, 512));
            drawing.DrawImage(preview, new Rect(0, 0, 512, 512));
        }
        var bitmap = new RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    internal async Task TakePhotoAsync()
    {
        if (takingPhoto || appearance is null || selecting) return;
        takingPhoto = true;
        try
        {
            var path = await library.SaveAsync(CapturePhoto(), appearance.Id, petName, backgrounds.SelectedItem?.ToString() ?? "摄影棚");
            notice.Text = "照片已保存 · 可在照片库查看";
            ErrorLog.Trace($"saved photo {path}");
        }
        catch (Exception ex) { notice.Text = "照片保存失败：" + ex.Message; ErrorLog.Write(ex); }
        finally { takingPhoto = false; }
    }

    private void OpenLibrary()
    {
        try { new PhotoLibraryWindow(library) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { notice.Text = "无法打开照片库：" + ex.Message; ErrorLog.Write(ex); }
    }
}
