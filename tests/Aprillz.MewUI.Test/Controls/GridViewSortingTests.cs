using System.Collections.ObjectModel;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class GridViewSortingTests
{
    private sealed record Row(int Id, string Name, int Value);

    private sealed class SingleView(IReadOnlyList<Row> rows) : ISelectableItemsView
    {
        private int _selectedIndex = -1;

        public int Count => rows.Count;
        public Func<object?, object?>? KeySelector => null;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int next = Count == 0 ? -1 : Math.Clamp(value, -1, Count - 1);
                if (_selectedIndex == next) return;
                _selectedIndex = next;
                SelectionChanged?.Invoke(next);
            }
        }
        public object? SelectedItem
        {
            get => SelectedIndex >= 0 ? rows[SelectedIndex] : null;
            set
            {
                if (value is not Row row)
                {
                    SelectedIndex = -1;
                    return;
                }
                int found = -1;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (ReferenceEquals(rows[i], row) || Equals(rows[i], row))
                    {
                        found = i;
                        break;
                    }
                }
                SelectedIndex = found;
            }
        }
        public event Action<ItemsChange>? Changed;
        public event Action<int>? SelectionChanged;
        public object? GetItem(int index) => rows[index];
        public string GetText(int index) => rows[index].Name;
        public void Invalidate() => Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, Count));
    }

    [TestMethod]
    public void Permutation_IsStableAndDescendingDoesNotNegateComparerResult()
    {
        Row[] rows =
        [
            new(0, "first", 2),
            new(1, "second", 1),
            new(2, "third", 2),
            new(3, "fourth", 3),
        ];
        var source = ItemsView.Create(rows);
        Comparison<object?> comparison = (left, right) =>
        {
            int x = ((Row)left!).Value;
            int y = ((Row)right!).Value;
            return x == y ? 0 : x < y ? int.MinValue : int.MaxValue;
        };

        int[] ascending = GridViewSortPermutation.Build(source, comparison, GridViewSortDirection.Ascending);
        int[] descending = GridViewSortPermutation.Build(source, comparison, GridViewSortDirection.Descending);

        CollectionAssert.AreEqual(new[] { 1, 0, 2, 3 }, ascending);
        CollectionAssert.AreEqual(new[] { 3, 0, 2, 1 }, descending);
    }

    [TestMethod]
    public void Core_SortsOneColumnAndClearRestoresSourceOrder()
    {
        Row[] rows =
        [
            new(0, "c", 30),
            new(1, "a", 10),
            new(2, "b", 20),
        ];
        var core = CreateCore(rows,
            Column((left, right) => StringComparer.Ordinal.Compare(((Row)left!).Name, ((Row)right!).Name)),
            Column((left, right) => ((Row)left!).Value.CompareTo(((Row)right!).Value)));
        var changes = new List<GridViewSortChange>();
        core.SortChanged += changes.Add;

        core.SortByColumn(0, GridViewSortDirection.Ascending);
        AssertDisplayIds(core, 1, 2, 0);

        core.SortByColumn(0, GridViewSortDirection.Descending);
        AssertDisplayIds(core, 0, 2, 1);

        core.SortByColumn(1, GridViewSortDirection.Ascending);
        AssertDisplayIds(core, 1, 2, 0);

        core.ClearSort();
        AssertDisplayIds(core, 0, 1, 2);
        CollectionAssert.AreEqual(rows, rows.OrderBy(row => row.Id).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                new GridViewSortChange(0, GridViewSortDirection.Ascending),
                new GridViewSortChange(0, GridViewSortDirection.Descending),
                new GridViewSortChange(1, GridViewSortDirection.Ascending),
                new GridViewSortChange(-1, GridViewSortDirection.None),
            },
            changes);
    }

    [TestMethod]
    public void Core_ComparerFailureLeavesPreviousSortAndPermutationUntouched()
    {
        Row[] rows = [new(0, "c", 30), new(1, "a", 10), new(2, "b", 20)];
        var core = CreateCore(rows,
            Column((left, right) => StringComparer.Ordinal.Compare(((Row)left!).Name, ((Row)right!).Name)),
            Column((_, _) => throw new InvalidOperationException("comparison failed")));
        core.SortByColumn(0, GridViewSortDirection.Ascending);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => core.SortByColumn(1, GridViewSortDirection.Ascending));

        Assert.AreEqual(0, core.SortColumnIndex);
        Assert.AreEqual(GridViewSortDirection.Ascending, core.SortDirection);
        AssertDisplayIds(core, 1, 2, 0);
    }

    [TestMethod]
    public void Sort_RetainsSelectedItemsAndRemapsDisplayIndices()
    {
        Row[] rows =
        [
            new(0, "c", 30),
            new(1, "a", 10),
            new(2, "b", 20),
            new(3, "d", 40),
        ];
        var source = ItemsView.Create(rows);
        source.SelectionMode = ItemsSelectionMode.Extended;
        var core = CreateCore(source,
            Column((left, right) => StringComparer.Ordinal.Compare(((Row)left!).Name, ((Row)right!).Name)));
        source.SelectSingle(0);
        source.SetSelected(2, true);

        core.SortByColumn(0, GridViewSortDirection.Ascending);

        Assert.AreSame(rows[2], core.SelectedItem);
        CollectionAssert.AreEqual(new[] { 1, 2 }, core.SelectedIndices.ToArray());
        Assert.IsTrue(core.IsItemSelected(1));
        Assert.IsTrue(core.IsItemSelected(2));
        Assert.AreEqual(2, core.MultiView!.AnchorIndex);

        core.MultiView.SelectSingle(0);
        Assert.AreSame(rows[1], source.SelectedItem);
        Assert.AreEqual(0, core.SelectedIndex);
        Assert.AreEqual(1, source.SelectedIndex);
    }

    [TestMethod]
    public void Sort_PreservesSingleSelectionCapabilityAndSelectedItem()
    {
        Row[] rows = [new(0, "c", 30), new(1, "a", 10), new(2, "b", 20)];
        var source = new SingleView(rows);
        var core = CreateCore(source,
            Column((left, right) => StringComparer.Ordinal.Compare(((Row)left!).Name, ((Row)right!).Name)));
        source.SelectedIndex = 0;

        core.SortByColumn(0, GridViewSortDirection.Ascending);

        Assert.IsNull(core.MultiView);
        Assert.AreSame(rows[0], core.SelectedItem);
        Assert.AreEqual(2, core.SelectedIndex);

        core.SelectedIndex = 0;
        Assert.AreSame(rows[1], source.SelectedItem);
    }

    [TestMethod]
    public void ActiveSort_RebuildsAfterSourceChangesAndRaisesDisplayReset()
    {
        var rows = new ObservableCollection<Row>
        {
            new(0, "c", 30),
            new(1, "a", 10),
        };
        var core = CreateCore(rows,
            Column((left, right) => StringComparer.Ordinal.Compare(((Row)left!).Name, ((Row)right!).Name)));
        core.SortByColumn(0, GridViewSortDirection.Ascending);
        ItemsChange? observed = null;
        core.ItemsChanged += change => observed = change;

        rows.Add(new Row(2, "0", 5));

        AssertDisplayIds(core, 2, 1, 0);
        Assert.IsNotNull(observed);
        Assert.AreEqual(ItemsChangeKind.Reset, observed.Value.Kind);
    }

    [TestMethod]
    public void SortState_PersistsAcrossAddColumnsAndItemsSourceSwap_ButSetColumnsClearsIt()
    {
        Row[] first = [new(0, "b", 2), new(1, "a", 1)];
        var sortable = Column(
            (left, right) => StringComparer.Ordinal.Compare(((Row)left!).Name, ((Row)right!).Name));
        var core = CreateCore(first, sortable);
        core.SortByColumn(0, GridViewSortDirection.Ascending);

        core.AddColumns([Column(null)]);
        Assert.AreEqual(0, core.SortColumnIndex);
        Assert.AreEqual(GridViewSortDirection.Ascending, core.SortDirection);

        Row[] second = [new(2, "z", 3), new(3, "c", 4)];
        core.SetItems(ItemsView.Create(second));
        AssertDisplayIds(core, 3, 2);
        Assert.AreEqual(0, core.SortColumnIndex);

        core.SetColumns([sortable]);
        Assert.AreEqual(-1, core.SortColumnIndex);
        Assert.AreEqual(GridViewSortDirection.None, core.SortDirection);
        AssertDisplayIds(core, 2, 3);
    }

    [TestMethod]
    public void ProgrammaticApi_RejectsInvalidOrNonSortableColumns()
    {
        var grid = new GridView();
        grid.SetColumns(
        [
            new GridViewColumn<Row>
            {
                Header = "Name",
                Width = GridLength.Pixels(100),
                CellTemplate = Template(),
            },
        ]);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => grid.SortByColumn(3, GridViewSortDirection.Ascending));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => grid.SortByColumn(0, (GridViewSortDirection)99));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => grid.SortByColumn(0, GridViewSortDirection.Ascending));
    }

    [TestMethod]
    public void HeaderClick_CyclesSortAndSeparatorKeepsResizePriority()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        Row[] rows = [new(0, "b", 2), new(1, "a", 1)];
        var source = ItemsView.Create(rows);
        var grid = new GridView { BorderThickness = 0, Padding = default, HeaderHeight = 30 };
        grid.ItemsSource = source;
        grid.SetColumns(
        [
            new GridViewColumn<Row>()
                .Header("Name")
                .PixelWidth(150)
                .Text(row => row.Name)
                .SortBy(row => row.Name, StringComparer.Ordinal),
            new GridViewColumn<Row>()
                .Header("Value")
                .PixelWidth(150)
                .Text(row => row.Value.ToString())
                .SortBy(row => row.Value),
        ]);

        var window = HeadlessWindow.Create(300, 180);
        window.Content = grid;
        window.PerformLayout();
        var firstHeader = new Point(grid.Bounds.X + 70, grid.Bounds.Y + 12);

        window.SendMouseDown(firstHeader);
        window.SendMouseUp(new Point(grid.Bounds.Right + 20, grid.Bounds.Y + 12));
        Assert.AreEqual(GridViewSortDirection.None, grid.SortDirection);

        window.SendClick(firstHeader);
        Assert.AreEqual(0, grid.SortColumnIndex);
        Assert.AreEqual(GridViewSortDirection.Ascending, grid.SortDirection);

        window.SendClick(firstHeader);
        Assert.AreEqual(GridViewSortDirection.Descending, grid.SortDirection);

        window.SendClick(firstHeader);
        Assert.AreEqual(-1, grid.SortColumnIndex);
        Assert.AreEqual(GridViewSortDirection.None, grid.SortDirection);

        window.SendClick(new Point(grid.Bounds.X + 220, grid.Bounds.Y + 12));
        Assert.AreEqual(1, grid.SortColumnIndex);
        Assert.AreEqual(GridViewSortDirection.Ascending, grid.SortDirection);

        window.SendClick(new Point(grid.Bounds.X + 150, grid.Bounds.Y + 12));
        Assert.AreEqual(1, grid.SortColumnIndex);
        Assert.AreEqual(GridViewSortDirection.Ascending, grid.SortDirection);
        Assert.AreSame(source, grid.ItemsSource);
    }

    private static GridView.GridViewCore CreateCore(
        IReadOnlyList<Row> rows,
        params GridView.GridViewCore.ColumnDefinition[] columns)
        => CreateCore(ItemsView.Create(rows), columns);

    private static GridView.GridViewCore CreateCore(
        ISelectableItemsView source,
        params GridView.GridViewCore.ColumnDefinition[] columns)
    {
        var core = new GridView.GridViewCore();
        core.SetColumns(columns);
        core.SetItems(source);
        return core;
    }

    private static GridView.GridViewCore.ColumnDefinition Column(Comparison<object?>? comparison)
        => new("Column", GridLength.Pixels(100), 0, double.PositiveInfinity, true, Template(), comparison);

    private static IDataTemplate<Row> Template()
        => new DelegateTemplate<Row>(
            build: _ => new TextBlock(),
            bind: static (_, _, _, _) => { });

    private static void AssertDisplayIds(GridView.GridViewCore core, params int[] expected)
    {
        int[] actual = Enumerable.Range(0, core.ItemsSource.Count)
            .Select(index => ((Row)core.ItemsSource.GetItem(index)!).Id)
            .ToArray();
        CollectionAssert.AreEqual(expected, actual);
    }
}
