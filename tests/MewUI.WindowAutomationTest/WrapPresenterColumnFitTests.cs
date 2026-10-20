using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A wrap presenter lays its cells out at the item width rounded to whole device pixels, so the column count
/// has to come from that same width: counted from the unrounded width on a scaled monitor, a row grew past
/// the viewport and lost its last cell where the rounding went up, and left room for another cell where it
/// went down.
/// </summary>
[TestClass]
public sealed class WrapPresenterColumnFitTests
{
    private const double ITEM_HEIGHT_DIP = 18;
    private const int ITEM_COUNT = 256;
    private const int LAYOUT_WAIT_MS = 5000;
    private const int LAYOUT_POLL_MS = 50;
    private const int LAYOUT_SETTLE_MS = 200;

    // Widths that round up, down and not at all at the scales the machine has, including the issue's three.
    private static readonly double[] _itemWidths = [18.0, (double)float.BitDecrement(18.0f), 17.0, 23.3, 40.0];
    private static readonly double[] _windowWidths = [700.0, 913.0];

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DynamicData(nameof(MonitorMatrix.DistinctScales), typeof(MonitorMatrix),
        DynamicDataDisplayName = nameof(MonitorMatrix.ScaleName),
        DynamicDataDisplayNameDeclaringType = typeof(MonitorMatrix))]
    public async Task EveryRow_FillsTheViewportWithoutPassingIt(MonitorProbe monitor)
    {
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real Win32 application loop.");
            return;
        }

        double onePixel = 96.0 / monitor.Dpi;
        var failures = new List<string>();
        await RealAppSession.RunAsync(async () =>
        {
            foreach (double windowWidth in _windowWidths)
            {
                foreach (double itemWidth in _itemWidths)
                {
                    var presenter = new WrapItemsPresenter { ItemWidth = itemWidth, ItemHeight = ITEM_HEIGHT_DIP };
                    var listBox = new ListBox().FontSize(10).ItemPadding(0.0).Items(Enumerable.Range(0, ITEM_COUNT).Select(index => $"{index:X2}").ToList(), item => item);
                    listBox.SetPresenter(presenter);
                    var window = new Window
                    {
                        Title = $"WrapFit {monitor.ScalePercent}% {itemWidth}",
                        StartupLocation = WindowStartupLocation.Manual,
                        Padding = new Thickness(0),
                        WindowSize = WindowSize.Fixed(windowWidth, 240),
                        Content = listBox,
                    };

                    try
                    {
                        window.Show();
                        MonitorProbe.SetWindowPos(window.Handle, 0,
                            monitor.PixelBounds.Left + 40, monitor.PixelBounds.Top + 40, 0, 0, MonitorProbe.MOVE_ONLY);

                        var waited = System.Diagnostics.Stopwatch.StartNew();
                        while ((listBox.GetDpi() != monitor.Dpi || !listBox.TryGetContainer(0, out _)) && waited.ElapsedMilliseconds < LAYOUT_WAIT_MS)
                        {
                            await Task.Delay(LAYOUT_POLL_MS);
                        }

                        await Task.Delay(LAYOUT_SETTLE_MS);
                        string? problem = Measure(listBox, presenter, onePixel);
                        Console.WriteLine($"[WrapFit] {monitor.ScalePercent}% window {windowWidth} item {itemWidth}: {problem ?? "fits"}");
                        if (problem != null)
                        {
                            failures.Add($"window {windowWidth} DIP, item {itemWidth}: {problem}");
                        }
                    }
                    finally
                    {
                        window.Close();
                    }
                }
            }
        });

        Assert.IsTrue(failures.Count == 0, $"at {monitor.ScalePercent}%:\n" + string.Join("\n", failures));
    }

    /// <summary>What is wrong with the first row, or null when it ends within a pixel short of a full cell from the right edge.</summary>
    private static string? Measure(ListBox listBox, WrapItemsPresenter presenter, double onePixel)
    {
        if (!listBox.TryGetContainer(0, out var first))
        {
            return "no item was realized";
        }

        int columns = presenter.ColumnCount;
        if (!listBox.TryGetContainer(columns - 1, out var last))
        {
            return $"the row's last item ({columns - 1}) was not realized";
        }

        var viewport = presenter.Bounds;
        double rowRight = last.Bounds.Right;
        double cellWidth = last.Bounds.Width;
        string layout = $"{columns} columns of {cellWidth:0.####} DIP end at {rowRight:0.##}, viewport ends at {viewport.Right:0.##}";
        if (rowRight > viewport.Right + onePixel)
        {
            return $"the row passes the viewport by {rowRight - viewport.Right:0.##} DIP ({layout})";
        }

        if (viewport.Right - rowRight >= cellWidth + onePixel)
        {
            return $"another cell fits in the {viewport.Right - rowRight:0.##} DIP left ({layout})";
        }

        return null;
    }
}
