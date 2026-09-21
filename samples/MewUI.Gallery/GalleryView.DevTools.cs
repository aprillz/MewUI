using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement DevToolsPage() =>
        CardGrid(
            Card(
                "Hot-reload",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .FontSize(ThemeFontSize.Small)
                            .TextWrapping(TextWrapping.Wrap)
                            .Text("Modify the code and save to see hot-reload in action.\nThis card will update with the current time."),
                        new TextBlock()
                            .Text($"Loaded: {DateTime.Now}"))
            ),

            DevToolsCard(),
            PerformanceToolsCard(),
            DirtyRegionOverlayCard(),
            BitmapCacheOverlayCard()
        );

    private FrameworkElement DevToolsCard()
    {
        var shortcuts = ShortcutList(
            "Inspector: Ctrl/Cmd+Shift+I",
            "Visual Tree: Ctrl/Cmd+Shift+T");

        if (window.DevTools is WindowDevTools devTools)
        {
            return Card(
                "DevTools",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        ToolToggle(
                            "Inspector Overlay",
                            () => devTools.InspectorIsVisible,
                            devTools.ToggleInspector,
                            sync => devTools.InspectorVisibleChanged += sync),
                        ToolToggle(
                            "Visual Tree Window",
                            () => devTools.VisualTreeIsOpen,
                            devTools.ToggleVisualTree,
                            sync => devTools.VisualTreeOpenChanged += sync),
                        shortcuts
                    )
            );
        }

        return Card("DevTools", DevToolsOffContent(shortcuts));
    }

    private FrameworkElement PerformanceToolsCard()
    {
        var shortcuts = ShortcutList(
            "Performance Monitor: Ctrl/Cmd+Shift+P",
            "Profiler: Ctrl/Cmd+Alt+Shift+P");

        if (window.DevTools is WindowDevTools devTools)
        {
            return Card(
                "Performance Monitor / Profiler",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        ToolToggle(
                            "Performance Monitor",
                            () => devTools.PerformanceMonitorIsVisible,
                            devTools.TogglePerformanceMonitor,
                            sync => devTools.PerformanceMonitorVisibleChanged += sync),
                        ToolToggle(
                            "Profiler Window",
                            () => devTools.ProfilerIsOpen,
                            devTools.ToggleProfiler,
                            sync => devTools.ProfilerOpenChanged += sync),
                        shortcuts
                    )
            );
        }

        return Card("Performance Monitor / Profiler", DevToolsOffContent(shortcuts));
    }

    private FrameworkElement DirtyRegionOverlayCard() =>
        Card(
            "Dirty Region Overlay",
            new StackPanel()
                .Width(280)
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(ThemeFontSize.Small)
                        .TextWrapping(TextWrapping.Wrap)
                        .Text("Tints the areas each frame repainted, in a color that changes per frame, and shows how many visuals the frame visited, recorded and replayed. Available in release builds too."),
                    ShortcutList("Dirty Region Overlay: Ctrl/Cmd+Shift+D")
                )
        );

    private FrameworkElement BitmapCacheOverlayCard() =>
        Card(
            "BitmapCache Overlay",
            new StackPanel()
                .Width(280)
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(ThemeFontSize.Small)
                        .TextWrapping(TextWrapping.Wrap)
                        .Text("Covers BitmapCache regions with a translucent color that changes whenever the cache is rebuilt."),
                    ShortcutList("BitmapCache Overlay: Ctrl/Cmd+Shift+B")
                )
        );

    /// <summary>A toggle that switches a tool and follows the tool's own change event, so a shortcut or a closed window updates it too.</summary>
    private static ToggleButton ToolToggle(string text, Func<bool> isOn, Action toggle, Action<Action<bool>> subscribe)
    {
        bool updating = false;
        var button = new ToggleButton().Content(text);

        void Sync()
        {
            updating = true;
            try
            {
                button.IsChecked = isOn();
            }
            finally
            {
                updating = false;
            }
        }

        button.CheckedChanged += _ =>
        {
            if (updating)
            {
                return;
            }

            toggle();
            Sync();
        };

        subscribe(_ => Sync());
        Sync();
        return button;
    }

    private static TextBlock ShortcutList(params string[] shortcuts) =>
        new TextBlock()
            .FontSize(ThemeFontSize.Small)
            .Text("Shortcuts:\n" + string.Join("\n", shortcuts.Select(shortcut => "- " + shortcut)));

    private static FrameworkElement DevToolsOffContent(TextBlock shortcuts) =>
        new StackPanel()
            .Width(280)
            .Vertical()
            .Spacing(8)
            .Children(
                new TextBlock()
                    .FontSize(ThemeFontSize.Small)
                    .TextWrapping(TextWrapping.Wrap)
                    .Text("DevTools are off in this build. Set <MewUIDevTools>true</MewUIDevTools> to enable them."),
                shortcuts
            );
}
