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
    private readonly Grid photoCanvas = new() { Width = 512, Height = 512, ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Grid stage = new() { Background = Brushes.Transparent, Focusable = true };
    private readonly TextBlock heading = PanoramaTheme.Label("小鸡", 28);
    private readonly TextBlock subtitle = PanoramaTheme.Label("检视 · 摄影棚", 12, true);
    private readonly TextBlock state = PanoramaTheme.Label("自在休息", 12, true);
    private readonly TextBlock notice = PanoramaTheme.Label("PNG 照片保存在本机“图片”文件夹", 11, true);
    private readonly TextBlock zoomText = PanoramaTheme.Label("100%", 12, true);
    private readonly ComboBox models = new() { DisplayMemberPath = "GroupLabel", MinHeight = 42 };
    private readonly ComboBox skins = new() { DisplayMemberPath = "Label", MinHeight = 42 };
    private readonly TextBox nameEntry = new() { MaxLength = 20, MinWidth = 180, MaxWidth = 300 };
    private readonly ComboBox backgrounds = new() { ItemsSource = new[] { "摄影棚", "浅色", "深色", "透明 PNG" }, SelectedIndex = 0 };
    private readonly Dictionary<string, Button> actionButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> basicButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<Button, TextBlock> captions = new();
    private readonly StackPanel actionRow = new() { Orientation = Orientation.Horizontal };
    private readonly Button saveName;
    private readonly Border nameEditor;
    private readonly Button zoomOut;
    private readonly Button zoomIn;
    private readonly Border appearancePanel;
    private readonly Border photographyPanel;
    private readonly TextBlock skinLabel = PanoramaTheme.Label("羽色", 12, true);
    private readonly Button[] settingsTabs;
    private readonly Button[] backgroundButtons = new Button[4];
    private readonly Grid settingsContent = new();
    private readonly Button capture;
    private readonly Border editGlyph;
    private readonly Button authorLink;
    private readonly List<TextBlock> sceneLabels = [];
    private Brush photoBackground = Brushes.Transparent;
    private IReadOnlyList<PetAppearance> appearances = [];
    private IReadOnlySet<string> available = new HashSet<string>();
    private PetAppearance? appearance;
    private string petName = "小鸡";
    private string current = "idle";
    private bool updating, selecting, takingPhoto;
    private int selectedPanel = 0;
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
        Width = Math.Min(1080, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(800, SystemParameters.WorkArea.Height - 40);
        MinWidth = 820; MinHeight = 620;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(25, 28, 30));
        WindowStartupLocation = WindowStartupLocation.CenterScreen; UseLayoutRounding = true;
        PanoramaTheme.Apply(this);
        // Handle the complete non-client area; WS_THICKFRAME alone exposes an
        // OS-coloured strip. Caption hit testing also gives native window dragging.
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 72, ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false, NonClientFrameEdges = System.Windows.Shell.NonClientFrameEdges.None,
        });
        var frame = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(61, 65, 67)), BorderThickness = new Thickness(1) };
        var root = new Grid { Background = Brushes.Transparent };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        frame.Child = root; Content = frame; root.MouseLeftButtonDown += MoveFromEmptySurface;

        var header = new Grid { Margin = new Thickness(28, 18, 18, 0), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        heading.MaxWidth = 460; heading.TextTrimming = TextTrimming.CharacterEllipsis; nameRow.Children.Add(heading);
        var edit = CreateIcon("edit_label", "给宠物起名", BeginNameEdit, 32); edit.Margin = new Thickness(10, 0, 0, 0);
        editGlyph = new Border { Width = 20, Height = 20, Background = PanoramaTheme.Muted,
            OpacityMask = new ImageBrush(icons.Get("edit_label")) { Stretch = Stretch.Uniform } };
        edit.Content = editGlyph;
        nameRow.Children.Add(edit); title.Children.Add(nameRow);
        subtitle.Margin = new Thickness(0, 5, 0, 0); title.Children.Add(subtitle); header.Children.Add(title);
        var chrome = new StackPanel { Orientation = Orientation.Horizontal };
        chrome.Children.Add(CreateIcon("settings", "桌宠设置", () => MoreSettingsRequested?.Invoke(), 36));
        chrome.Children.Add(CreateIcon("minimize", "最小化", () => WindowState = WindowState.Minimized, 36));
        chrome.Children.Add(CreateIcon("close", "回到桌面 · Esc", Close, 36));
        var chromePanel = Surface(chrome, new Thickness(2)); chromePanel.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(chromePanel, 1); header.Children.Add(chromePanel); root.Children.Add(header);
        var editRow = new StackPanel { Orientation = Orientation.Horizontal }; editRow.Children.Add(nameEntry);
        saveName = CreateIcon("check", "保存名称 · Enter", SaveName, 36); saveName.Margin = new Thickness(6, 0, 0, 0); editRow.Children.Add(saveName);
        nameEditor = Surface(editRow, new Thickness(10)); nameEditor.Visibility = Visibility.Collapsed;
        nameEditor.HorizontalAlignment = HorizontalAlignment.Left; nameEditor.VerticalAlignment = VerticalAlignment.Top;
        nameEditor.Margin = new Thickness(28, 70, 0, 0); Grid.SetRowSpan(nameEditor, 2);
        Panel.SetZIndex(nameEditor, 5); root.Children.Add(nameEditor);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(nameEditor, true);
        nameEntry.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SaveName(); e.Handled = true; } };

        var body = new Grid { Margin = new Thickness(20, 0, 24, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        Grid.SetRow(body, 1); root.Children.Add(body);
        var previewArea = new Grid { Margin = new Thickness(0, 0, 20, 0) };
        previewArea.RowDefinitions.Add(new RowDefinition());
        previewArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(previewArea);
        photoCanvas.Children.Add(new Image { Source = preview, Width = 512, Height = 512, Stretch = Stretch.Fill, IsHitTestVisible = false });
        stage.Children.Add(new Viewbox { Child = photoCanvas, Stretch = Stretch.Uniform, ClipToBounds = true, Margin = new Thickness(16, 0, 16, 0) });
        previewArea.Children.Add(stage); photoCanvas.Focusable = true;
        photoCanvas.MouseLeftButtonDown += (_, e) =>
        {
            var point = e.GetPosition(photoCanvas);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || !HitsPet(point)) { MoveWindow(e); return; }
            photoCanvas.Focus(); dragPoint = point; photoCanvas.CaptureMouse(); e.Handled = true;
        };
        photoCanvas.MouseMove += (_, e) =>
        {
            if (dragPoint is not { } previous || e.LeftButton != MouseButtonState.Pressed) return;
            var next = e.GetPosition(photoCanvas); OrbitRequested?.Invoke((float)(next.X - previous.X) * .5f); dragPoint = next; e.Handled = true;
        };
        photoCanvas.MouseLeftButtonUp += (_, e) => { dragPoint = null; photoCanvas.ReleaseMouseCapture(); e.Handled = true; };
        photoCanvas.LostMouseCapture += (_, _) => dragPoint = null;
        stage.MouseWheel += (_, e) => { SetZoom(zoom + (e.Delta > 0 ? .1f : -.1f)); e.Handled = true; };
        stage.MouseLeftButtonDown += (_, e) => { if (!e.Handled) MoveWindow(e); };

        var camera = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        camera.Children.Add(CreateIcon("left", "向左旋转", () => OrbitRequested?.Invoke(-15), 34));
        camera.Children.Add(CreateIcon("right", "向右旋转", () => OrbitRequested?.Invoke(15), 34));
        camera.Children.Add(CreateIcon("reset", "恢复初始视角", () => { SetZoom(1); ResetViewRequested?.Invoke(); }, 34));
        camera.Children.Add(Divider());
        zoomOut = CreateIcon("camera_zoom_out", "拉远 · 滚轮向下", () => SetZoom(zoom - .1f), 34);
        zoomIn = CreateIcon("camera_zoom_in", "拉近 · 滚轮向上", () => SetZoom(zoom + .1f), 34);
        camera.Children.Add(zoomOut); zoomText.Width = 46; zoomText.TextAlignment = TextAlignment.Center;
        camera.Children.Add(zoomText); camera.Children.Add(zoomIn);
        var cameraPanel = Surface(camera, new Thickness(6, 2, 6, 2));
        cameraPanel.HorizontalAlignment = HorizontalAlignment.Center; cameraPanel.VerticalAlignment = VerticalAlignment.Bottom;
        cameraPanel.Margin = new Thickness(0, 0, 0, 10); stage.Children.Add(cameraPanel);
        var hint = PanoramaTheme.Label("拖动小鸡旋转   ·   滚轮缩放   ·   拖动空白处移动窗口", 11, true);
        hint.HorizontalAlignment = HorizontalAlignment.Center; hint.Margin = new Thickness(0, 6, 0, 12);
        Grid.SetRow(hint, 1); previewArea.Children.Add(hint); sceneLabels.Add(hint);
        var interaction = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        state.HorizontalAlignment = HorizontalAlignment.Center; state.Margin = new Thickness(0, 0, 0, 8); interaction.Children.Add(state);
        var dock = new StackPanel { Orientation = Orientation.Horizontal };
        AddBasic(dock, "喂食", "feed", "pet_feed"); AddBasic(dock, "睡觉", "sleep", "pet_activity_sleep"); AddBasic(dock, "停止", "idle", "stop");
        dock.Children.Add(Divider());
        foreach (var activity in activities)
        {
            var button = ActionButton(activity.Icon, activity.Label); button.Tag = activity.Id;
            button.Click += (_, _) => ActionRequested?.Invoke(activity.Id);
            ToolTipService.SetShowOnDisabled(button, true); actionButtons[activity.Id] = button; actionRow.Children.Add(button);
        }
        dock.Children.Add(actionRow); interaction.Children.Add(Surface(dock, new Thickness(6, 4, 6, 4)));
        Grid.SetRow(interaction, 2); previewArea.Children.Add(interaction);

        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) };
        var tabs = new UniformGrid { Columns = 2 };
        settingsTabs = [new Button { Content = "外观", MinHeight = 38 }, new Button { Content = "摄影", MinHeight = 38 }];
        settingsTabs[0].Click += (_, _) => SelectPanel(0); settingsTabs[1].Click += (_, _) => SelectPanel(1);
        foreach (var tab in settingsTabs) tabs.Children.Add(tab);
        controls.Children.Add(Surface(tabs, new Thickness(4)));
        var appearanceSettings = new StackPanel(); Section(appearanceSettings, "宠物"); appearanceSettings.Children.Add(models);
        skinLabel.Margin = new Thickness(0, 18, 0, 8); appearanceSettings.Children.Add(skinLabel); appearanceSettings.Children.Add(skins);
        appearancePanel = Surface(appearanceSettings, new Thickness(18));
        var photoSettings = new StackPanel(); Section(photoSettings, "背景");
        var swatches = new UniformGrid { Columns = 2 };
        for (var index = 0; index < backgroundButtons.Length; index++)
        {
            var value = index; var content = new StackPanel();
            content.Children.Add(new Border { Width = 82, Height = 45, CornerRadius = new CornerRadius(2),
                Background = index == 3 ? Checkerboard() : BackgroundFor(index) });
            var label = PanoramaTheme.Label(backgrounds.Items[index].ToString()!, 11);
            label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 7, 0, 0); content.Children.Add(label);
            var button = new Button { Content = content, Margin = new Thickness(2), Padding = new Thickness(5, 7, 5, 7),
                ToolTip = index == 3 ? "保存带透明背景的 PNG" : "使用" + backgrounds.Items[index] + "背景" };
            button.Click += (_, _) => backgrounds.SelectedIndex = value; backgroundButtons[index] = button; swatches.Children.Add(button);
        }
        photoSettings.Children.Add(swatches);
        var photoNote = PanoramaTheme.Label("选择背景，即可预览拍照效果。", 11, true); photoNote.Margin = new Thickness(0, 14, 0, 0);
        photoSettings.Children.Add(photoNote); photographyPanel = Surface(photoSettings, new Thickness(18));
        settingsContent.Margin = new Thickness(0, 10, 0, 0);
        settingsContent.Children.Add(appearancePanel); settingsContent.Children.Add(photographyPanel); controls.Children.Add(settingsContent);
        var photography = new StackPanel();
        capture = new Button { MinHeight = 46, Background = new SolidColorBrush(Color.FromRgb(84, 87, 80)), ToolTip = "保存当前姿态 · Ctrl+P" };
        var captureContent = new StackPanel { Orientation = Orientation.Horizontal }; captureContent.Children.Add(icons.Create("photo", 22));
        var captureLabel = PanoramaTheme.Label("拍照", 14); captureLabel.Margin = new Thickness(10, 0, 0, 0); captureContent.Children.Add(captureLabel);
        capture.Content = captureContent; capture.Click += async (_, _) => await TakePhotoAsync(); photography.Children.Add(capture);
        var album = new Button { Content = "照片库", MinHeight = 38, BorderBrush = PanoramaTheme.Border, Margin = new Thickness(0, 8, 0, 0) };
        album.Click += (_, _) => OpenLibrary(); photography.Children.Add(album);
        notice.TextWrapping = TextWrapping.Wrap; notice.LineHeight = 17; notice.Margin = new Thickness(0, 12, 0, 0);
        photography.Children.Add(notice);
        var capturePanel = Surface(photography, new Thickness(18)); capturePanel.Margin = new Thickness(0, 16, 0, 0);
        controls.Children.Add(capturePanel);
        var scroll = new ScrollViewer { Content = controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1); body.Children.Add(scroll);

        var footer = new DockPanel { Margin = new Thickness(28, 5, 22, 0) };
        authorLink = new Button { Content = "niceday_zhu · GitHub", Padding = new Thickness(4, 3, 4, 3), FontSize = 11, Opacity = .75 };
        authorLink.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/nicedayzhu") { UseShellExecute = true });
        DockPanel.SetDock(authorLink, Dock.Right); footer.Children.Add(authorLink);
        var credit = PanoramaTheme.Label("CS2 鸡桌宠  /  Source 2 Viewer · VRF", 11, true); footer.Children.Add(credit); sceneLabels.Add(credit);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        models.SelectionChanged += (_, _) =>
        {
            if (updating || models.SelectedItem is not PetAppearance model) return;
            var selected = appearances.FirstOrDefault(item => item.ModelId == model.ModelId && item.Skin is null) ?? appearances.First(item => item.ModelId == model.ModelId);
            RequestAppearance(selected.Id);
        };
        skins.SelectionChanged += (_, _) => { if (!updating && skins.SelectedItem is PetAppearance selected) RequestAppearance(selected.Id); };
        backgrounds.SelectionChanged += (_, _) => UpdateBackground();
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) { if (nameEditor.IsVisible) EndNameEdit(); else Close(); e.Handled = true; }
            else if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { await TakePhotoAsync(); e.Handled = true; }
            else if (e.Key == Key.Space && Keyboard.FocusedElement is not (TextBox or ComboBox))
            { ActionRequested?.Invoke(current == "sleep" ? "wake" : "idle"); e.Handled = true; }
        };
        UpdateBackground(); SelectPanel(0); SetState("idle");
    }

    private Button CreateIcon(string name, string tip, Action clicked, double size)
    {
        var button = PanoramaTheme.IconButton(icons, name, tip, clicked, size);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(button, true); return button;
    }
    private static Border Surface(UIElement content, Thickness padding) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(225, 43, 47, 49)), CornerRadius = new CornerRadius(6),
        BorderBrush = new SolidColorBrush(Color.FromArgb(45, 220, 226, 227)), BorderThickness = new Thickness(1), Padding = padding, Child = content,
    };
    private static Border Divider() => new() { Width = 1, Height = 26, Background = PanoramaTheme.Border, Margin = new Thickness(8, 0, 8, 0), Opacity = .5 };
    private static void Section(StackPanel parent, string text)
    { var label = PanoramaTheme.Label(text, 12, true); label.Margin = new Thickness(0, 0, 0, 9); parent.Children.Add(label); }
    private Button ActionButton(string icon, string text)
    {
        var content = new StackPanel(); content.Children.Add(icons.Create(icon, 24));
        var label = PanoramaTheme.Label(text, 11); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 6, 0, 0); content.Children.Add(label);
        var button = new Button { Content = content, Width = 46, Height = 59, Padding = new Thickness(3), ToolTip = text };
        captions[button] = label; return button;
    }
    private void AddBasic(StackPanel row, string label, string action, string icon)
    {
        var button = ActionButton(icon, label); button.Tag = action;
        button.Click += (_, _) => { var selected = (string)button.Tag; ActionRequested?.Invoke(selected == "sleep" && current == "sleep" ? "wake" : selected); };
        ToolTipService.SetShowOnDisabled(button, true); basicButtons[action] = button; row.Children.Add(button);
    }
    private void SelectPanel(int index)
    {
        selectedPanel = selectedPanel == index && settingsContent.IsVisible ? -1 : index;
        appearancePanel.Visibility = selectedPanel == 0 ? Visibility.Visible : Visibility.Collapsed;
        photographyPanel.Visibility = selectedPanel == 1 ? Visibility.Visible : Visibility.Collapsed;
        settingsContent.Visibility = selectedPanel == -1 ? Visibility.Collapsed : Visibility.Visible;
        for (var i = 0; i < settingsTabs.Length; i++)
        {
            settingsTabs[i].Background = selectedPanel == i ? new SolidColorBrush(Color.FromArgb(32, 226, 219, 190)) : Brushes.Transparent;
            settingsTabs[i].Foreground = selectedPanel == i ? PanoramaTheme.Accent : PanoramaTheme.Muted;
            settingsTabs[i].BorderBrush = selectedPanel == i ? new SolidColorBrush(Color.FromArgb(90, 214, 201, 153)) : Brushes.Transparent;
        }
    }
    private void BeginNameEdit() { nameEntry.Text = petName; nameEditor.Visibility = Visibility.Visible; nameEntry.Focus(); nameEntry.SelectAll(); }
    private void EndNameEdit() { nameEditor.Visibility = Visibility.Collapsed; nameEntry.Text = petName; stage.Focus(); }
    internal bool HitsPet(Point point)
    {
        var x = (int)point.X; var y = (int)point.Y;
        if (x < 0 || x >= preview.PixelWidth || y < 0 || y >= preview.PixelHeight) return false;
        var pixel = new byte[4]; preview.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel[3] >= 32;
    }
    private void MoveFromEmptySurface(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or TextBoxBase or Selector or RangeBase || node == photoCanvas) return;
            if (node == sender) break;
        }
        MoveWindow(e);
    }
    private void MoveWindow(MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        e.Handled = true; try { DragMove(); } catch (InvalidOperationException) { }
    }

    public void SetCatalog(IReadOnlyList<PetAppearance> catalog)
    { appearances = catalog; updating = true; models.ItemsSource = catalog.DistinctBy(item => item.ModelId).ToArray(); updating = false; }
    public void SetAppearance(PetAppearance selected, IReadOnlySet<string> actions, string name)
    {
        appearance = selected; available = actions; petName = name; heading.Text = name; Title = $"{name} · 检视与摄影棚";
        subtitle.Text = selected.GroupLabel + (selected.Skin is null ? "" : " · " + selected.Label) + "  /  检视 · 摄影棚";
        updating = true; models.SelectedItem = models.Items.Cast<PetAppearance>().FirstOrDefault(item => item.ModelId == selected.ModelId);
        skins.ItemsSource = appearances.Where(item => item.ModelId == selected.ModelId).ToArray(); skins.SelectedItem = selected;
        skinLabel.Visibility = skins.Visibility = skins.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        nameEntry.Text = name; updating = false; EndNameEdit(); SetSelecting(false); SetState(current);
        actionRow.Visibility = selected.Kind == PetKind.Chicken ? Visibility.Visible : Visibility.Collapsed;
        if (basicButtons.TryGetValue("feed", out var feed)) { captions[feed].Text = selected.Kind == PetKind.Egg ? "破壳" : "喂食"; feed.Tag = selected.Kind == PetKind.Egg ? "trick" : "feed"; }
    }
    public void SetSelecting(bool value)
    {
        selecting = value; models.IsEnabled = !value; skins.IsEnabled = !value && skins.Items.Count > 1;
        nameEntry.IsEnabled = saveName.IsEnabled = !value; SetState(current);
    }
    public void SetState(string action)
    {
        current = action; var busy = !PetActions.Loops(action);
        var label = appearance?.Kind == PetKind.Egg && action is "trick" or "trick2" ? "破壳" : PetActions.Label(action);
        state.Text = selecting ? "正在切换宠物…" : action == "sleep" ? "睡梦中 · 点击叫醒" : busy ? "正在" + label : "自在休息";
        state.Foreground = backgrounds.SelectedIndex == 1 ? new SolidColorBrush(Color.FromRgb(59, 65, 65)) : busy ? PanoramaTheme.Accent : PanoramaTheme.Muted;
        foreach (var activity in activities)
        {
            var button = actionButtons[activity.Id]; var supported = available.Contains(activity.Id);
            button.IsEnabled = supported && !selecting && !busy;
            button.Background = action == activity.Id ? new SolidColorBrush(Color.FromArgb(45, 221, 210, 165)) : Brushes.Transparent;
            button.ToolTip = !supported ? $"当前宠物不支持{activity.Label}" : busy ? "当前动作播放完即可选择" : activity.Label;
        }
        foreach (var (id, button) in basicButtons)
        {
            var key = id == "feed" && appearance?.Kind == PetKind.Egg ? "trick" : id;
            button.IsEnabled = !selecting && (key == "idle" || available.Contains(key)) && (!busy || key == "idle");
            button.ToolTip = key == "idle" ? "停止当前动作 · 空格" : available.Contains(key) ? PetActions.Label(key) : "当前宠物没有这个动作";
            if (id == "sleep") captions[button].Text = action == "sleep" ? "叫醒" : "睡觉";
        }
        capture.IsEnabled = !selecting && !takingPhoto;
    }
    public void UpdateName(string name) { petName = name; heading.Text = name; Title = $"{name} · 检视与摄影棚"; nameEntry.Text = name; notice.Text = "名称已保存"; EndNameEdit(); }
    private void RequestAppearance(string id) { if (id != appearance?.Id) { SetSelecting(true); AppearanceRequested?.Invoke(id); } }
    private void SaveName() { NameRequested?.Invoke(nameEntry.Text.Trim()); EndNameEdit(); }
    private void SetZoom(float value)
    {
        zoom = Math.Clamp(value, .7f, 1.45f); zoomText.Text = $"{zoom * 100:F0}%"; zoomOut.IsEnabled = zoom > .7001f; zoomIn.IsEnabled = zoom < 1.4499f; ZoomRequested?.Invoke(zoom);
    }
    private static Brush BackgroundFor(int index) => index switch
    {
        1 => new SolidColorBrush(Color.FromRgb(208, 212, 211)), 2 => new SolidColorBrush(Color.FromRgb(20, 23, 25)), 3 => Brushes.Transparent,
        _ => new RadialGradientBrush(Color.FromRgb(84, 87, 82), Color.FromRgb(26, 30, 32))
        { GradientOrigin = new Point(.4, .4), Center = new Point(.4, .4), RadiusX = .75, RadiusY = .8 },
    };
    private static DrawingBrush Checkerboard()
    {
        var group = new DrawingGroup(); var light = new SolidColorBrush(Color.FromRgb(62, 67, 70));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(48, 53, 56)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        var result = new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16), Stretch = Stretch.None };
        result.Freeze(); return result;
    }
    private void UpdateBackground()
    {
        photoBackground = BackgroundFor(backgrounds.SelectedIndex);
        ((Border)Content).Background = backgrounds.SelectedIndex == 3 ? BackgroundFor(2) : photoBackground;
        heading.Foreground = backgrounds.SelectedIndex == 1 ? new SolidColorBrush(Color.FromRgb(43, 48, 50)) : PanoramaTheme.Text;
        subtitle.Foreground = backgrounds.SelectedIndex == 1 ? heading.Foreground : PanoramaTheme.Muted;
        editGlyph.Background = backgrounds.SelectedIndex == 1 ? heading.Foreground : PanoramaTheme.Muted;
        authorLink.Foreground = backgrounds.SelectedIndex == 1 ? heading.Foreground : PanoramaTheme.Muted;
        foreach (var label in sceneLabels) label.Foreground = backgrounds.SelectedIndex == 1 ? heading.Foreground : PanoramaTheme.Muted;
        for (var i = 0; i < backgroundButtons.Length; i++) backgroundButtons[i].BorderBrush = i == backgrounds.SelectedIndex ? PanoramaTheme.Accent : Brushes.Transparent;
        SetState(current);
    }
    internal BitmapSource CapturePhoto()
    {
        if (backgrounds.SelectedIndex == 3) { var transparent = preview.Clone(); transparent.Freeze(); return transparent; }
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(photoBackground, null, new Rect(0, 0, 512, 512)); drawing.DrawImage(preview, new Rect(0, 0, 512, 512)); }
        var bitmap = new RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal async Task TakePhotoAsync()
    {
        if (takingPhoto || appearance is null || selecting) return;
        takingPhoto = true; capture.IsEnabled = false;
        try { var path = await library.SaveAsync(CapturePhoto(), appearance.Id, petName, backgrounds.SelectedItem?.ToString() ?? "摄影棚"); notice.Text = "照片已保存 · 可在照片库查看"; ErrorLog.Trace($"saved photo {path}"); }
        catch (Exception ex) { notice.Text = "照片保存失败：" + ex.Message; ErrorLog.Write(ex); }
        finally { takingPhoto = false; capture.IsEnabled = !selecting; }
    }
    private void OpenLibrary()
    {
        try { new PhotoLibraryWindow(library) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { notice.Text = "无法打开照片库：" + ex.Message; ErrorLog.Write(ex); }
    }
}
