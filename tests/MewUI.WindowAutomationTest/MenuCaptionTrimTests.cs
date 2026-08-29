using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A menu sizes its caption column in MeasureContent and then, while rendering, carves the fixed
/// icon, shortcut and submenu columns out of the arranged width and leaves the remainder to the
/// caption. Every width the arrangement loses therefore comes out of the caption alone and trims
/// its last glyph, so the render must never grant less than the measure asked for. Run once per
/// scale the machine offers, because the loss only appears where the arithmetic lands off a pixel.
/// </summary>
[TestClass]
public sealed class MenuCaptionTrimTests
{
    [TestMethod]
    public async Task RenderGrantsTheMeasuredCaptionWidthAtEveryScale()
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

        Assert.IsTrue(scales.Count > 0, $"no displays to probe: {MonitorMatrix.Describe()}");

        var failures = new List<string>();

        foreach (var monitor in scales)
        {
            await RealAppSession.RunAsync(async () =>
            {
                var owner = new Button { Content = new TextBlock { Text = "owner" } };
                var window = new Window
                {
                    Title = "menu caption trim",
                    StartupLocation = WindowStartupLocation.Manual,
                    Content = owner,
                };

                // The icon and shortcut columns are what the caption competes with, so the probe only
                // means something when every column the real menu has is present.
                var icon = new IconTemplate(static size => new Border
                {
                    Width = size.Dip,
                    Height = size.Dip,
                    Background = Color.FromRgb(120, 120, 120),
                });

                var newFile = new Command("probe.new", "New", icon);
                var saveCopy = new Command("probe.saveCopy", "Save a Copy", icon);
                var print = new Command("probe.print", "Print", icon);

                var menu = new ContextMenu();
                menu.AddItem(newFile);
                menu.AddItem(saveCopy);
                menu.AddItem(print);

                try
                {
                    window.Show();
                    MonitorProbe.SetWindowPos(window.Handle, 0,
                        monitor.PixelBounds.CenterX - 200, monitor.PixelBounds.CenterY - 200, 0, 0,
                        MonitorProbe.MOVE_ONLY);
                    await Task.Delay(250);

                    window.InputMap.Map(newFile, new KeyGesture(Key.N, ModifierKeys.Control));
                    window.InputMap.Map(saveCopy, new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
                    window.InputMap.Map(print, new KeyGesture(Key.P, ModifierKeys.Control));

                    menu.LastCaptionWidth = double.PositiveInfinity;
                    menu.Show(owner, new Point(owner.Bounds.X, owner.Bounds.Bottom));
                    await Task.Delay(300);

                    double granted = menu.LastCaptionWidth;
                    double measured = menu.MeasuredCaptionWidth;

                    if (double.IsInfinity(granted))
                    {
                        failures.Add($"{monitor.Label}: the menu never rendered a caption");
                    }
                    else if (granted + 0.001 < measured)
                    {
                        failures.Add(
                            $"{monitor.Label}: caption column {granted:0.###} < measured {measured:0.###} " +
                            $"(short by {measured - granted:0.###} DIP)");
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        Assert.IsTrue(failures.Count == 0, string.Join("; ", failures));
    }
}
