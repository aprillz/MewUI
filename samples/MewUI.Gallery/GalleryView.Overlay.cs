using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement OverlayPage()
    {
        ConfettiOverlay confetti = new(this);
        window.OverlayLayer.Add(confetti);

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
            Card("Confetti",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Label()
                            .Text("Port of WpfConfetti by caefale")
                            .WithTheme((t, c) => c.Foreground(t.Palette.DisabledText))
                            .FontSize(11),
                        new Grid()
                            .Columns("*,*")
                            .Rows("Auto,Auto,Auto,Auto")
                            .Spacing(4)
                            .Children(
                                new Button()
                                    .Content("Burst")
                                    .OnClick(() => confetti?.Burst())
                                    .ColumnSpan(2),
                                new Button()
                                    .Content("Start Cannons")
                                    .OnClick(() => confetti?.Cannons())
                                    .Row(1),
                                new Button()
                                    .Content("Stop Cannons")
                                    .OnClick(() => confetti?.StopCannons())
                                    .Row(1).Column(1),
                                new Button()
                                    .Content("Start Rain")
                                    .OnClick(() => confetti?.StartRain())
                                    .Row(2),
                                new Button()
                                    .Content("Stop Rain")
                                    .OnClick(() => confetti?.StopRain())
                                    .Row(2).Column(1),
                                new Button()
                                    .Content("Clear All")
                                    .OnClick(() => confetti?.Clear())
                                    .Row(3).ColumnSpan(2)
                            )
                    ))
        );
    }

    private async void ShowBusyDemo(bool cancellable)
    {
        using var busy = window.ShowBusyIndicator("Initializing...", cancellable);

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
