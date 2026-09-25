using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;

namespace ChickenDesktopPet3D;

internal sealed class QuickActionBar : Border
{
    private readonly StackPanel buttons = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
    public event Action<string>? ActionRequested;
    public bool HasActions => buttons.Children.Count > 0;

    public QuickActionBar()
    {
        CornerRadius = new CornerRadius(18);
        Background = new SolidColorBrush(Color.FromArgb(240, 30, 34, 40));
        BorderBrush = new SolidColorBrush(Color.FromArgb(65, 255, 255, 255));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(5);
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 12);
        Visibility = Visibility.Collapsed;
        Child = buttons;
    }

    public void SetActions(IReadOnlySet<string> actions, bool egg)
    {
        buttons.Children.Clear();
        Add("喂食", "feed", actions.Contains("feed"));
        Add(egg ? "破壳" : "表演", "trick", actions.Contains("trick") || actions.Contains("trick2"));
        Add("睡觉", "sleep", actions.Contains("sleep"));
        Add("叫醒", "wake", actions.Contains("react") || actions.Contains("react2") || actions.Contains("idle"));
        if (!HasActions) Visibility = Visibility.Collapsed;
    }

    private void Add(string label, string action, bool enabled)
    {
        if (!enabled) return;
        var border = new FrameworkElementFactory(typeof(Border), "surface");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(70, 77, 87)), "surface"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(95, 86, 57)), "surface"));
        template.Triggers.Add(pressed);
        var button = new Button
        {
            Content = label, Width = 46, Height = 28, FontSize = 12,
            Foreground = Brushes.White, Cursor = Cursors.Hand, Template = template,
            ToolTip = label, Focusable = false,
        };
        button.Click += (_, _) => ActionRequested?.Invoke(action);
        buttons.Children.Add(button);
    }
}
