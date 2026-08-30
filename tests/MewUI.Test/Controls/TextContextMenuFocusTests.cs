using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Closing a text context menu must return focus to the owning text box, for both the
/// escape-close and item-invoke close paths.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextContextMenuFocusTests
{
    [TestMethod]
    public void EscapeClose_RestoresFocusToTextBox()
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

        textBox.Text = "hello clipboard";
        textBox.Focus();
        textBox.SelectAll();

        var center = new Point(
            textBox.Bounds.X + textBox.Bounds.Width / 2,
            textBox.Bounds.Y + 10);
        window.SendMouseDown(center, MouseButton.Right);
        window.SendMouseUp(center, MouseButton.Right);
        window.PerformLayout();

        var menuField = typeof(TextBase).GetField("_defaultContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var menu = (ContextMenu?)menuField.GetValue(textBox);
        Assert.IsNotNull(menu, "default context menu opened");
        Assert.AreSame(menu, window.FocusManager.FocusedElement, "menu takes focus while open");

        window.SendKeyDown(Key.Escape);
        window.PerformLayout();

        Assert.IsNull(menu.FindVisualRoot() as Window, "menu closed by escape");
        Assert.AreSame(textBox, window.FocusManager.FocusedElement,
            $"escape close restores focus; actual={window.FocusManager.FocusedElement?.GetType().Name ?? "null"}");
    }

    [TestMethod]
    public void ClickingCopyItem_RestoresFocusToTextBox()
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

        textBox.Text = "hello clipboard";
        textBox.Focus();
        textBox.SelectAll();
        Assert.IsTrue(textBox.IsFocused, "text box focused before menu");

        var center = new Point(
            textBox.Bounds.X + textBox.Bounds.Width / 2,
            textBox.Bounds.Y + 10);
        window.SendMouseDown(center, MouseButton.Right);
        window.SendMouseUp(center, MouseButton.Right);
        window.PerformLayout();

        var menuField = typeof(TextBase).GetField("_defaultContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var menu = (ContextMenu?)menuField.GetValue(textBox);
        Assert.IsNotNull(menu, "default context menu opened");
        Assert.AreSame(menu, window.FocusManager.FocusedElement, "menu takes focus while open");

        int copyIndex = -1;
        for (int i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is MenuItem item && item.Command == StandardCommands.Copy)
            {
                copyIndex = i;
                break;
            }
        }
        Assert.IsTrue(copyIndex >= 0, "menu has a Copy item");

        var rowMethod = typeof(ContextMenu).GetMethod("TryGetEntryRowBounds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object?[] args = [copyIndex, null];
        Assert.IsTrue((bool)rowMethod.Invoke(menu, args)!, "copy row bounds resolve");
        var row = (Rect)args[1]!;
        var rowCenterInWindow = new Point(
            row.X + row.Width / 2,
            row.Y + row.Height / 2);

        window.SendMouseDown(rowCenterInWindow, MouseButton.Left);
        Assert.AreSame(menu, window.FocusManager.FocusedElement,
            $"menu keeps focus through mouse down; actual={window.FocusManager.FocusedElement?.GetType().Name ?? "null"}");
        window.SendMouseUp(rowCenterInWindow, MouseButton.Left);
        window.PerformLayout();

        Assert.IsNull(menu.FindVisualRoot() as Window, "menu closed after invoking the item");
        Assert.AreSame(textBox, window.FocusManager.FocusedElement,
            $"focus returns to the owning text box after executing a menu item; actual={window.FocusManager.FocusedElement?.GetType().Name ?? "null"}");
    }
}
