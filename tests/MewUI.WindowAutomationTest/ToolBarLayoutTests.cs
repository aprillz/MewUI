using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// The toolbar's heights come from BaseControlHeight and must survive the real style pass: headless
/// layout has reported a control's own minimum where a real window showed the base control height.
/// </summary>
[TestClass]
public sealed class ToolBarLayoutTests
{
    [TestMethod]
    public async Task GroupAndEntryKeepTheirMetricHeightsInARealWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only.");
            return;
        }

        string report = "not measured";
        Rect plate = default;
        Rect entry = default;
        Rect icon = default;
        double bandPitch = 0;
        double groupMetric = 0;

        await RealAppSession.RunAsync(async () =>
        {
            var icons = new IconTemplate(static size =>
                new Border { Width = size.Dip, Height = size.Dip, Background = Color.Gray });

            var bar = new ToolBar { CanReorderGroups = true, Width = 400 };
            bar.Bands.Add(new ToolBarBand(
                new ToolBarGroup(
                    new ToolBarItem(new Command("real.a", "A", icons)),
                    new ToolBarItem(new Command("real.b", "B", icons))),
                new ToolBarGroup(new ToolBarToggleItem(new Command("real.c", "C", icons)))));
            bar.Bands.Add(new ToolBarBand(
                new ToolBarGroup(new ToolBarItem(new Command("real.d", "D", icons)))));

            var window = new Window
            {
                Title = "toolbar layout",
                StartupLocation = WindowStartupLocation.Manual,
                Content = bar,
            };

            try
            {
                window.Show();
                await Task.Delay(300);

                var first = bar.VisualsInternal[0];
                plate = first.Groups[0].Bounds;
                entry = ((UIElement)first.Groups[0].Entries[0]).Bounds;
                icon = VisualTree.Find(
                    (Element)first.Groups[0].Entries[0],
                    static e => e is Border { Bounds.Height: > 0 })!.Bounds;
                bandPitch = bar.VisualsInternal[1].Bounds.Y - first.Bounds.Y;
                groupMetric = bar.ThemeInternal.Metrics.BaseControlHeight + 4;

                report = $"plate={plate} entry={entry} icon={icon} pitch={bandPitch} metric={groupMetric}";
            }
            finally
            {
                window.Close();
            }
        });

        // One DIP of tolerance: a scaled display snaps every edge to whole device pixels.
        Assert.IsLessThanOrEqualTo(1, Math.Abs(plate.Height - groupMetric), $"the plate is not one group tall: {report}");
        Assert.IsLessThanOrEqualTo(1, Math.Abs(entry.Height - (groupMetric - 4)), $"the entry ignored the plate padding: {report}");
        Assert.IsLessThanOrEqualTo(1, Math.Abs(bandPitch - (groupMetric + 4)), $"the band pitch is not the group plus its margin: {report}");
        Assert.IsLessThanOrEqualTo(1, Math.Abs(icon.Height - 16), $"the icon is not the toolbar icon size: {report}");
    }
}
