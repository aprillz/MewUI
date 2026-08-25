using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// A grid that sizes to content builds its desired size from the children, so arranging that same
/// size must not re-split it by star weight below what each cell was measured at - the child would
/// draw over its neighbour. A grid with room to spare still distributes proportionally.
/// </summary>
[TestClass]
public sealed class GridStarArrangeTests
{
    private const double LEFT_SIZE = 500;
    private const double RIGHT_SIZE = 200;
    private const double SPACING = 30;

    [TestMethod]
    public void ContentSizedGridKeepsStarCellsAtTheirMeasuredWidth()
    {
        var (grid, left, right) = CreateGrid(HorizontalAlignment.Center);
        Layout(grid, availableWidth: 1200);

        Assert.AreEqual(LEFT_SIZE, grid.ColumnDefinitions[0].ActualWidth, 0.5,
            "The 10* cell was squeezed below the content the desired size was built from.");
        Assert.AreEqual(RIGHT_SIZE, grid.ColumnDefinitions[1].ActualWidth, 0.5,
            "The 7* cell did not keep its measured width.");
        double overlap = Math.Max(0, left.Bounds.Right - right.Bounds.X);
        Assert.AreEqual(0, overlap, 0.5,
            $"The cells overlap: left ends at {left.Bounds.Right}, right starts at {right.Bounds.X}.");
    }

    [TestMethod]
    public void StretchedGridSplitsTheExtraWidthByStarWeight()
    {
        var (grid, _, _) = CreateGrid(HorizontalAlignment.Stretch);
        Layout(grid, availableWidth: 1200);

        double usable = 1200 - SPACING;
        Assert.AreEqual(usable * 10 / 17, grid.ColumnDefinitions[0].ActualWidth, 0.5);
        Assert.AreEqual(usable * 7 / 17, grid.ColumnDefinitions[1].ActualWidth, 0.5);
    }

    [TestMethod]
    public void SqueezedGridStillShrinksStarCells()
    {
        var (grid, _, _) = CreateGrid(HorizontalAlignment.Stretch);
        Layout(grid, availableWidth: 400);

        double total = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[1].ActualWidth;
        Assert.AreEqual(400 - SPACING, total, 0.5,
            "A grid narrower than its content must still fill exactly the width it was given.");
    }

    private static (Grid Grid, Border Left, Border Right) CreateGrid(HorizontalAlignment alignment)
    {
        var left = new Border().MinWidth(LEFT_SIZE).MaxWidth(LEFT_SIZE).MinHeight(LEFT_SIZE).MaxHeight(LEFT_SIZE);
        var right = new Border().MinWidth(RIGHT_SIZE).MinHeight(100);
        var grid = new Grid().Spacing(SPACING).Columns("10*, 7*").Children(
            left.Column(0),
            right.Column(1));
        grid.HorizontalAlignment = alignment;
        grid.VerticalAlignment = VerticalAlignment.Center;
        return (grid, left, right);
    }

    private static void Layout(Grid grid, double availableWidth)
    {
        var host = new Border().Child(grid);
        host.Measure(new Size(availableWidth, 800));
        host.Arrange(new Rect(0, 0, availableWidth, 800));
    }
}
