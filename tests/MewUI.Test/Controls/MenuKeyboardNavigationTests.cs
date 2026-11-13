using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Arrow key navigation in context menus and the menu bar through the production input router, on the
/// in-surface popup host.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MenuKeyboardNavigationTests
{
    [TestMethod]
    public void ArrowKeys_SkipSeparatorsAndDisabledItems_AndWrap()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var owner = CreateOwner(window);

        var menu = new ContextMenu();
        menu.AddItem("A");
        menu.AddSeparator();
        menu.AddItem("B", isEnabled: false);
        menu.AddItem("C");
        menu.AddItem("D");
        menu.Show(owner, new Point(100, 100));
        window.PerformLayout();

        Assert.AreEqual(-1, HotIndex(menu), "nothing is highlighted before a key");

        window.SendKeyDown(Key.Down);
        Assert.AreEqual(0, HotIndex(menu), "down highlights the first item");
        window.SendKeyDown(Key.Down);
        Assert.AreEqual(3, HotIndex(menu), "down skips the separator and the disabled item");
        window.SendKeyDown(Key.Down);
        window.SendKeyDown(Key.Down);
        Assert.AreEqual(0, HotIndex(menu), "down past the last item wraps to the first");
        window.SendKeyDown(Key.Up);
        Assert.AreEqual(4, HotIndex(menu), "up past the first item wraps to the last");
        window.SendKeyDown(Key.Home);
        Assert.AreEqual(0, HotIndex(menu), "home highlights the first item");
        window.SendKeyDown(Key.End);
        Assert.AreEqual(4, HotIndex(menu), "end highlights the last item");
    }

    [TestMethod]
    public void TabMovesLikeTheArrows_AndEnterWithNothingHighlightedTakesTheFirstItem()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var owner = CreateOwner(window);

        var menu = new ContextMenu();
        menu.AddItem("A");
        menu.AddItem("B", isEnabled: false);
        menu.AddItem("C");
        menu.Show(owner, new Point(100, 100));
        window.PerformLayout();

        window.SendKeyDown(Key.Enter);
        Assert.AreEqual(0, HotIndex(menu), "enter with nothing highlighted highlights the first item");
        Assert.AreEqual(1, PopupCount(window), "that enter does not close the menu");

        window.SendKeyDown(Key.Tab);
        Assert.AreEqual(2, HotIndex(menu), "tab moves down past the disabled item");
        window.SendKeyDown(Key.Tab, ModifierKeys.Shift);
        Assert.AreEqual(0, HotIndex(menu), "shift+tab moves up");
        Assert.AreSame(menu, window.FocusManager.FocusedElement, "tab does not move the focus out of the menu");
        Assert.AreEqual(1, PopupCount(window), "the menu stays open");
    }

    [TestMethod]
    public void SharedAccessKey_MovesBetweenItsItems_AndAUniqueOneActivates()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previous = PlatformConventions.Current;
        try
        {
            PlatformConventions.Current = new PlatformConventions();

            var window = HeadlessWindow.Create();
            var owner = CreateOwner(window);

            var menu = new ContextMenu();
            menu.AddItem("_Alpha");
            menu.AddItem("_Apple");
            menu.AddSubMenu("_Beta", new Menu().Item("Inside"));
            menu.Show(owner, new Point(100, 100));
            window.PerformLayout();

            window.SendKeyDown(Key.A);
            Assert.AreEqual(0, HotIndex(menu), "a shared access key highlights its first item");
            window.SendKeyDown(Key.A);
            Assert.AreEqual(1, HotIndex(menu), "pressed again it moves to the next item that shares it");
            window.SendKeyDown(Key.A);
            Assert.AreEqual(0, HotIndex(menu), "and comes back around");
            Assert.AreEqual(1, PopupCount(window), "a shared access key activates nothing");

            window.SendKeyDown(Key.B);
            window.PerformLayout();
            Assert.AreEqual(2, PopupCount(window), "an access key of one item activates it");
        }
        finally
        {
            PlatformConventions.Current = previous;
        }
    }

    [TestMethod]
    public void WithoutAccessKeys_LettersSelectByTitle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previous = PlatformConventions.Current;
        try
        {
            PlatformConventions.Current = new NoAccessKeyConventions();

            var window = HeadlessWindow.Create();
            var owner = CreateOwner(window);

            var menu = new ContextMenu();
            menu.AddItem("Copy");
            menu.AddItem("Cut");
            menu.AddSubMenu("_Paste", new Menu().Item("Inside"));
            menu.Show(owner, new Point(100, 100));
            window.PerformLayout();

            window.SendKeyDown(Key.C);
            Assert.AreEqual(0, HotIndex(menu), "a letter highlights the first title starting with it");
            window.SendKeyDown(Key.U);
            Assert.AreEqual(1, HotIndex(menu), "the next letter narrows to the title starting with both");
            window.SendKeyDown(Key.P);
            Assert.AreEqual(1, HotIndex(menu), "letters that start no title leave the highlight");

            Assert.AreEqual(1, PopupCount(window), "letters only highlight, they open nothing");
        }
        finally
        {
            PlatformConventions.Current = previous;
        }
    }

    [TestMethod]
    public void RightEntersSubMenu_LeftAndEscapeBackOutOneLevel()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var owner = CreateOwner(window);

        var menu = new ContextMenu();
        menu.AddSubMenu("Parent", new Menu().Item("Sub A").Item("Sub B"));
        menu.AddItem("Other");
        menu.Show(owner, new Point(100, 100));
        window.PerformLayout();

        window.SendKeyDown(Key.Down);
        window.SendKeyDown(Key.Right);
        window.PerformLayout();

        Assert.AreEqual(2, PopupCount(window), "right opens the submenu of the highlighted row");
        var subMenu = (ContextMenu)PopupElementAt(window, 1);
        Assert.AreSame(subMenu, window.FocusManager.FocusedElement, "the submenu takes the keys");
        Assert.AreEqual(0, HotIndex(subMenu), "the submenu opens with its first item highlighted");

        window.SendKeyDown(Key.Down);
        Assert.AreEqual(1, HotIndex(subMenu), "down moves inside the submenu");
        Assert.AreEqual(0, HotIndex(menu), "the parent keeps its row");

        window.SendKeyDown(Key.Left);
        window.PerformLayout();
        Assert.AreEqual(1, PopupCount(window), "left closes the submenu");
        Assert.AreSame(menu, window.FocusManager.FocusedElement, "the parent takes the keys back");
        Assert.AreEqual(0, HotIndex(menu), "the parent row stays highlighted");

        window.SendKeyDown(Key.Enter);
        window.PerformLayout();
        Assert.AreEqual(2, PopupCount(window), "enter on a submenu row opens it");

        window.SendKeyDown(Key.Escape);
        window.PerformLayout();
        Assert.AreEqual(1, PopupCount(window), "escape closes only the submenu");
        Assert.AreSame(menu, window.FocusManager.FocusedElement, "escape hands the keys back to the parent");

        window.SendKeyDown(Key.Escape);
        window.PerformLayout();
        Assert.AreEqual(0, PopupCount(window), "a second escape closes the menu");
    }

    [TestMethod]
    public void EnterInvokesTheHighlightedItem_AndReturnsFocusToTheTextBox()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var textBox = new MultiLineTextBox();
        window.Content = textBox;
        window.PerformLayout();

        textBox.Text = "hello keyboard";
        textBox.Focus();

        var point = new Point(textBox.Bounds.X + textBox.Bounds.Width / 2, textBox.Bounds.Y + 10);
        window.SendMouseDown(point, MouseButton.Right);
        window.SendMouseUp(point, MouseButton.Right);
        window.PerformLayout();

        var menu = (ContextMenu?)typeof(TextBase).GetField("_defaultContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(textBox);
        Assert.IsNotNull(menu, "default context menu opened");
        var selectAll = menu.Items[^1] as MenuItem;
        Assert.AreSame(StandardCommands.SelectAll, selectAll?.Command, "Select All is the last item");

        // End lands on the last enabled item whatever the clipboard holds for Paste.
        window.SendKeyDown(Key.End);
        window.SendKeyDown(Key.Enter);
        window.PerformLayout();

        Assert.AreEqual(0, PopupCount(window), "enter closes the menu");
        Assert.AreSame(textBox, window.FocusManager.FocusedElement, "the focus returns to the text box");
        Assert.AreEqual(textBox.Text.Length, textBox.SelectionLength, "Select All ran");
    }

    [TestMethod]
    public void MenuBar_ArrowKeysMoveBetweenDropDowns_AndEscapeReturnsFocus()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var menuBar = new MenuBar()
            .Item("File", new Menu().Item("New").Item("Open"))
            .Item("Edit", new Menu().Item("Undo").Item("Redo"));
        var textBox = new MultiLineTextBox { Height = 100 };
        window.Content = new StackPanel().Children(menuBar, textBox);
        window.PerformLayout();
        textBox.Focus();

        var itemBounds = (List<Rect>)typeof(MenuBar).GetField("_itemBounds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menuBar)!;
        window.SendClick(CenterOf(itemBounds[0]));
        window.PerformLayout();

        var file = OpenDropDown(menuBar);
        Assert.IsNotNull(file, "clicking File opens its drop-down");
        Assert.AreSame(file, window.FocusManager.FocusedElement, "the drop-down takes the keys");
        Assert.AreEqual(-1, HotIndex(file), "a drop-down opened by the pointer starts with nothing highlighted");

        window.SendKeyDown(Key.Right);
        window.PerformLayout();
        var edit = OpenDropDown(menuBar);
        Assert.IsNotNull(edit, "right opens the next drop-down");
        Assert.AreNotSame(file, edit, "the Edit drop-down replaced the File one");
        Assert.AreEqual(1, PopupCount(window), "only one drop-down is open");
        Assert.AreEqual(0, HotIndex(edit), "a drop-down opened by a key starts on its first item");

        window.SendKeyDown(Key.Right);
        window.PerformLayout();
        Assert.AreEqual(0, OpenIndex(menuBar), "right past the last item wraps to the first");

        window.SendKeyDown(Key.Escape);
        window.PerformLayout();
        Assert.AreEqual(0, PopupCount(window), "escape closes the drop-down");
        Assert.AreSame(menuBar, window.FocusManager.FocusedElement, "the bar takes the keys");
        Assert.AreEqual(0, MenuBarHotIndex(menuBar), "the bar keeps the item highlighted");

        window.SendKeyDown(Key.Right);
        Assert.AreEqual(1, MenuBarHotIndex(menuBar), "right moves along the bar");
        window.SendKeyDown(Key.Up);
        window.PerformLayout();
        var reopened = OpenDropDown(menuBar);
        Assert.IsNotNull(reopened, "up opens the highlighted item");
        Assert.AreEqual(1, HotIndex(reopened), "up opens on the last item");

        window.SendKeyDown(Key.Enter);
        window.PerformLayout();
        Assert.AreEqual(0, PopupCount(window), "enter on an item closes the drop-down");
        Assert.AreSame(textBox, window.FocusManager.FocusedElement, "the focus returns to the text box");

        window.SendKeyDown(Key.Down);
        Assert.IsNull(OpenDropDown(menuBar), "keys go to the text box after the menu closed");
    }

    [TestMethod]
    public void MenuBar_SecondEscapeReturnsFocus()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var menuBar = new MenuBar().Item("File", new Menu().Item("New"));
        var textBox = new MultiLineTextBox { Height = 100 };
        window.Content = new StackPanel().Children(menuBar, textBox);
        window.PerformLayout();
        textBox.Focus();

        var itemBounds = (List<Rect>)typeof(MenuBar).GetField("_itemBounds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menuBar)!;
        window.SendClick(CenterOf(itemBounds[0]));
        window.PerformLayout();

        window.SendKeyDown(Key.Escape);
        window.PerformLayout();
        Assert.AreSame(menuBar, window.FocusManager.FocusedElement, "the first escape leaves the keys on the bar");

        window.SendKeyDown(Key.Escape);
        Assert.AreSame(textBox, window.FocusManager.FocusedElement, "the second escape gives the focus back");
    }

    [TestMethod]
    public void MenuBar_CommandFromAClickOpenedDropDown_ActsOnTheContentThatHadTheFocus()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var menuBar = new MenuBar().Item("Edit", new Menu().Item(StandardCommands.SelectAll));
        var textBox = new MultiLineTextBox { Height = 100 };
        window.Content = new StackPanel().Children(menuBar, textBox);
        window.PerformLayout();
        textBox.Text = "hello keyboard";
        textBox.Focus();

        var itemBounds = (List<Rect>)typeof(MenuBar).GetField("_itemBounds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menuBar)!;
        window.SendClick(CenterOf(itemBounds[0]));
        window.PerformLayout();

        window.SendKeyDown(Key.Down);
        window.SendKeyDown(Key.Enter);
        window.PerformLayout();

        Assert.AreEqual(textBox.Text.Length, textBox.SelectionLength, "Select All ran on the text box, not on the bar the click focused");
        Assert.AreSame(textBox, window.FocusManager.FocusedElement, "the focus returns to the text box");
    }

    private sealed class NoAccessKeyConventions : PlatformConventions
    {
        public override bool SupportsAccessKeys => false;
    }

    private static Border CreateOwner(Window window)
    {
        var owner = new Border
        {
            Width = 50,
            Height = 50,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = owner;
        window.PerformLayout();
        return owner;
    }

    // Item cells are laid out in the same space as Bounds, which is the window's while nothing transforms.
    private static Point CenterOf(Rect cell) => new(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);

    private static int HotIndex(ContextMenu menu)
        => (int)typeof(ContextMenu).GetField("_hotIndex", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menu)!;

    private static int MenuBarHotIndex(MenuBar menuBar)
        => (int)typeof(MenuBar).GetField("_hotIndex", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menuBar)!;

    private static int OpenIndex(MenuBar menuBar)
        => (int)typeof(MenuBar).GetField("_openIndex", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menuBar)!;

    private static ContextMenu? OpenDropDown(MenuBar menuBar)
        => (ContextMenu?)typeof(MenuBar).GetField("_openPopup", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menuBar);

    private static int PopupCount(Window window)
    {
        var popupManager = typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return (int)popupManager.GetType().GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(popupManager)!;
    }

    private static UIElement PopupElementAt(Window window, int index)
    {
        var popupManager = typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var method = popupManager.GetType().GetMethod("ElementAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (UIElement)method.Invoke(popupManager, [index])!;
    }
}
