using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Pointer-driven selection semantics for GridView: selection must observe clicks without
/// consuming them, so interactive cell content (ComboBox etc.) keeps working, while the
/// row/cell double-dispatch still applies non-idempotent multi-select operations only once.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GridViewPointerSelectionTests
{
    private sealed class RowItem
    {
        public string Name { get; set; } = "";
        public int RoleIndex { get; set; }
    }

    private static GridView CreateGrid()
    {
        var grid = new GridView()
            .RowHeight(30)
            .Columns(
                new GridViewColumn<RowItem>()
                    .Header("Name")
                    .Width(150)
                    .Bind(
                        build: _ => new TextBlock(),
                        bind: (view, item) => ((TextBlock)view).Text = item.Name),
                new GridViewColumn<RowItem>()
                    .Header("Role")
                    .Width(200)
                    .Bind(
                        build: _ => new ComboBox().Items(["User", "Admin", "Guest"]),
                        bind: (view, item) => ((ComboBox)view).SelectedIndex = item.RoleIndex));
        grid.ItemsSource = ItemsView.Create(new[]
        {
            new RowItem { Name = "a", RoleIndex = 0 },
            new RowItem { Name = "b", RoleIndex = 1 },
            new RowItem { Name = "c", RoleIndex = 2 },
        });
        return grid;
    }

    // Regression: cell-level selection used to set e.Handled before DropDownBase's own logic
    // ran, so a mouse click never opened the popup (keyboard did). Selection must not consume.
    [TestMethod]
    public void Click_OnComboCell_OpensPopupAndSelectsRow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var grid = CreateGrid();
        window.Content = grid;
        window.PerformLayout();

        var combo = (ComboBox?)VisualTree.Find(grid, element => element is ComboBox);
        Assert.IsNotNull(combo);

        window.SendMouseDown(combo.CenterOf());

        Assert.IsTrue(combo.IsDropDownOpen, "mouse click must open the dropdown");
        Assert.AreEqual(0, grid.SelectedIndex, "clicking cell content must also select its row");

        window.SendMouseUp(combo.CenterOf());
    }

    // Regression guard for the dedup that replaced Handled: a click on a non-interactive cell
    // reaches both the cell handler and the bubbled row handler with the same pointer-down,
    // and a Ctrl+click toggle applied twice would cancel itself out.
    // The cell must be non-focusable content: focus moving into a focusable cell control also
    // syncs SelectedIndex (OnDescendantFocused), which is a separate pre-existing interaction.
    [TestMethod]
    public void CtrlClick_OnTextCell_TogglesRowExactlyOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var grid = CreateGrid();
        grid.SelectionMode = ItemsSelectionMode.Extended;
        window.Content = grid;
        window.PerformLayout();

        var text = (TextBlock?)VisualTree.Find(grid, element => element is TextBlock tb && tb.Text == "b");
        Assert.IsNotNull(text);
        var cellPoint = text.CenterOf();

        window.SendClick(cellPoint, modifiers: ModifierKeys.Control);
        CollectionAssert.Contains(grid.SelectedIndices.ToList(), 1, "first Ctrl+click must toggle the row on exactly once");

        window.SendClick(cellPoint, modifiers: ModifierKeys.Control);
        CollectionAssert.DoesNotContain(grid.SelectedIndices.ToList(), 1, "second Ctrl+click must toggle the row back off");
    }
}
