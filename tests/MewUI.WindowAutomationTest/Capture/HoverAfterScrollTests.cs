using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A scroll moves rows under a pointer that stays where it is. The window works the element under the
/// pointer out again on its dispatcher, which only the real application loop runs, so the row that
/// arrives there has to become the hovered one without another pointer move.
/// </summary>
[TestClass]
public sealed class HoverAfterScrollTests
{
    private const int ITEM_COUNT = 80;
    private const int SCROLL_TARGET = 40;

    [TestMethod]
    public Task AGridRow_ThatArrivesUnderThePointer_BecomesTheHoveredOne() => CaptureScene.RunAsync(async scene =>
    {
        var grid = new GridView { Margin = new Thickness(20) };
        grid.ItemsSource = ItemsView.Create(Enumerable.Range(0, ITEM_COUNT).Select(index => "Item " + index).ToArray());
        grid.SetColumns(
        [
            new GridViewColumn<string>
            {
                Header = "Text",
                Width = 200,
                CellTemplate = new DelegateTemplate<string>(
                    build: _ => new TextBlock(),
                    bind: (view, item, _, _) => ((TextBlock)view).Text = item),
            },
        ]);
        var window = await scene.ShowAsync(grid);

        var pointer = CaptureScene.Center(RowAt(grid, 3));
        await scene.Input.MoveAsync(window, pointer);
        Assert.IsTrue(
            await CaptureScene.WaitUntilAsync(() => HoveredRows(grid) is [3]),
            $"precondition: the row under the pointer is hovered (hovered: {Describe(HoveredRows(grid))})");

        grid.ScrollIntoView(SCROLL_TARGET);

        Assert.IsTrue(
            await CaptureScene.WaitUntilAsync(() => IndexUnder(grid, pointer) is int under && under != 3 && HoveredRows(grid) is [int hovered] && hovered == under),
            $"after the scroll row {IndexUnder(grid, pointer)?.ToString() ?? "none"} is under the pointer and the hovered rows are {Describe(HoveredRows(grid))}");

        var palette = grid.ThemeInternal.Palette;
        var expected = palette.ControlBackground.Lerp(palette.Accent, 0.15);
        int underNow = IndexUnder(grid, pointer)!.Value;
        Assert.IsTrue(
            await CaptureScene.WaitUntilAsync(() => RowAt(grid, underNow).Background == expected),
            "the row under the pointer did not take the hover background from its style");
        grid.VisitRealizedRows((index, row) =>
        {
            if (index != underNow)
            {
                Assert.AreEqual(0, row.Background.A, $"row {index} keeps a state background, the pointer is over row {underNow}");
            }
        });
    });

    private static GridViewRow RowAt(GridView grid, int index)
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

    private static int? IndexUnder(GridView grid, Point point)
    {
        int? under = null;
        grid.VisitRealizedRows((index, row) =>
        {
            if (row.Bounds.Contains(point))
            {
                under = index;
            }
        });
        return under;
    }

    private static int[] HoveredRows(GridView grid)
    {
        var hovered = new List<int>();
        grid.VisitRealizedRows((index, row) =>
        {
            if (row.IsMouseOver)
            {
                hovered.Add(index);
            }
        });
        return hovered.ToArray();
    }

    private static string Describe(int[] rows) => rows.Length == 0 ? "none" : string.Join(", ", rows);
}
