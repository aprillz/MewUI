using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// A grid view hands its own row container to the prepare hook, so behavior attached there covers
/// the whole row regardless of which column the pointer is over.
/// </summary>
[TestClass]
public sealed class GridViewRowTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;

    [TestMethod]
    public void PrepareContainer_ReceivesTheRowWithItsIndex()
    {
        var seen = new List<(int Index, string Item)>();
        var grid = MakeGrid();
        grid.PrepareContainer<string>((row, item, index, _) =>
        {
            seen.Add((index, item));
            Assert.AreEqual(index, row.Index);
        });

        Layout(grid);

        Assert.IsGreaterThan(0, seen.Count);
        for (int i = 0; i < seen.Count; i++)
        {
            Assert.AreEqual("Item " + seen[i].Index, seen[i].Item);
        }
    }

    [TestMethod]
    public void TheRowCarriesAContextMenu()
    {
        var menu = new ContextMenu();
        var grid = MakeGrid();
        grid.PrepareContainer<string>((row, _, _, _) => row.ContextMenu = menu);

        Layout(grid);

        int checkedRows = 0;
        grid.VisitRealizedRows((_, row) =>
        {
            Assert.AreSame(menu, row.ContextMenu);
            checkedRows++;
        });
        Assert.IsGreaterThan(0, checkedRows);
    }

    [TestMethod]
    public void RowProperties_DoNotSurviveIntoTheNextItem()
    {
        var menu = new ContextMenu();
        var grid = MakeGrid();
        grid.PrepareContainer<string>((row, _, index, _) =>
        {
            if (index == 0)
            {
                row.ContextMenu = menu;
            }
        });
        Layout(grid);

        grid.ScrollIntoView(60);
        Layout(grid);

        grid.VisitRealizedRows((index, row) =>
        {
            if (index != 0)
            {
                Assert.IsNull(row.ContextMenu, $"row {index} kept the first item's menu");
            }
        });
    }

    [TestMethod]
    public void IsSelected_FollowsTheSelection()
    {
        var grid = MakeGrid();
        grid.PrepareContainer<string>((_, _, _, _) => { });
        Layout(grid);

        grid.SelectedIndex = 2;
        Layout(grid);

        grid.VisitRealizedRows((index, row) => Assert.AreEqual(index == 2, row.IsSelected, $"row {index}"));

        grid.SelectedIndex = 4;
        Layout(grid);

        grid.VisitRealizedRows((index, row) => Assert.AreEqual(index == 4, row.IsSelected, $"row {index}"));
    }

    [TestMethod]
    public void ClearContainer_RunsBeforeTheRowTakesAnotherItem()
    {
        var cleared = new List<int>();
        var grid = MakeGrid();
        grid.PrepareContainer<string>((_, _, _, _) => { });
        grid.ClearContainer<string>((_, _, index, _) => cleared.Add(index));
        Layout(grid);

        Assert.AreEqual(0, cleared.Count);

        grid.ScrollIntoView(80);
        Layout(grid);

        Assert.IsGreaterThan(0, cleared.Count);
    }

    private static GridView MakeGrid()
    {
        var grid = new GridView { Width = WIDTH, Height = HEIGHT };
        grid.ItemsSource = ItemsView.Create(Enumerable.Range(0, 100).Select(i => "Item " + i).ToArray());
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
        return grid;
    }

    private static void Layout(GridView grid)
    {
        grid.Measure(new Size(WIDTH, HEIGHT));
        grid.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
    }
}
