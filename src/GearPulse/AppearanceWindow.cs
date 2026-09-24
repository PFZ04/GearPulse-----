using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using Orientation = System.Windows.Controls.Orientation;

namespace GearPulse;

public sealed class AppearanceWindow : Window
{
    private WidgetSettings settings;
    private readonly Action<WidgetSettings> changed;
    private bool ready;

    public AppearanceWindow(WidgetSettings initial, Action<WidgetSettings> onChanged)
    {
        settings = initial;
        changed = onChanged;
        Width = 370;
        Height = 440;
        MinWidth = 340;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        BuildControls();
    }

    public void RefreshLanguage() => BuildControls();

    private void BuildControls()
    {
        ready = false;
        Title = UiLanguage.AppearanceMenu;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(Choice(UiLanguage.IconStyleLabel,
            [("line", UiLanguage.LineIcons), ("silhouette", UiLanguage.SilhouetteIcons)],
            settings.IconStyle, value => settings = settings with { IconStyle = value }));
        panel.Children.Add(Choice(UiLanguage.SizeLabel,
            [("small", UiLanguage.SmallSize), ("medium", UiLanguage.MediumSize), ("large", UiLanguage.LargeSize)],
            settings.Size, value => settings = settings with { Size = value }));
        panel.Children.Add(Slider(UiLanguage.BackgroundOpacityLabel, settings.BackgroundOpacity,
            value => settings = settings with { BackgroundOpacity = value }));
        panel.Children.Add(Slider(UiLanguage.ContentOpacityLabel, settings.ContentOpacity,
            value => settings = settings with { ContentOpacity = value }));
        var displays = new List<(string, string)> { ("", UiLanguage.PrimaryDisplay) };
        displays.AddRange(Screen.AllScreens.Select((screen, index) =>
            (screen.DeviceName, $"{index + 1}: {screen.DeviceName}")));
        if (settings.Monitor is { } selected && displays.All(item => item.Item1 != selected))
            displays.Add((selected, $"{selected} ({UiLanguage.DisconnectedDisplay})"));
        panel.Children.Add(Choice(UiLanguage.MonitorLabel, displays, settings.Monitor ?? "",
            value => settings = settings with { Monitor = value.Length == 0 ? null : value }));
        panel.Children.Add(Choice(UiLanguage.CornerLabel,
            [("top-left", UiLanguage.TopLeft), ("top-right", UiLanguage.TopRight),
             ("bottom-left", UiLanguage.BottomLeft), ("bottom-right", UiLanguage.BottomRight)],
            settings.Corner, value => settings = settings with { Corner = value }));
        panel.Children.Add(Toggle(UiLanguage.ShowWiredHeadsets, settings.ShowWiredHeadsets,
            value => settings = settings with { ShowWiredHeadsets = value }));
        panel.Children.Add(Toggle(UiLanguage.ShowBluetoothHeadsets, settings.ShowBluetoothHeadsets,
            value => settings = settings with { ShowBluetoothHeadsets = value }));
        panel.Children.Add(Toggle(UiLanguage.HideUnreadableInformation, settings.HideUnreadableInformation,
            value => settings = settings with { HideUnreadableInformation = value }));
        Content = new ScrollViewer { Content = panel, Background = System.Windows.Media.Brushes.White,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ready = true;
    }

    private FrameworkElement Choice(string label, IEnumerable<(string Value, string Text)> items,
        string selected, Action<string> update)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 5) });
        var box = new System.Windows.Controls.ComboBox { Height = 29 };
        foreach (var (value, text) in items)
            box.Items.Add(new ComboBoxItem { Tag = value, Content = text });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected);
        box.SelectionChanged += (_, _) =>
        {
            if (!ready || box.SelectedItem is not ComboBoxItem item) return;
            update((string)item.Tag);
            Changed();
        };
        stack.Children.Add(box);
        return stack;
    }

    private FrameworkElement Slider(string label, int initial, Action<int> update)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var header = new DockPanel { LastChildFill = false };
        header.Children.Add(new TextBlock { Text = label });
        var percent = new TextBlock { Text = $"{initial}%", HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        DockPanel.SetDock(percent, Dock.Right);
        header.Children.Add(percent);
        stack.Children.Add(header);
        var slider = new System.Windows.Controls.Slider
        {
            Minimum = 0, Maximum = 100, Value = initial, TickFrequency = 1,
            IsSnapToTickEnabled = true, Margin = new Thickness(0, 5, 0, 0)
        };
        slider.ValueChanged += (_, _) =>
        {
            var value = (int)slider.Value;
            percent.Text = $"{value}%";
            if (!ready) return;
            update(value);
            Changed();
        };
        stack.Children.Add(slider);
        return stack;
    }

    private FrameworkElement Toggle(string label, bool initial, Action<bool> update)
    {
        var box = new System.Windows.Controls.CheckBox
        {
            Content = label, IsChecked = initial, Margin = new Thickness(0, 0, 0, 14)
        };
        box.Checked += (_, _) => { if (ready) { update(true); Changed(); } };
        box.Unchecked += (_, _) => { if (ready) { update(false); Changed(); } };
        return box;
    }

    private void Changed()
    {
        settings = settings.Normalized();
        changed(settings);
    }
}
