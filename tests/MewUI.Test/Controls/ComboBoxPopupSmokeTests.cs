using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ComboBoxPopupSmokeTests
{
    [TestMethod]
    public void ComboBox_OpenCloseReopen_DoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var combo = new ComboBox().Items(["User", "Admin", "Guest"]);
        window.Content = combo;
        window.PerformLayout();

        combo.IsDropDownOpen = true;
        combo.IsDropDownOpen = false;
        combo.IsDropDownOpen = true;
        combo.IsDropDownOpen = false;
    }

    private sealed class RowItem
    {
        public int RoleIndex { get; set; }
    }

    [TestMethod]
    public void ComboBox_InsideGridViewCell_OpensAndReopens()
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
                    .Header("Role")
                    .Width(120)
                    .Bind(
                        build: _ => new ComboBox().Items(["User", "Admin", "Guest"]),
                        bind: (view, item) => ((ComboBox)view).SelectedIndex = item.RoleIndex));
        grid.ItemsSource = ItemsView.Create(new[] { new RowItem { RoleIndex = 0 }, new RowItem { RoleIndex = 1 } });

        window.Content = grid;
        window.PerformLayout();

        var combo = (ComboBox?)VisualTree.Find(grid, element => element is ComboBox);
        Assert.IsNotNull(combo, "cell template must have built a ComboBox");

        combo.IsDropDownOpen = true;
        Assert.IsTrue(combo.IsDropDownOpen, "dropdown must stay open after opening");
        combo.IsDropDownOpen = false;
        combo.IsDropDownOpen = true;
        Assert.IsTrue(combo.IsDropDownOpen);
        combo.IsDropDownOpen = false;
    }
}
