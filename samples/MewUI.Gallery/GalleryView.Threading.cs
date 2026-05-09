using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement ThreadingPage() =>
        CardGrid(
            ThreadingInvokeCard(),
            ThreadingAwaitCard()
        );

    private FrameworkElement ThreadingInvokeCard()
    {
        TextBlock statusLabel = null!;
        TextBlock counterLabel = null!;
        Button startButton = null!;

        return Card(
            "Update by BeginInvoke",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(11)
                        .Text("Background thread updates label via BeginInvoke."),
                    new TextBlock()
                        .Ref(out statusLabel)
                        .Text("Status: Idle"),
                    new TextBlock()
                        .Ref(out counterLabel)
                        .Text("Step: -"),
                    new Button()
                        .Ref(out startButton)
                        .Content("Start")
                        .OnClick(async () =>
                        {
                            startButton.IsEnabled = false;
                            statusLabel.Text = "Status: Running...";

                            await Task.Run(() =>
                            {
                                var dispatcher = Application.Current.Dispatcher!;
                                for (int i = 1; i <= 300; i++)
                                {
                                    Thread.Sleep(1);

                                    int step = i;
                                    dispatcher.BeginInvoke(() =>
                                    {
                                        counterLabel.Text = $"Step: {step} / 300";
                                    });
                                }
                            });

                            statusLabel.Text = "Status: Done";
                            startButton.IsEnabled = true;
                        })
                ));
    }

    private FrameworkElement ThreadingAwaitCard()
    {
        TextBlock statusLabel = null!;
        TextBlock counterLabel = null!;
        Button startButton = null!;

        return Card(
            "Update by await",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(11)
                        .Text("await Task.Run returns to UI thread.\nNo Dispatcher call needed."),
                    new TextBlock()
                        .Ref(out statusLabel)
                        .Text("Status: Idle"),
                    new TextBlock()
                        .Ref(out counterLabel)
                        .Text("Step: -"),
                    new Button()
                        .Ref(out startButton)
                        .Content("Start")
                        .OnClick(async () =>
                        {
                            startButton.IsEnabled = false;
                            statusLabel.Text = "Status: Running...";

                            for (int i = 1; i <= 300; i++)
                            {
                                await Task.Delay(10);

                                counterLabel.Text = $"Step: {i} / 300";
                            }

                            statusLabel.Text = "Status: Done";
                            startButton.IsEnabled = true;
                        })
                ));
    }
}
