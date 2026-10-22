using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A click raises <see cref="NavigationList.ItemInvoked"/> before it selects the row, so a handler can keep
/// the selection where it was by marking the activation handled.
/// </summary>
[TestClass]
public sealed class NavigationListInvokeTests
{
    private static NavigationList? CreateList()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return null;
        }

        var window = HeadlessWindow.Create(200, 300);
        var list = new NavigationList();
        list.Items(new[] { "Home", "Settings", "Exit" });
        window.Content = list;
        window.PerformLayout();
        return list;
    }

    private static void Click(NavigationList list, int index)
    {
        Assert.IsTrue(list.TryGetContainer(index, out var container));
        var bounds = container!.Bounds;
        var point = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        list.RaiseMouseDown(new MouseEventArgs(point, point, MouseButton.Left, leftButton: true));
        list.RaiseMouseUp(new MouseEventArgs(point, point, MouseButton.Left, leftButton: false));
    }

    [TestMethod]
    public void Click_RaisesItemInvokedBeforeSelecting()
    {
        var list = CreateList();
        if (list == null)
        {
            return;
        }

        list.SelectedIndex = 0;
        int selectionAtInvoke = -1;
        list.ItemInvoked += args => selectionAtInvoke = list.SelectedIndex;

        Click(list, 1);

        Assert.AreEqual(0, selectionAtInvoke);
        Assert.AreEqual(1, list.SelectedIndex);
    }

    [TestMethod]
    public void Click_HandledInvokeLeavesSelectionUnchanged()
    {
        var list = CreateList();
        if (list == null)
        {
            return;
        }

        list.SelectedIndex = 0;
        int selectionChanges = 0;
        list.SelectionChanged += _ => selectionChanges++;
        object? invoked = null;
        list.ItemInvoked += args =>
        {
            invoked = args.Item;
            args.Handled = true;
        };

        Click(list, 2);

        Assert.AreEqual("Exit", invoked);
        Assert.AreEqual(0, list.SelectedIndex);
        Assert.AreEqual(0, selectionChanges);
    }
}
