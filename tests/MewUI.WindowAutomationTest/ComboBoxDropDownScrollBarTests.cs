using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A drop-down short enough to show every item must not scroll. The list rounds its row height to
/// device pixels, so the popup has to be sized from the rounded rows; run once per scale the machine
/// offers, because the shortfall only appears where the row height lands off a pixel.
/// </summary>
[TestClass]
public sealed class ComboBoxDropDownScrollBarTests
{
    [TestMethod]
    public async Task ATwoItemDropDownShowsNoScrollBarAtEveryScale()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only.");
            return;
        }

        var scales = MonitorMatrix.Monitors
            .GroupBy(static monitor => monitor.Dpi)
            .Select(static group => group.First())
            .OrderBy(static monitor => monitor.Dpi)
            .ToList();

        Assert.IsNotEmpty(scales, $"no displays to probe: {MonitorMatrix.Describe()}");

        var failures = new List<string>();
        var observed = new List<string>();

        foreach (var monitor in scales)
        {
            await RealAppSession.RunAsync(async () =>
            {
                var combo = new ComboBox
                {
                    Width = 200,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(8),
                };
                combo.Items(["Alpha", "Beta"]);
                var window = new Window
                {
                    Title = "combo drop-down scroll bar",
                    StartupLocation = WindowStartupLocation.Manual,
                    WindowSize = WindowSize.Fixed(400, 300),
                    Content = new Grid().Children(combo),
                };

                try
                {
                    window.Show();
                    MonitorProbe.SetWindowPos(window.Handle, 0,
                        monitor.PixelBounds.CenterX - 200, monitor.PixelBounds.CenterY - 200, 0, 0,
                        MonitorProbe.MOVE_ONLY);
                    await Task.Delay(400);

                    combo.IsDropDownOpen = true;
                    await Task.Delay(700);

                    var popupList = (ListBox?)typeof(ComboBox)
                        .GetField("_popupList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .GetValue(combo);
                    if (popupList == null || !combo.IsDropDownOpen)
                    {
                        failures.Add($"{monitor.Label}: the drop-down did not open");
                        return;
                    }

                    ScrollBar? vertical = null;
                    VisualTree.Visit(popupList, element =>
                    {
                        if (element is ScrollBar bar && bar.Orientation == Orientation.Vertical)
                        {
                            vertical = bar;
                        }
                    });

                    string state =
                        $"{monitor.Label}: window dpi {window.GetDpi()}, list {popupList.Bounds.Width:0.##}x{popupList.Bounds.Height:0.##} DIP, " +
                        $"scroll bar {(vertical?.IsVisible == true ? "shown" : "hidden")}";
                    observed.Add(state);
                    Console.Error.WriteLine(state);
                    if (vertical?.IsVisible == true)
                    {
                        failures.Add(state);
                    }
                }
                finally
                {
                    combo.IsDropDownOpen = false;
                    window.Close();
                }
            });
        }

        Assert.IsEmpty(failures, string.Join("; ", failures) + " | observed: " + string.Join("; ", observed));
        // Surfaces what was actually measured, since a pass says nothing about which scales ran.
        Console.WriteLine("observed: " + string.Join("; ", observed));
    }
}
