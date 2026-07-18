using System.Collections.ObjectModel;

using Aprillz.MewUI;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class MultiSelectableItemsViewTests
{
    private static ItemsView<string> CreateView(int count, ItemsSelectionMode mode)
    {
        var items = new List<string>();
        for (int index = 0; index < count; index++)
        {
            items.Add($"item {index}");
        }
        return new ItemsView<string>(items) { SelectionMode = mode };
    }

    private static string Set(IMultiSelectableItemsView view) => string.Join(",", view.SelectedIndices);

    [TestMethod]
    public void DefaultMode_IsSingle()
    {
        var view = new ItemsView<string>(["a", "b", "c"]);
        Assert.AreEqual(ItemsSelectionMode.Single, view.SelectionMode);
    }

    [TestMethod]
    public void SelectedIndex_KeepsSetConsistent()
    {
        var view = new ItemsView<string>(["a", "b", "c"]);
        view.SelectedIndex = 1;
        Assert.AreEqual("1", Set(view));
        Assert.IsTrue(view.IsSelected(1));
        Assert.AreEqual(1, view.AnchorIndex);
    }

    [TestMethod]
    public void Single_ClickReplaces()
    {
        var view = CreateView(10, ItemsSelectionMode.Single);
        ItemsSelectionInput.HandleClick(view, 3, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 5, ModifierKeys.Control);
        Assert.AreEqual("5", Set(view));
        Assert.AreEqual(5, view.SelectedIndex);
    }

    [TestMethod]
    public void Extended_PlainCtrlShift()
    {
        var view = CreateView(10, ItemsSelectionMode.Extended);
        ItemsSelectionInput.HandleClick(view, 3, ModifierKeys.None);
        Assert.AreEqual("3", Set(view));

        ItemsSelectionInput.HandleClick(view, 5, ModifierKeys.Control);
        Assert.AreEqual("3,5", Set(view));

        ItemsSelectionInput.HandleClick(view, 3, ModifierKeys.Control);
        Assert.AreEqual("5", Set(view));
    }

    [TestMethod]
    public void Extended_ShiftRange()
    {
        var view = CreateView(10, ItemsSelectionMode.Extended);
        ItemsSelectionInput.HandleClick(view, 2, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 6, ModifierKeys.Shift);
        Assert.AreEqual("2,3,4,5,6", Set(view));
        Assert.AreEqual(6, view.SelectedIndex);

        ItemsSelectionInput.HandleClick(view, 8, ModifierKeys.Control);
        ItemsSelectionInput.HandleClick(view, 9, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.AreEqual("2,3,4,5,6,8,9", Set(view));
    }

    [TestMethod]
    public void Multiple_PlainToggleAndShiftRange()
    {
        var view = CreateView(10, ItemsSelectionMode.Multiple);
        ItemsSelectionInput.HandleClick(view, 2, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 5, ModifierKeys.None);
        Assert.AreEqual("2,5", Set(view));

        ItemsSelectionInput.HandleClick(view, 2, ModifierKeys.None);
        Assert.AreEqual("5", Set(view));

        ItemsSelectionInput.HandleClick(view, 8, ModifierKeys.Shift); // range from anchor 2
        Assert.AreEqual("2,3,4,5,6,7,8", Set(view));
    }

    [TestMethod]
    public void Insert_ShiftsSelectionDown()
    {
        var items = new ObservableCollection<string> { "0", "1", "2", "3", "4" };
        var view = new ItemsView<string>(items) { SelectionMode = ItemsSelectionMode.Extended };
        ItemsSelectionInput.HandleClick(view, 1, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 3, ModifierKeys.Control);

        items.Insert(0, "new");

        Assert.AreEqual("2,4", Set(view));
    }

    [TestMethod]
    public void Remove_DropsAndShifts()
    {
        var items = new ObservableCollection<string> { "0", "1", "2", "3", "4" };
        var view = new ItemsView<string>(items) { SelectionMode = ItemsSelectionMode.Extended };
        ItemsSelectionInput.HandleClick(view, 1, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 3, ModifierKeys.Control);

        items.RemoveAt(1); // drop selected index 1

        Assert.AreEqual("2", Set(view));
    }

    [TestMethod]
    public void SwitchToSingle_CollapsesToPrimary()
    {
        var view = CreateView(5, ItemsSelectionMode.Extended);
        ItemsSelectionInput.HandleClick(view, 0, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 2, ModifierKeys.Control);
        ItemsSelectionInput.HandleClick(view, 4, ModifierKeys.Control);

        view.SelectionMode = ItemsSelectionMode.Single;

        Assert.AreEqual("4", Set(view));
    }

    [TestMethod]
    public void ClearSelection_EmptiesSet()
    {
        var view = CreateView(5, ItemsSelectionMode.Extended);
        ItemsSelectionInput.HandleClick(view, 1, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 3, ModifierKeys.Control);

        view.ClearSelection();

        Assert.AreEqual(string.Empty, Set(view));
        Assert.AreEqual(-1, view.SelectedIndex);
    }
}
