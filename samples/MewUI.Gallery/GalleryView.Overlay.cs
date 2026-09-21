using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement OverlayPage()
    {
        return CardGrid(
            Card(
                "Toast",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Show Toast")
                            .OnClick(() => window.ShowToast("Hello, Toast!")),
                        new Button()
                            .Content("Long Message")
                            .OnClick(() => window.ShowToast("This is a longer toast message to test auto-dismiss duration scaling.")),
                        new Button()
                            .Content("Rapid Fire")
                            .OnClick(() => window.ShowToast($"Toast at {DateTime.Now:HH:mm:ss}"))
                    )
            ),

            Card(
                "BusyIndicator",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Show (non-cancellable)")
                            .OnClick(() => ShowBusyDemo(cancellable: false)),
                        new Button()
                            .Content("Show (cancellable)")
                            .OnClick(() => ShowBusyDemo(cancellable: true))
                    )
            ),

            Card(
                "ToolTip",
                new Button()
                    .Content("Hover me")
                    .ToolTip("ToolTip text")
            ),

            Card(
                "Tooltip font isolation",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("The button is 20pt Consolas. Hover it: the tooltip keeps the theme font, not the button's font. A popup no longer inherits the triggering control's font.")
                            .TextWrapping(TextWrapping.Wrap)
                            .FontSize(ThemeFontSize.Small),
                        new Button()
                            .Content("Hover me (20pt / Consolas)")
                            .FontSize(20)
                            .FontFamily("Consolas")
                            .ToolTip("This tooltip stays in the theme font.")
                            .HorizontalAlignment(HorizontalAlignment.Left)
                    )
            )
        );
    }

    private async void ShowBusyDemo(bool cancellable)
    {
        using var busy = window.CreateBusyIndicator("Initializing...", cancellable);

        try
        {
            for (int i = 1; i <= 5; i++)
            {
                await Task.Delay(1000, busy.CancellationToken);
                busy.NotifyProgress($"Step {i} of 5...");
            }

            await Task.Delay(500, busy.CancellationToken);
            window.ShowToast("Operation completed!");
        }
        catch (OperationCanceledException)
        {
            window.ShowToast("Operation aborted.");
        }
    }
}
