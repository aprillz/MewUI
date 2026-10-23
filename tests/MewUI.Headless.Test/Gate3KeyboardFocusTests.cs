using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

namespace MewUI.Headless.Test;

/// <summary>Gate 3: pressing Tab moves the keyboard focus to the next control.</summary>
[TestClass]
public sealed class Gate3KeyboardFocusTests
{
    [TestMethod]
    public void KeyPress_Tab_MovesFocusToNextControl()
    {
        var first = new TextBox();
        var second = new TextBox();
        var panel = new StackPanel().Children(first, second);
        var window = HeadlessTestHost.ShowWindow(panel);
        try
        {
            HeadlessTestHost.Shared.Run(() => Assert.IsTrue(first.Focus()));
            window.SendKeyPress(Key.Tab);

            Assert.AreSame(second, HeadlessTestHost.Shared.Run(() => window.FocusManager.FocusedElement));
            Assert.IsTrue(HeadlessTestHost.Shared.Run(() => second.IsFocused));
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }

    [TestMethod]
    public void KeyPress_ShiftTab_MovesFocusBack()
    {
        var first = new TextBox();
        var second = new TextBox();
        var panel = new StackPanel().Children(first, second);
        var window = HeadlessTestHost.ShowWindow(panel);
        try
        {
            HeadlessTestHost.Shared.Run(() => Assert.IsTrue(second.Focus()));
            window.SendKeyPress(Key.Tab, ModifierKeys.Shift);

            Assert.AreSame(first, HeadlessTestHost.Shared.Run(() => window.FocusManager.FocusedElement));
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }
}
