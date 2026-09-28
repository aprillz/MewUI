using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.TaskManager.Sample;

internal sealed class TaskManagerView : UserControl
{
    private readonly MonitorController _monitor = new();
    private readonly ProcessesPage _processes;
    private readonly PerformancePage _performance;

    public TaskManagerView()
    {
        _processes = new ProcessesPage(_monitor);
        _performance = new PerformancePage(_monitor);
        Build();
    }

    public void Start() => _monitor.Start();

    protected override Element OnBuild()
    {
        var navigation = new NavigationView
        {
            PaneWidth = 260,
            PaneDisplayMode = PaneDisplayMode.Auto,
        };

        var pages = new[]
        {
            new NavigationEntry("Processes", FluentIcons.Create("apps_regular"), _processes),
            new NavigationEntry("Performance", FluentIcons.Create("data_trending_regular"), _performance),
        };
        navigation.Items(pages, x => x.Title, icon: x => x.Icon, content: x => x.Content);

        var settings = new[]
        {
            new NavigationEntry("Settings", FluentIcons.Create("settings_regular"), BuildSettingsPage()),
        };
        navigation.FooterItems(settings, x => x.Title, icon: x => x.Icon, content: x => x.Content);
        navigation.SelectedIndex = 0;

        return new Border()
            .BorderThickness(new Thickness(0, 1, 0, 0))
            .WithTheme((theme, border) => border.BorderBrush(theme.Palette.ControlBorder))
            .Child(navigation);
    }

    private FrameworkElement BuildSettingsPage()
    {
        // The visual tree is built before Application.Run initializes Application.Current.
        // This sample starts in system mode and keeps the local selection in sync when changed here.
        var themeMode = new ObservableValue<ThemeVariant>(ThemeVariant.System);

        var refresh = new ComboBox();
        refresh
            .Items(["Low", "Normal", "High", "Real time"])
            .SelectedIndex(1)
            .OnSelectionChanged(selected => _monitor.IntervalMilliseconds = selected switch
            {
                "Low" => 4000,
                "High" => 1000,
                "Real time" => 500,
                _ => 2000,
            });

        return PageChrome(
            "Settings",
            new StackPanel()
                .Vertical()
                .Spacing(28)
                .Children(
                    SettingsSection(
                        "Appearance",
                        "Choose which color mode Task Manager uses.",
                        new StackPanel()
                            .Horizontal()
                            .Spacing(18)
                            .Children(
                                ThemeRadio("Use system setting", ThemeVariant.System),
                                ThemeRadio("Light", ThemeVariant.Light),
                                ThemeRadio("Dark", ThemeVariant.Dark))),
                    SettingsSection(
                        "Accent color",
                        "Colors the selection and the Processes heat map, whose cells darken as a process uses more.",
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(BuiltInAccent.Accents.Select(AccentSwatch).ToArray())),
                    SettingsSection(
                        "Real time update speed",
                        "Choose how often resource usage is refreshed.",
                        refresh.Width(220)),
                    SettingsSection(
                        "Resource access",
                        PrivilegeService.IsElevated
                            ? "Task Manager is running with elevated resource access."
                            : "Restart with elevated access to read restricted process resources.",
                        new Button()
                            .Content(new StackPanel().Horizontal().Spacing(8).Children(
                                FluentIcons.Create("shield_regular").Size(16, 16),
                                new TextBlock().Text(PrivilegeService.IsElevated ? "Elevated" : "Restart with elevated access")))
                            .IsEnabled(!PrivilegeService.IsElevated)
                            .OnClick(PrivilegeService.RestartElevated)))
        );

        Button AccentSwatch(Accent accent) => new Button()
            .CornerRadius(12)
            .MinHeight(24)
            .Width(24)
            .Height(24)
            .BorderThickness(0)
            .Content(string.Empty)
            .WithTheme((theme, button) => button.Background(accent.GetAccentColor(theme.IsDark)))
            .ToolTip(accent.ToString())
            .OnClick(() => Application.Current.SetAccent(accent));

        RadioButton ThemeRadio(string text, ThemeVariant variant) => new RadioButton()
            .Content(text)
            .BindIsChecked(themeMode, value => value == variant)
            .OnChecked(() =>
            {
                themeMode.Value = variant;
                Application.Current.SetTheme(variant);
            });
    }

    /// <summary>A page title with the page's actions on the right, as Task Manager's command bar lays them out.</summary>
    internal static FrameworkElement PageHeader(string title, params FrameworkElement[] actions) =>
        new DockPanel()
            .Padding(28, 14)
            .Spacing(12)
            .Children(
                new StackPanel().DockRight().Horizontal().Spacing(4).Children(actions),
                new TextBlock().Text(title).FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical());

    internal static Button CommandButton(string icon, string text) =>
        new Button()
            .StyleName("flat-button")
            .Padding(10, 6)
            .Content(new StackPanel().Horizontal().Spacing(7).Children(
                FluentIcons.Create(icon).Size(16, 16).CenterVertical(),
                new TextBlock().Text(text).CenterVertical()));

    internal static Button RunNewTaskButton()
    {
        var button = CommandButton("window_new_regular", "Run new task");
        button.OnClick(async () =>
        {
            var owner = button.FindVisualRoot() as Window;
            var dialog = new RunTaskWindow();
            if (owner != null) await dialog.ShowDialogAsync(owner);
            else dialog.Show();
        });
        return button;
    }

    /// <summary>The overflow button: opens <paramref name="menu"/> under itself.</summary>
    internal static Button MoreButton(ContextMenu menu)
    {
        menu.Placement = MenuPlacement.Below;
        var button = new Button()
            .StyleName("flat-button")
            .Padding(10, 6)
            .ToolTip("See more")
            .Content(FluentIcons.Create("more_regular").Size(18, 18));
        button.OnClick(() => menu.Show(button));
        return button;
    }

    /// <summary>A command that opens the platform's own system monitor, handled on <paramref name="scope"/>.</summary>
    internal static Command OpenSystemMonitorCommand(UIElement scope)
    {
        var command = new Command(
            "taskmanager.openSystemMonitor",
            OperatingSystem.IsWindows() ? "Open Resource Monitor" : OperatingSystem.IsMacOS() ? "Open Activity Monitor" : "Open system monitor");
        scope.Commands.Register(command, TaskActions.OpenSystemMonitor);
        return command;
    }

    internal static FrameworkElement PageChrome(string title, FrameworkElement content) =>
        new Grid()
            .Rows("Auto, *")
            .Children(
                new Border()
                    .Padding(28, 22, 28, 18)
                    .BorderThickness(new Thickness(0, 0, 0, 1))
                    .WithTheme((theme, border) => border.BorderBrush(theme.Palette.ControlBorder))
                    .Child(new TextBlock().Text(title).FontSize(ThemeFontSize.Medium).SemiBold()),
                new ScrollViewer()
                    .Row(1)
                    .AutoVerticalScroll()
                    .NoHorizontalScroll()
                    .Padding(28, 24)
                    .Content(content));

    private static FrameworkElement SettingsSection(string title, string description, FrameworkElement control) =>
        new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new TextBlock().Text(title).FontSize(ThemeFontSize.Medium).SemiBold(),
                new TextBlock()
                    .Text(description)
                    .TextWrapping(TextWrapping.Wrap)
                    .WithTheme((theme, text) => text.Foreground(theme.Palette.DisabledText)),
                control.Margin(0, 6, 0, 0));

    private sealed record NavigationEntry(string Title, Element Icon, FrameworkElement Content);

    protected override void OnDispose()
    {
        _monitor.Dispose();
        base.OnDispose();
    }
}
