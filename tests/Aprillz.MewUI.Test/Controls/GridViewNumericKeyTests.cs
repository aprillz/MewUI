using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class GridViewNumericKeyTests
{
    private sealed class RowItem
    {
        public RowItem(double amount) => Amount = new ObservableValue<double>(amount);
        public ObservableValue<double> Amount { get; }
    }

    [TestMethod]
    public void ArrowKeys_OnFocusedNumericUpDownInCell_ApplyEveryPress()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var grid = new GridView()
            .RowHeight(30)
            .Columns(
                new GridViewColumn<RowItem>()
                    .Header("Amount")
                    .Width(110)
                    .Template(
                        build: _ => new NumericUpDown().Padding(6, 0).CenterVertical().Minimum(0).Maximum(100).Step(0.5).Format("0.##"),
                        bind: (view, row) => view.BindValue(row.Amount)));
        var items = new[]
        {
            new RowItem(10),
            new RowItem(20),
        };
        grid.ItemsSource = ItemsView.Create(items);

        // Hostile-host wiring: every value edit swaps in a fresh ItemsSource (a sort/filter
        // page re-applying its projection). The presenter's deferred-focus restore must bring
        // focus back to the control after the recycle round-trip so keyboard stepping survives.
        foreach (var item in items)
        {
            item.Amount.Changed += () =>
            {
                grid.ItemsSource = ItemsView.Create(items);
                window.PerformLayout();
            };
        }

        window.Content = grid;
        window.PerformLayout();

        var numeric = (NumericUpDown?)VisualTree.Find(grid, element => element is NumericUpDown);
        Assert.IsNotNull(numeric);

        // Tab-style focus: the control itself, no edit mode.
        numeric.Focus();
        double valueBefore = numeric.Value;
        Assert.IsFalse(numeric.IsEditing, "tab-style focus must not enter edit mode");

        window.SendKeyPress(Key.Up);
        double afterFirst = items[0].Amount.Value;
        var focusedAfterFirst = window.FocusManager.FocusedElement;

        window.SendKeyPress(Key.Up);
        double afterSecond = items[0].Amount.Value;
        var focusedAfterSecond = window.FocusManager.FocusedElement;

        Assert.AreEqual(valueBefore + 0.5, afterFirst,
            $"first Up must step the value (focused={focusedAfterFirst?.GetType().Name})");
        Assert.AreEqual(valueBefore + 1.0, afterSecond,
            $"second Up must step again (focusedAfterFirst={focusedAfterFirst?.GetType().Name}, focusedAfterSecond={focusedAfterSecond?.GetType().Name}, gridSelected={grid.SelectedIndex})");
    }

}
