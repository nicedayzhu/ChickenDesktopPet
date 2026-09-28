using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ChickenDesktopPet3D;

internal sealed class QuickActionBar : Border
{
    private readonly StackPanel root = new();
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal };
    private readonly UniformGrid activityGrid = new() { Columns = 4, Margin = new Thickness(0, 5, 0, 0) };
    private readonly TextBlock title = PanoramaTheme.Label("小鸡");
    private readonly TextBlock status = PanoramaTheme.Label("自在休息", 11, true);
    private readonly Dictionary<string, Button> activityButtons = new(StringComparer.Ordinal);
    private readonly PetIconStore icons;
    private IReadOnlyList<PetActivity> activities = PetUiResources.DefaultActivities;
    private IReadOnlySet<string> available = new HashSet<string>();
    private Button? feed;
    private Button? sleep;
    private Button? actionToggle;
    private bool egg;
    private string current = "idle";
    public event Action<string>? ActionRequested;
    public event Action? SizeChangedByContent;
    public bool HasActions { get; private set; }
    public bool Expanded => activityGrid.Visibility == Visibility.Visible;

    public QuickActionBar(PetIconStore? iconStore = null)
    {
        icons = iconStore ?? new PetIconStore();
        PanoramaTheme.Apply(this);
        CornerRadius = new CornerRadius(8);
        Background = PanoramaTheme.Panel;
        BorderBrush = PanoramaTheme.Border;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(6);
        Visibility = Visibility.Collapsed;
        var heading = new Grid { Margin = new Thickness(7, 3, 7, 7) };
        title.MaxWidth = 135; title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        status.HorizontalAlignment = HorizontalAlignment.Right;
        heading.Children.Add(title); heading.Children.Add(status);
        root.Children.Add(heading);
        root.Children.Add(buttons);
        activityGrid.Visibility = Visibility.Collapsed;
        root.Children.Add(activityGrid);
        Child = root;
    }

    public void SetResources(PetUiResources resources)
    {
        activities = resources.Activities;
        Rebuild();
    }

    public void SetTitle(string name) => title.Text = name;

    public void SetActions(IReadOnlySet<string> actions, bool isEgg)
    {
        available = actions;
        egg = isEgg;
        HasActions = true;
        Rebuild();
    }

    private void Rebuild()
    {
        buttons.Children.Clear(); activityGrid.Children.Clear(); activityButtons.Clear();
        feed = AddMain("喂食", "pet_feed", () => Request("feed"));
        actionToggle = AddMain(egg ? "破壳" : "动作", "pet_activity_trick", () =>
        {
            if (egg) Request("trick");
            else
            {
                activityGrid.Visibility = Expanded ? Visibility.Collapsed : Visibility.Visible;
                SizeChangedByContent?.Invoke();
            }
        });
        if (egg || !activities.Any(activity => available.Contains(activity.Id))) activityGrid.Visibility = Visibility.Collapsed;
        sleep = AddMain("睡觉", "pet_activity_sleep", () => Request(current == "sleep" ? "wake" : "sleep"));
        AddMain("检视", "zoom_in", () => Request("inspect"));
        AddMain("拍照", "photo", () => Request("photo"));
        foreach (var activity in activities)
        {
            var content = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            content.Children.Add(icons.Create(activity.Icon));
            var label = PanoramaTheme.Label(activity.Label, 11);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 4, 0, 0); content.Children.Add(label);
            var button = new Button { Content = content, Width = 58, Height = 58, Padding = new Thickness(3), Focusable = false, Tag = activity.Id };
            button.Click += (_, _) => Request(activity.Id);
            ToolTipService.SetShowOnDisabled(button, true);
            activityButtons.Add(activity.Id, button); activityGrid.Children.Add(button);
        }
        SetState(current);
    }

    private Button AddMain(string label, string icon, Action clicked)
    {
        var content = new StackPanel();
        content.Children.Add(icons.Create(icon, 24));
        var text = PanoramaTheme.Label(label, 11); text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, 4, 0, 0); content.Children.Add(text);
        var button = new Button { Content = content, Width = 48, Height = 56,
            Padding = new Thickness(3), Focusable = false, ToolTip = label };
        button.Click += (_, _) => clicked();
        ToolTipService.SetShowOnDisabled(button, true);
        buttons.Children.Add(button);
        return button;
    }

    public void SetState(string action)
    {
        current = action;
        var busy = !PetActions.Loops(action);
        if (actionToggle is not null)
        {
            actionToggle.IsEnabled = egg ? available.Contains("trick") && !busy : activities.Any(activity => available.Contains(activity.Id));
            actionToggle.ToolTip = actionToggle.IsEnabled ? egg ? "破壳" : "选择动作" : "当前宠物没有可用的动作";
        }
        var label = egg && action is "trick" or "trick2" ? "破壳" : PetActions.Label(action);
        status.Text = action == "sleep" ? "睡梦中" : busy ? label + "中" : "自在休息";
        status.Foreground = busy ? PanoramaTheme.Accent : PanoramaTheme.Muted;
        if (feed is not null)
        {
            feed.IsEnabled = available.Contains("feed") && !busy;
            feed.ToolTip = available.Contains("feed") ? "喂食 · 鼠标中键" : "这个宠物没有喂食动作";
        }
        if (sleep is not null)
        {
            sleep.IsEnabled = !busy && (action == "sleep" || available.Contains("sleep"));
            ((TextBlock)((StackPanel)sleep.Content).Children[1]).Text = action == "sleep" ? "叫醒" : "睡觉";
            sleep.ToolTip = action == "sleep" ? "叫醒" : available.Contains("sleep") ? "睡觉" : "这个宠物没有睡觉动作";
        }
        foreach (var activity in activities)
        {
            if (!activityButtons.TryGetValue(activity.Id, out var button)) continue;
            button.IsEnabled = available.Contains(activity.Id) && !busy;
            button.Background = action == activity.Id ? new SolidColorBrush(Color.FromRgb(92, 91, 78)) : Brushes.Transparent;
            button.ToolTip = !available.Contains(activity.Id) ? $"当前宠物不支持{activity.Label}" : busy ? "当前动作播放完即可选择" : activity.Label;
        }
    }

    public void CollapseActivities()
    {
        activityGrid.Visibility = Visibility.Collapsed;
        SizeChangedByContent?.Invoke();
    }

    private void Request(string action) => ActionRequested?.Invoke(action);
}
