using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;

namespace ChickenDesktopPet3D;

internal sealed class PhotoLibraryWindow : Window
{
    public PhotoLibraryWindow(PhotoLibrary library)
    {
        Title = "鸡桌宠 · 照片库"; Width = 760; Height = 560; MinWidth = 460; MinHeight = 320;
        Background = new SolidColorBrush(Color.FromRgb(35, 37, 39));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        PanoramaTheme.Apply(this);
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = new Button { Content = "关闭", Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var folder = new Button { Content = "打开照片文件夹" };
        folder.Click += (_, _) => library.OpenFolder();
        DockPanel.SetDock(folder, Dock.Right); header.Children.Add(folder);
        header.Children.Add(PanoramaTheme.Label("我的照片", 23));
        header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) { try { DragMove(); } catch (InvalidOperationException) { } } };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var photos = library.ReadRecent();
        var cards = new WrapPanel();
        foreach (var photo in photos)
        {
            try
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 200; image.UriSource = new Uri(photo.Path); image.EndInit(); image.Freeze();
                var content = new StackPanel();
                content.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(57, 59, 59)),
                    Width = 190, Height = 180, CornerRadius = new CornerRadius(5), Child = new Image { Source = image, Stretch = Stretch.Uniform } });
                var name = PanoramaTheme.Label(photo.Name, 13); name.Margin = new Thickness(2, 7, 0, 0);
                name.MaxWidth = 185; name.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(name);
                var date = PanoramaTheme.Label(photo.Taken.ToString("MM-dd HH:mm"), 11, true);
                date.Margin = new Thickness(2, 4, 0, 0); content.Children.Add(date);
                var button = new Button { Content = content, Margin = new Thickness(0, 0, 10, 14), Padding = new Thickness(4), ToolTip = "打开原图" };
                button.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(photo.Path) { UseShellExecute = true });
                cards.Children.Add(button);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException) { ErrorLog.Trace(ex.Message); }
        }
        if (photos.Count == 0)
            root.Children.Add(new TextBlock { Text = "还没有照片\n在检视窗口选择背景，点击拍照即可保存。", Foreground = PanoramaTheme.Muted,
                FontFamily = PanoramaTheme.Font, FontSize = 15, LineHeight = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        else root.Children.Add(new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = new Border { BorderBrush = PanoramaTheme.Border, BorderThickness = new Thickness(1), Child = root };
    }
}
