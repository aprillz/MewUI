using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class GridViewColumnSizingTests
{
    [TestMethod]
    public void Resolver_DistributesRemainingWidthByStarWeight()
    {
        GridViewColumnWidthRequest[] columns =
        [
            new(GridLength.Pixels(100), 0, 0, double.PositiveInfinity),
            new(GridLength.Star, 0, 0, double.PositiveInfinity),
            new(GridLength.Stars(2), 0, 0, double.PositiveInfinity),
        ];
        var widths = new double[columns.Length];

        double extent = GridViewColumnWidthResolver.Resolve(columns, 700, widths);

        AssertClose(100, widths[0]);
        AssertClose(200, widths[1]);
        AssertClose(400, widths[2]);
        AssertClose(700, extent);
    }

    [TestMethod]
    public void Resolver_UsesStarMinimumsWhenMinimumExtentOverflows()
    {
        GridViewColumnWidthRequest[] columns =
        [
            new(GridLength.Pixels(450), 0, 0, double.PositiveInfinity),
            new(GridLength.Star, 0, 80, double.PositiveInfinity),
            new(GridLength.Star, 0, 90, double.PositiveInfinity),
        ];
        var widths = new double[columns.Length];

        double extent = GridViewColumnWidthResolver.Resolve(columns, 500, widths);

        AssertClose(450, widths[0]);
        AssertClose(80, widths[1]);
        AssertClose(90, widths[2]);
        AssertClose(620, extent);
    }

    [TestMethod]
    public void Resolver_FixedOverflowDoesNotGiveStarInfiniteWidth()
    {
        GridViewColumnWidthRequest[] columns =
        [
            new(GridLength.Pixels(300), 0, 0, double.PositiveInfinity),
            new(GridLength.Pixels(300), 0, 0, double.PositiveInfinity),
            new(GridLength.Star, 0, 0, double.PositiveInfinity),
        ];
        var widths = new double[columns.Length];

        double extent = GridViewColumnWidthResolver.Resolve(columns, 500, widths);

        AssertClose(300, widths[0]);
        AssertClose(300, widths[1]);
        AssertClose(0, widths[2]);
        AssertClose(600, extent);
    }

    [TestMethod]
    public void Resolver_RedistributesAfterMinAndMaxConstraints()
    {
        GridViewColumnWidthRequest[] columns =
        [
            new(GridLength.Star, 0, 400, double.PositiveInfinity),
            new(GridLength.Star, 0, 0, 200),
            new(GridLength.Star, 0, 0, double.PositiveInfinity),
        ];
        var widths = new double[columns.Length];

        double extent = GridViewColumnWidthResolver.Resolve(columns, 900, widths);

        AssertClose(400, widths[0]);
        AssertClose(200, widths[1]);
        AssertClose(300, widths[2]);
        AssertClose(900, extent);
    }

    [TestMethod]
    public void Resolver_UsesAutoDesiredWidthAndConstraints()
    {
        GridViewColumnWidthRequest[] columns =
        [
            new(GridLength.Auto, 40, 100, 180),
            new(GridLength.Auto, 240, 0, 180),
            new(GridLength.Star, 0, 20, double.PositiveInfinity),
        ];
        var widths = new double[columns.Length];

        double extent = GridViewColumnWidthResolver.Resolve(columns, 500, widths);

        AssertClose(100, widths[0]);
        AssertClose(180, widths[1]);
        AssertClose(220, widths[2]);
        AssertClose(500, extent);
    }

    [TestMethod]
    public void Resolver_UsesStarMinimumForInfiniteConstraint()
    {
        GridViewColumnWidthRequest[] columns =
        [
            new(GridLength.Pixels(100), 0, 0, double.PositiveInfinity),
            new(GridLength.Auto, 120, 0, double.PositiveInfinity),
            new(GridLength.Stars(2), 0, 60, double.PositiveInfinity),
        ];
        var widths = new double[columns.Length];

        double extent = GridViewColumnWidthResolver.Resolve(columns, double.PositiveInfinity, widths);

        AssertClose(100, widths[0]);
        AssertClose(120, widths[1]);
        AssertClose(60, widths[2]);
        AssertClose(280, extent);
    }

    [TestMethod]
    public void StarResize_PreservesStarModeAndReweightsPeers()
    {
        var core = CreateCore(
            Column(GridLength.Star),
            Column(GridLength.Stars(2)));
        core.ResolveColumnWidths(600, out _);

        var session = core.BeginColumnResize(0);
        Assert.IsNotNull(session);
        Assert.IsTrue(core.ResizeColumn(session, 300));

        Assert.IsTrue(core.Columns[0].Width.IsStar);
        Assert.IsTrue(core.Columns[1].Width.IsStar);
        AssertClose(300, core.Columns[0].ActualWidth);
        AssertClose(300, core.Columns[1].ActualWidth);

        core.ResolveColumnWidths(900, out _);
        AssertClose(450, core.Columns[0].ActualWidth);
        AssertClose(450, core.Columns[1].ActualWidth);
    }

    [TestMethod]
    public void LastStarResize_UsesStarPeerOnTheLeft()
    {
        var core = CreateCore(
            Column(GridLength.Star),
            Column(GridLength.Stars(2)));
        core.ResolveColumnWidths(600, out _);

        Assert.IsTrue(core.CanResizeColumn(1));
        var session = core.BeginColumnResize(1);
        Assert.IsNotNull(session);
        Assert.IsTrue(core.ResizeColumn(session, 300));

        Assert.IsTrue(core.Columns[0].Width.IsStar);
        Assert.IsTrue(core.Columns[1].Width.IsStar);
        AssertClose(300, core.Columns[0].ActualWidth);
        AssertClose(300, core.Columns[1].ActualWidth);

        core.ResolveColumnWidths(900, out _);
        AssertClose(450, core.Columns[0].ActualWidth);
        AssertClose(450, core.Columns[1].ActualWidth);
    }

    [TestMethod]
    public void StarResize_RespectsPeerMinimum()
    {
        var core = CreateCore(
            Column(GridLength.Star),
            Column(GridLength.Star, minWidth: 250));
        core.ResolveColumnWidths(600, out _);

        var session = core.BeginColumnResize(0);
        Assert.IsNotNull(session);
        Assert.IsTrue(core.ResizeColumn(session, 500));

        AssertClose(350, core.Columns[0].ActualWidth);
        AssertClose(250, core.Columns[1].ActualWidth);
    }

    [TestMethod]
    public void LoneStarResize_MaterializesToPixel()
    {
        var core = CreateCore(Column(GridLength.Star, minWidth: 100, maxWidth: 500));
        core.ResolveColumnWidths(400, out _);

        Assert.IsTrue(core.CanResizeColumn(0));
        var session = core.BeginColumnResize(0);
        Assert.IsNotNull(session);
        Assert.IsTrue(core.ResizeColumn(session, 260));

        Assert.IsTrue(core.Columns[0].Width.IsAbsolute);
        AssertClose(260, core.Columns[0].Width.Value);
        AssertClose(260, core.Columns[0].ActualWidth);

        // Once materialized, viewport changes no longer make the user-sized column flex.
        core.ResolveColumnWidths(700, out _);
        AssertClose(260, core.Columns[0].ActualWidth);

        Assert.IsTrue(core.CanAutoSizeColumn(0));
        Assert.IsTrue(core.ResetColumnToAuto(0));
        Assert.IsTrue(core.Columns[0].Width.IsAuto);
    }

    [TestMethod]
    public void ResetColumnToAuto_ClearsPreviousAutoObservation()
    {
        var core = CreateCore(Column(GridLength.Pixels(180), minWidth: 50, maxWidth: 300));
        core.Columns[0].AutoDesiredWidth = 260;

        Assert.IsTrue(core.ResetColumnToAuto(0));

        Assert.IsTrue(core.Columns[0].Width.IsAuto);
        AssertClose(0, core.Columns[0].AutoDesiredWidth);
        Assert.IsTrue(core.ReportAutoDesiredWidth(0, 120));
        core.ResolveColumnWidths(500, out _);
        AssertClose(120, core.Columns[0].ActualWidth);

        var nonResizable = CreateCore(Column(GridLength.Pixels(180), resizable: false));
        Assert.IsFalse(nonResizable.CanAutoSizeColumn(0));
        Assert.IsFalse(nonResizable.ResetColumnToAuto(0));
        Assert.IsTrue(nonResizable.Columns[0].Width.IsAbsolute);
    }

    [TestMethod]
    public void AutoResize_MaterializesToPixel()
    {
        var core = CreateCore(Column(GridLength.Auto, minWidth: 50, maxWidth: 300));
        core.ReportAutoDesiredWidth(0, 140);
        core.ResolveColumnWidths(500, out _);

        var session = core.BeginColumnResize(0);
        Assert.IsNotNull(session);
        Assert.IsTrue(core.Columns[0].Width.IsAuto);
        Assert.IsTrue(core.ResizeColumn(session, 180));

        Assert.IsTrue(core.Columns[0].Width.IsAbsolute);
        AssertClose(180, core.Columns[0].Width.Value);
        AssertClose(180, core.Columns[0].ActualWidth);
    }

    [TestMethod]
    public void ColumnRegistration_RejectsInvalidSizingValues()
    {
        var grid = new GridView();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => grid.SetColumns(
        [
            new GridViewColumn<int>
            {
                Width = GridLength.Stars(0),
                CellTemplate = Template(),
            },
        ]));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => grid.SetColumns(
        [
            new GridViewColumn<int>
            {
                Width = 100,
                MinWidth = 120,
                MaxWidth = 80,
                CellTemplate = Template(),
            },
        ]));
    }

    [TestMethod]
    public void GridView_StarColumnsUseTheArrangedViewport()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        var grid = new GridView { BorderThickness = 0, Padding = default };
        grid.SetColumns(
        [
            PublicColumn(GridLength.Pixels(100)),
            PublicColumn(GridLength.Star),
            PublicColumn(GridLength.Stars(2)),
        ]);

        var window = HeadlessWindow.Create(700, 300);
        window.Content = grid;
        window.PerformLayout();
        window.PerformLayout();

        var scrollViewer = (ScrollViewer?)VisualTree.Find(grid, static element => element is ScrollViewer);
        Assert.IsNotNull(scrollViewer);
        double remaining = Math.Max(0, scrollViewer.ViewportWidth - 100);
        double secondBoundary = 100 + remaining / 3;

        Assert.IsTrue(grid.TryGetColumnIndexAt(new Point(secondBoundary - 2, 10), out int before));
        Assert.AreEqual(1, before);
        Assert.IsTrue(grid.TryGetColumnIndexAt(new Point(secondBoundary + 2, 10), out int after));
        Assert.AreEqual(2, after);
    }

    [TestMethod]
    public void GridView_StarColumnsSettleWhenMeasuredInfiniteAndArrangedFinite()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        var grid = new GridView { BorderThickness = 0, Padding = default };
        grid.SetColumns(
        [
            PublicColumn(GridLength.Pixels(100)),
            PublicColumn(GridLength.Star, minWidth: 80),
        ]);

        var host = new InfiniteWidthMeasureHost();
        host.Add(grid);

        var window = HeadlessWindow.Create(700, 300);
        window.Content = host;
        window.PerformLayout();

        Assert.IsTrue(window.IsUpdatePassSettled);
        Assert.IsTrue(grid.TryGetColumnIndexAt(new Point(650, 10), out int column));
        Assert.AreEqual(1, column);

        // A settled window early-outs instead of scheduling another bounded layout pass.
        window.PerformLayout();
        Assert.IsTrue(window.IsUpdatePassSettled);
    }

    [TestMethod]
    public void GridView_DoubleClickOnLoneStarBoundary_ResetsColumnToAuto()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        var grid = new GridView
        {
            BorderThickness = 0,
            Padding = default,
            CellPadding = default,
            ItemsSource = ItemsView.Create(new[] { 1 }),
        };
        grid.SetColumns(
        [
            new GridViewColumn<int>
            {
                Header = "Value",
                Width = GridLength.Star,
                MinWidth = 40,
                MaxWidth = 300,
                CellTemplate = new DelegateTemplate<int>(
                    build: _ => new Border { Width = 80 },
                    bind: static (_, _, _, _) => { }),
            },
        ]);

        var window = HeadlessWindow.Create(300, 200);
        window.Content = grid;
        window.PerformLayout();

        var probePoint = new Point(grid.Bounds.X + 200, grid.Bounds.Y + 10);
        Assert.IsTrue(grid.TryGetColumnIndexAt(probePoint, out _));

        // The final Star boundary is on the viewport's right edge; hit it from the inside.
        window.SendDoubleClick(new Point(grid.Bounds.Right - 1, grid.Bounds.Y + 10));
        window.PerformLayout();

        Assert.IsFalse(grid.TryGetColumnIndexAt(probePoint, out _));
        Assert.IsTrue(window.IsUpdatePassSettled);
    }

    [TestMethod]
    public void GridView_DragOnLoneLastStarBoundary_MaterializesToPixel()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        var grid = new GridView { BorderThickness = 0, Padding = default };
        grid.SetColumns(
        [
            PublicColumn(GridLength.Star, minWidth: 80),
        ]);

        var window = HeadlessWindow.Create(400, 200);
        window.Content = grid;
        window.PerformLayout();

        var boundary = new Point(grid.Bounds.Right - 1, grid.Bounds.Y + 10);
        window.SendMouseDown(boundary);
        window.SendMouseMove(new Point(boundary.X - 100, boundary.Y));
        window.SendMouseUp(new Point(boundary.X - 100, boundary.Y));
        window.PerformLayout();

        Assert.IsFalse(grid.TryGetColumnIndexAt(
            new Point(grid.Bounds.Right - 20, grid.Bounds.Y + 10), out _));
        Assert.IsTrue(window.IsUpdatePassSettled);
    }

    [TestMethod]
    public void GridView_ShowsHorizontalBarWhenFixedAndStarMinimumsOverflow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        var grid = new GridView { BorderThickness = 0, Padding = default };
        grid.SetColumns(
        [
            PublicColumn(GridLength.Pixels(450)),
            PublicColumn(GridLength.Star, minWidth: 160),
            PublicColumn(GridLength.Star, minWidth: 170),
        ]);

        var window = HeadlessWindow.Create(600, 300);
        window.Content = grid;
        window.PerformLayout();
        window.PerformLayout();

        var horizontalBar = (ScrollBar?)VisualTree.Find(
            grid,
            static element => element is ScrollBar bar && bar.Orientation == Orientation.Horizontal);
        Assert.IsNotNull(horizontalBar);
        Assert.IsTrue(horizontalBar.IsVisible);
    }

    [TestMethod]
    public void GridView_AutoColumnGrowsFromRealizedCellContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        var grid = new GridView
        {
            BorderThickness = 0,
            Padding = default,
            CellPadding = default,
            ItemsSource = ItemsView.Create(new[] { 1 }),
        };
        grid.SetColumns(
        [
            new GridViewColumn<int>
            {
                Header = "A",
                Width = GridLength.Auto,
                MaxWidth = 300,
                CellTemplate = new DelegateTemplate<int>(
                    build: _ => new Border { Width = 250 },
                    bind: static (_, _, _, _) => { }),
            },
            PublicColumn(GridLength.Star),
        ]);

        var window = HeadlessWindow.Create(500, 300);
        window.Content = grid;
        window.PerformLayout();
        window.PerformLayout();
        window.PerformLayout();

        Assert.IsTrue(grid.TryGetColumnIndexAt(new Point(200, 10), out int contentColumn));
        Assert.AreEqual(0, contentColumn);
        Assert.IsTrue(grid.TryGetColumnIndexAt(new Point(320, 10), out int starColumn));
        Assert.AreEqual(1, starColumn);
    }

    private static GridView.GridViewCore CreateCore(params GridView.GridViewCore.ColumnDefinition[] columns)
    {
        var core = new GridView.GridViewCore();
        core.SetColumns(columns);
        return core;
    }

    private static GridView.GridViewCore.ColumnDefinition Column(
        GridLength width,
        double minWidth = 0,
        double maxWidth = double.PositiveInfinity,
        bool resizable = true)
        => new("", width, minWidth, maxWidth, resizable, Template());

    private static GridViewColumn<int> PublicColumn(
        GridLength width,
        double minWidth = 0,
        double maxWidth = double.PositiveInfinity)
        => new()
        {
            Width = width,
            MinWidth = minWidth,
            MaxWidth = maxWidth,
            CellTemplate = Template(),
        };

    private static IDataTemplate<int> Template()
        => new DelegateTemplate<int>(
            build: _ => new TextBlock(),
            bind: static (_, _, _, _) => { });

    private sealed class InfiniteWidthMeasureHost : Panel
    {
        protected override Size MeasureContent(Size availableSize)
        {
            if (Count == 0 || this[0] is not UIElement child)
            {
                return Size.Empty;
            }

            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            return child.DesiredSize;
        }

        protected override void ArrangeContent(Rect bounds)
        {
            if (Count > 0 && this[0] is UIElement child)
            {
                child.Arrange(bounds);
            }
        }
    }

    private static void AssertClose(double expected, double actual)
        => Assert.AreEqual(expected, actual, 0.001);
}
