using System.Diagnostics;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement ShowDialogPage()
    {
        var syncStatus = new ObservableValue<string>("Result: -");
        var asyncStatus = new ObservableValue<string>("Result: -");

        return CardGrid(
            Card(
                "Synchronous ShowDialog",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .FontSize(11)
                            .Text("ShowDialog() blocks this click handler (no await)\nwhile a nested loop keeps input, timers, and paint live."),
                        new Button()
                            .Content("Show (sync)")
                            .OnClick(() =>
                            {
                                // Note: this handler is NOT async. ShowDialog blocks here until the
                                // dialog closes; the live counter inside proves the loop keeps pumping.
                                var dialog = new SyncDialogWindow();
                                dialog.ShowDialog(window);
                                syncStatus.Value = $"Result: {dialog.Result}, clicks={dialog.ClickCount}";
                            }),
                        new TextBlock().BindText(syncStatus).FontSize(11)
                    )
            ),
            Card(
                "Asynchronous ShowDialogAsync",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .FontSize(11)
                            .Text("ShowDialogAsync() returns a Task on the same loop.\nSame dialog, awaited instead of blocking."),
                        new Button()
                            .Content("Show (async)")
                            .OnClick(async () =>
                            {
                                var dialog = new SyncDialogWindow();
                                await dialog.ShowDialogAsync(window);
                                asyncStatus.Value = $"Result: {dialog.Result}, clicks={dialog.ClickCount}";
                            }),
                        new TextBlock().BindText(asyncStatus).FontSize(11)
                    )
            )
        );
    }
}

/// <summary>
/// A minimal managed modal window used to exercise <see cref="Window.ShowDialog"/> and its nested loop.
/// A background task updates a live elapsed counter via the dispatcher; if the nested loop did not keep
/// pumping, the counter would freeze while the dialog is open.
/// </summary>
internal sealed class SyncDialogWindow : Window
{
    private readonly ObservableValue<string> _elapsed = new("Elapsed (live): 0.0s");
    private readonly ObservableValue<string> _clicks = new("Clicks: 0");
    private volatile bool _running = true;

    public bool? Result { get; private set; }
    public int ClickCount { get; private set; }

    public SyncDialogWindow()
    {
        Title = "ShowDialog sample";
        Padding = new Thickness(16);
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.FitContentSize(380, 280);

        Closed += () => _running = false;
        PreviewKeyDown += OnPreviewKeyDown;

        Content = BuildContent();
        StartElapsedTimer();
    }

    private Element BuildContent()
    {
        var buttons = new StackPanel()
            .Horizontal()
            .Spacing(12)
            .Right()
            .Children(
                new Button().MinWidth(60).Content("OK").OnClick(() => CloseWith(true)),
                new Button().MinWidth(60).Content("Cancel").OnClick(() => CloseWith(false))
            );

        return new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                new TextBlock()
                    .TextWrapping(TextWrapping.Wrap)
                    .Text("This is a managed modal dialog shown with ShowDialog().\nThe owner is disabled until you close it."),
                new TextBlock().BindText(_elapsed),
                new TextBlock().BindText(_clicks),
                new Button()
                    .Content("Click me (+1)")
                    .OnClick(() =>
                    {
                        ClickCount++;
                        _clicks.Value = $"Clicks: {ClickCount}";
                    }),
                buttons
            );
    }

    private void StartElapsedTimer()
    {
        var dispatcher = Application.Current.Dispatcher!;
        long start = Stopwatch.GetTimestamp();

        _ = Task.Run(async () =>
        {
            while (_running)
            {
                await Task.Delay(100);
                if (!_running) break;

                double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
                dispatcher.BeginInvoke(() => _elapsed.Value = $"Elapsed (live): {seconds:0.0}s");
            }
        });
    }

    private void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseWith(false);
        }
    }

    private void CloseWith(bool? result)
    {
        Result = result;
        Close();
    }
}
