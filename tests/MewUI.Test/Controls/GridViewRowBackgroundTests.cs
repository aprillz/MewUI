using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Pins the pixels a <see cref="GridViewRow"/> paints for selection, hover and alternating rows, so the
/// way the row comes by those colours can change without changing what a grid looks like. Each test
/// lays a <see cref="GridView"/> out in a window, runs the update a frame runs, renders into an
/// offscreen GDI surface and reads the row's own pixels.
/// Not parallelizable: renders through the process-wide graphics factory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GridViewRowBackgroundTests
{
    private const int SURFACE_WIDTH = 260;
    private const int SURFACE_HEIGHT = 220;
    private const int ITEM_COUNT = 6;

    // Sampled at the row's trailing edge: the cell text sits against the leading edge, and the row's
    // vertical middle keeps the sample clear of an antialiased rounded corner.
    private const int SAMPLE_INSET = 8;

    [TestMethod]
    public void SelectedRow_PaintsTheThemeSelectionBackground()
    {
        if (!TryCreateGrid(zebraStriping: false, out var window, out var grid))
        {
            return;
        }

        grid.SelectedIndex = 1;
        byte[] pixels = RenderFrame(window, grid);

        AssertRowColor(pixels, grid, 1, grid.ThemeInternal.Palette.SelectionBackground, "the selected row");
    }

    [TestMethod]
    public void HoveredRow_PaintsTheHoverBackground()
    {
        if (!TryCreateGrid(zebraStriping: false, out var window, out var grid))
        {
            return;
        }

        var palette = grid.ThemeInternal.Palette;
        var hovered = Row(grid, 2).Bounds;
        window.SendMouseMove(new Point(hovered.X + (hovered.Width / 2), hovered.Y + (hovered.Height / 2)));
        byte[] pixels = RenderFrame(window, grid);

        AssertRowColor(pixels, grid, 2, palette.ControlBackground.Lerp(palette.Accent, 0.15), "the hovered row");
    }

    [TestMethod]
    public void AlternatingRows_PaintTheStripe()
    {
        if (!TryCreateGrid(zebraStriping: true, out var window, out var grid))
        {
            return;
        }

        byte[] pixels = RenderFrame(window, grid);
        var even = ReadPixel(pixels, SamplePoint(grid, 2));
        var odd = ReadPixel(pixels, SamplePoint(grid, 3));

        Assert.AreNotEqual(even, odd, "an alternating row looks the same as the row before it");
    }

    [TestMethod]
    public void SelectionWins_OverTheStripeAndTheHover()
    {
        if (!TryCreateGrid(zebraStriping: true, out var window, out var grid))
        {
            return;
        }

        var row = Row(grid, 3).Bounds;
        window.SendMouseMove(new Point(row.X + (row.Width / 2), row.Y + (row.Height / 2)));
        grid.SelectedIndex = 3;
        byte[] pixels = RenderFrame(window, grid);

        AssertRowColor(pixels, grid, 3, grid.ThemeInternal.Palette.SelectionBackground, "a selected, hovered, alternating row");
    }

    [TestMethod]
    public void SelectedAndHoveredRows_TakeTheirBackgroundFromTheStyle()
    {
        if (!TryCreateGrid(zebraStriping: false, out var window, out var grid))
        {
            return;
        }

        var palette = grid.ThemeInternal.Palette;
        var hovered = Row(grid, 2).Bounds;
        window.SendMouseMove(new Point(hovered.X + (hovered.Width / 2), hovered.Y + (hovered.Height / 2)));
        grid.SelectedIndex = 4;
        window.PerformLayout();
        window.PerformLayout();

        Assert.AreEqual(palette.ControlBackground.Lerp(palette.Accent, 0.15), Row(grid, 2).Background, "the hovered row");
        Assert.AreEqual(palette.SelectionBackground, Row(grid, 4).Background, "the selected row");
        Assert.AreEqual(0, Row(grid, 0).Background.A, "a row that is neither");
    }

    [TestMethod]
    public void AStyleForTheRow_ChangesTheSelectionColour()
    {
        if (!TryCreateGrid(zebraStriping: false, out var window, out var grid))
        {
            return;
        }

        var chosen = Color.FromArgb(255, 20, 160, 90);
        var sheet = new StyleSheet();
        sheet.Define<GridViewRow>(new Style(typeof(GridViewRow))
        {
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Setters = [Setter.Create(Control.BackgroundProperty, chosen)],
                },
            ],
        });
        window.StyleSheet = sheet;
        grid.SelectedIndex = 1;
        byte[] pixels = RenderFrame(window, grid);

        AssertRowColor(pixels, grid, 1, chosen, "the selected row under an application style");
    }

    [TestMethod]
    public void ACellInACollapsedColumn_IsNeitherDrawnNorHit()
    {
        if (!TryCreateGrid(zebraStriping: false, out var window, out var grid))
        {
            return;
        }

        var row = Row(grid, 1);
        FrameworkElement? cell = null;
        VisualTree.Visit(row, element =>
        {
            if (cell == null && element is TextBlock block)
            {
                cell = block;
            }
        });
        Assert.IsNotNull(cell, "the cell is not among the row's visual children");

        var inside = new Point(cell.Bounds.X + 4, cell.Bounds.Y + (cell.Bounds.Height / 2));
        Assert.AreSame(cell, row.HitTest(inside), "precondition: the cell is hit while its column is shown");

        grid.SetColumns(TextColumn(width: 0));
        window.PerformLayout();
        window.PerformLayout();

        Assert.AreSame(row, row.HitTest(inside), "a cell of a collapsed column took the hit");
        bool stillVisited = false;
        VisualTree.Visit(row, element => stillVisited |= ReferenceEquals(element, cell));
        Assert.IsTrue(stillVisited, "the cell of a collapsed column left the row, so it would miss theme and DPI changes");
        Assert.AreEqual("Item 1", ((TextBlock)cell).Text, "the cell of a collapsed column lost its binding");
    }

    private static bool TryCreateGrid(bool zebraStriping, out Window window, out GridView grid)
    {
        window = null!;
        grid = null!;
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return false;
        }

        Application.DefaultGraphicsFactory = new Aprillz.MewUI.Rendering.Gdi.GdiGraphicsFactory();
        grid = new GridView { Width = 240, Height = 200, ZebraStriping = zebraStriping };
        grid.ItemsSource = ItemsView.Create(Enumerable.Range(0, ITEM_COUNT).Select(index => "Item " + index).ToArray());
        grid.SetColumns(TextColumn(width: 120));

        window = HeadlessWindow.Create(SURFACE_WIDTH, SURFACE_HEIGHT);
        window.Content = grid;
        window.PerformLayout();
        window.PerformLayout();
        return true;
    }

    private static GridViewColumn<string>[] TextColumn(double width) =>
    [
        new GridViewColumn<string>
        {
            Header = "Text",
            Width = width,
            CellTemplate = new DelegateTemplate<string>(
                build: _ => new TextBlock(),
                bind: (view, item, _, _) => ((TextBlock)view).Text = item),
        },
    ];

    private static GridViewRow Row(GridView grid, int index)
    {
        GridViewRow? found = null;
        grid.VisitRealizedRows((rowIndex, row) =>
        {
            if (rowIndex == index)
            {
                found = row;
            }
        });

        Assert.IsNotNull(found, $"row {index} is not realized");
        return found;
    }

    private static byte[] RenderFrame(Window window, GridView grid)
    {
        // A frame brings layout and the visual states up to date before it draws.
        window.PerformLayout();
        window.PerformLayout();

        var factory = Application.DefaultGraphicsFactory;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            grid.Render(context);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var source = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        var copy = new byte[SURFACE_WIDTH * SURFACE_HEIGHT * 4];
        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            source.Slice(row * stride, SURFACE_WIDTH * 4).CopyTo(copy.AsSpan(row * SURFACE_WIDTH * 4));
        }

        return copy;
    }

    private static (int X, int Y) SamplePoint(GridView grid, int index)
    {
        var bounds = Row(grid, index).Bounds;
        return ((int)bounds.Right - SAMPLE_INSET, (int)(bounds.Y + (bounds.Height / 2)));
    }

    private static Color ReadPixel(byte[] pixels, (int X, int Y) point)
    {
        int offset = ((point.Y * SURFACE_WIDTH) + point.X) * 4;
        return Color.FromArgb(255, pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static void AssertRowColor(byte[] pixels, GridView grid, int index, Color expected, string what)
    {
        var point = SamplePoint(grid, index);
        var actual = ReadPixel(pixels, point);
        Assert.IsTrue(
            Math.Abs(actual.R - expected.R) <= 1 && Math.Abs(actual.G - expected.G) <= 1 && Math.Abs(actual.B - expected.B) <= 1,
            $"{what} (index {index}) sampled at {point}: expected RGB({expected.R},{expected.G},{expected.B}) but read RGB({actual.R},{actual.G},{actual.B})");
    }
}
