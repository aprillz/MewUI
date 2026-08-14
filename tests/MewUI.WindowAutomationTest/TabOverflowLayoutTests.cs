using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// The tab strip's overflow chevron keeps its own height and centres in the row. Headless layout
/// already covers the arithmetic; this runs it through a real window because a real render path has
/// caught divergences from headless before.
/// </summary>
[TestClass]
public sealed class TabOverflowLayoutTests
{
    [TestMethod]
    public async Task OverflowButtonKeepsItsHeightInARealWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only.");
            return;
        }

        string report = "not measured";
        Rect overflowBounds = default;
        Rect headerBounds = default;

        await RealAppSession.RunAsync(async () =>
        {
            // Narrow enough that the strip has to hide tabs, which is what shows the chevron at all.
            var tabs = new TabControl { Width = 200, Height = 120 };
            for (int i = 0; i < 8; i++)
            {
                tabs.AddTab(new TabItem
                {
                    Header = new TextBlock { Text = $"Tab number {i}" },
                    HeaderText = $"Tab number {i}",
                    Content = new Border(),
                });
            }

            var window = new Window
            {
                Title = "tab overflow layout",
                StartupLocation = WindowStartupLocation.Manual,
                Content = tabs,
            };

            try
            {
                window.Show();
                await Task.Delay(300);

                var overflow = (DropDownButton)VisualTree.Find(tabs, e => e is DropDownButton)!;
                var header = VisualTree.Find(tabs, e => e is TabHeaderButton && e.Bounds.Height > 0)!;

                overflowBounds = overflow.Bounds;
                headerBounds = header.Bounds;
                report = $"overflow={overflowBounds} header={headerBounds} desired={overflow.DesiredSize}";
            }
            finally
            {
                window.Close();
            }
        });

        // Tolerances are one DIP: a scaled display snaps every edge to whole device pixels, so an 18 DIP
        // button measures 18.4 at 125%. What must hold is that it keeps its own height instead of
        // filling the strip.
        Assert.IsTrue(overflowBounds.Height > 0, $"the overflow button was not laid out: {report}");
        Assert.IsTrue(Math.Abs(overflowBounds.Height - 18) <= 1, $"the chevron is not its own height: {report}");
        Assert.IsTrue(overflowBounds.Height < headerBounds.Height - 1, $"the chevron stretched to the strip: {report}");
        Assert.IsTrue(
            Math.Abs(overflowBounds.Y - (headerBounds.Y + ((headerBounds.Height - overflowBounds.Height) / 2))) <= 1,
            $"the chevron is not centred in the strip: {report}");
    }
}
