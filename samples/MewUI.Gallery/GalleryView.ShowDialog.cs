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
                            .Text("ShowDialog() blocks this click handler (no await)\nwhile a nested loop keeps input and paint live."),
                        new Button()
                            .Content("Show (sync)")
                            .OnClick(() =>
                            {
                                // Note: this handler is NOT async. ShowDialog blocks here until the dialog closes.
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
/// A minimal managed modal window used to exercise <see cref="Window.ShowDialog"/>.
/// </summary>
internal sealed class SyncDialogWindow : Window
{
    private readonly ObservableValue<string> _clicks = new("Clicks: 0");
    private bool _confirmedClose;

    public bool? Result { get; private set; }
    public int ClickCount { get; private set; }

    public SyncDialogWindow()
    {
        Title = "ShowDialog sample";
        Padding = new Thickness(16);
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.FitContentSize(380, 300);

        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;

        Content = BuildContent();
    }

    // Demonstrates a SYNC dialog shown from inside the Closing event (issue #131 scenario):
    // closing via the title-bar X runs a nested ShowDialog confirm and can cancel the close.
    private void OnClosing(ClosingEventArgs e)
    {
        if (_confirmedClose)
        {
            return; // OK/Cancel/Escape already chose a result — no prompt
        }

        // Sync MessageBox shown from inside Closing (nested loop via ShowDialog).
        if (MessageBox.AskYesNo("Close this dialog?", PromptIconKind.Question, owner: this))
        {
            Result = null; // closed via X, no explicit OK/Cancel result
        }
        else
        {
            e.Cancel = true; // keep the dialog open
        }
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
                    .Text("Managed modal dialog (ShowDialog). The owner is disabled until closed.\nClose via the title-bar X to see a sync confirm shown from the Closing event."),
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
        _confirmedClose = true; // explicit choice — skip the Closing confirm prompt
        Close();
    }
}
