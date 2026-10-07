using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A menu item that is checkable has a check slot in the icon column: its menu keeps that column even without
/// icons, the check is drawn there only while the item is checked, and the item's own icon is not shown. The
/// column depends on the declaration, not on whether any check is shown.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MenuItemCheckTests
{
    private const int SIZE = 300;
    private const int ROW_COUNT = 4;
    private const int CHECK_ROW = 1;

    private IGraphicsFactory? _previousFactory;

    // A backend session leaves its disposed factory as the default; the tests after this class need the one before.
    [TestInitialize]
    public void KeepTheDefaultFactory() => _previousFactory = Application.DefaultGraphicsFactory;

    [TestCleanup]
    public void RestoreTheDefaultFactory() => Application.DefaultGraphicsFactory = _previousFactory!;

    [TestMethod]
    public void CheckableItem_WidensTheMenuByTheCheckColumn()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Measures text through a Win32 backend.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.Direct2D);
        var plain = ShowMenu(checkable: false, isChecked: false);
        var checkable = ShowMenu(checkable: true, isChecked: false);

        double column = ThemeMetrics.Default.CommandIconSize + ContextMenu.IconTextGap;
        Assert.AreEqual(plain.Menu.Bounds.Width + column, checkable.Menu.Bounds.Width, 0.001);
    }

    [TestMethod]
    public void ChangingTheCheck_KeepsTheMenuWidth()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Measures text through a Win32 backend.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.Direct2D);
        var shown = ShowMenu(checkable: true, isChecked: false);
        double width = shown.Menu.Bounds.Width;

        shown.Item.IsChecked = true;
        shown.Window.PerformLayout();

        Assert.AreEqual(width, shown.Menu.Bounds.Width, 0.001, "showing the check changed the menu width");
    }

    [TestMethod]
    public void CheckedWithoutBeingCheckable_ReservesNoColumn()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Measures text through a Win32 backend.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.Direct2D);
        var plain = ShowMenu(checkable: false, isChecked: false);
        var checkedOnly = ShowMenu(checkable: false, isChecked: true);

        Assert.AreEqual(plain.Menu.Bounds.Width, checkedOnly.Menu.Bounds.Width, 0.001);
    }

    [TestMethod]
    public void CheckableItem_SharesTheIconColumnAndShowsNoIcon()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Measures text through a Win32 backend.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.Direct2D);
        int plainIcons = 0;
        int checkableIcons = 0;
        var withIcon = ShowIconMenu(checkable: false, () => plainIcons++);
        var withCheck = ShowIconMenu(checkable: true, () => checkableIcons++);

        Assert.AreEqual(withIcon.Bounds.Width, withCheck.Bounds.Width, 0.001, "the check took a column of its own beside the icon column");
        Assert.AreEqual(1, plainIcons, "the icon of a plain item was not built");
        Assert.AreEqual(0, checkableIcons, "a checkable item built its icon");
    }

    /// <summary>A menu whose first item has an icon and whose second item, with an icon of its own, may be checkable.</summary>
    private static ContextMenu ShowIconMenu(bool checkable, Action iconBuilt)
    {
        var owner = new Border { Width = 50, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = HeadlessWindow.Create(SIZE, SIZE);
        window.Content = owner;
        window.PerformLayout();

        var menu = new ContextMenu();
        menu.AddEntry(new MenuItem("Menu row 0") { Icon = new IconTemplate(static size => new Border { Width = size.Dip, Height = size.Dip }) });
        menu.AddEntry(new MenuItem("Menu row 1")
        {
            IsCheckable = checkable,
            Icon = new IconTemplate(size =>
            {
                iconBuilt();
                return new Border { Width = size.Dip, Height = size.Dip };
            }),
        });

        menu.Show(owner, new Point(40, 40));
        window.PerformLayout();
        return menu;
    }

    [TestMethod]
    public void TheCheck_IsDrawnInItsColumnOnlyWhileChecked()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Renders through a Win32 backend.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.Direct2D);
        var off = Render(session, ShowMenu(checkable: true, isChecked: false), out var checkColumn);
        var on = Render(session, ShowMenu(checkable: true, isChecked: true), out _);

        int insideColumn = 0;
        int outsideColumn = 0;
        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                int offset = (y * SIZE + x) * 4;
                if (off[offset] == on[offset] && off[offset + 1] == on[offset + 1] && off[offset + 2] == on[offset + 2])
                {
                    continue;
                }

                if (checkColumn.Contains(new Point(x + 0.5, y + 0.5)))
                {
                    insideColumn++;
                }
                else
                {
                    outsideColumn++;
                }
            }
        }

        Assert.IsGreaterThan(0, insideColumn, "checking the item drew nothing in its check column");
        Assert.AreEqual(0, outsideColumn, "checking the item changed pixels outside its check column");
    }

    [TestMethod]
    public void MarkupExtensions_SetAndBindTheCheck()
    {
        var chosen = new ObservableValue<int>(1);
        var first = new MenuItem("One").IsCheckable().BindIsChecked(chosen, value => value == 1);
        var second = new MenuItem("Two").IsCheckable().BindIsChecked(chosen, value => value == 2);
        var flag = new ObservableValue<bool>(false);
        var toggle = new MenuItem("Toggle").IsCheckable().BindIsChecked(flag);
        var literal = new MenuItem("Literal").IsCheckable().IsChecked();

        Assert.IsTrue(first.IsCheckable);
        Assert.IsTrue(first.IsChecked);
        Assert.IsFalse(second.IsChecked);
        Assert.IsTrue(literal.IsChecked);

        chosen.Value = 2;
        flag.Value = true;

        Assert.IsFalse(first.IsChecked, "the item kept its check after the chosen value moved on");
        Assert.IsTrue(second.IsChecked);
        Assert.IsTrue(toggle.IsChecked);
    }

    private static (Window Window, ContextMenu Menu, MenuItem Item) ShowMenu(bool checkable, bool isChecked)
    {
        var owner = new Border { Width = 50, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = HeadlessWindow.Create(SIZE, SIZE);
        window.Content = owner;
        window.PerformLayout();

        var menu = new ContextMenu();
        MenuItem? target = null;
        for (int index = 0; index < ROW_COUNT; index++)
        {
            var item = new MenuItem($"Menu row {index}");
            if (index == CHECK_ROW)
            {
                item.IsCheckable = checkable;
                item.IsChecked = isChecked;
                target = item;
            }

            menu.AddEntry(item);
        }

        menu.Show(owner, new Point(40, 40));
        window.PerformLayout();
        return (window, menu, target!);
    }

    private static byte[] Render(TestBackendSession session, (Window Window, ContextMenu Menu, MenuItem Item) shown, out Rect checkColumn)
    {
        using var surface = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SIZE, SIZE, 1.0, hasAlpha: false));
        shown.Window.PerformLayout();
        shown.Window.RenderFrameToSurface(surface);
        var pixels = new byte[SIZE * SIZE * 4];
        Assert.IsTrue(session.Factory.TryReadPixels(surface, pixels, SIZE * 4), "the surface could not be read back");

        var method = typeof(ContextMenu).GetMethod("TryGetEntryRowBounds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object?[] args = [CHECK_ROW, null];
        Assert.IsTrue((bool)method.Invoke(shown.Menu, args)!, "the checkable row is not in the menu");
        var row = (Rect)args[1]!;
        double left = row.X + shown.Menu.ItemPadding.Left;
        checkColumn = new Rect(left, row.Y, ThemeMetrics.Default.CommandIconSize, row.Height);
        return pixels;
    }
}
