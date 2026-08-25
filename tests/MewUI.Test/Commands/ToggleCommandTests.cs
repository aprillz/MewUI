using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

[TestClass]
[DoNotParallelize]
public sealed class ToggleCommandTests
{
    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    private static (Window window, Command command, int[] executed) Host(Control control)
    {
        var window = HeadlessWindow.Create();
        window.Content = control;
        window.PerformLayout();

        var command = new Command("test.toggle");
        var executed = new int[1];
        control.Commands.Register(command, () => executed[0]++);
        return (window, command, executed);
    }

    [TestMethod]
    public void ToggleButton_UserActivation_ExecutesCommandOnce()
    {
        if (SkipOnNonWindows()) return;

        var toggle = new ToggleButton();
        var (window, command, executed) = Host(toggle);
        toggle.Command = command;
        window.PerformLayout();

        toggle.OnAccessKey();

        Assert.IsTrue(toggle.IsChecked, "the access key commits the toggle");
        Assert.AreEqual(1, executed[0], "one user activation runs the command once");
    }

    [TestMethod]
    public void ToggleButton_DirectStateAssignment_DoesNotExecuteCommand()
    {
        if (SkipOnNonWindows()) return;

        var toggle = new ToggleButton();
        var (window, command, executed) = Host(toggle);
        toggle.Command = command;
        window.PerformLayout();

        toggle.IsChecked = true;
        toggle.IsChecked = false;

        Assert.AreEqual(0, executed[0], "setting the state directly is not a user activation");
    }

    [TestMethod]
    public void CheckBox_UserActivation_ExecutesCommandOnce()
    {
        if (SkipOnNonWindows()) return;

        var checkBox = new CheckBox();
        var (window, command, executed) = Host(checkBox);
        checkBox.Command = command;
        window.PerformLayout();

        checkBox.Toggle();

        Assert.AreEqual(true, checkBox.IsChecked);
        Assert.AreEqual(1, executed[0]);
    }

    [TestMethod]
    public void CheckBox_DirectStateAssignment_DoesNotExecuteCommand()
    {
        if (SkipOnNonWindows()) return;

        var checkBox = new CheckBox();
        var (window, command, executed) = Host(checkBox);
        checkBox.Command = command;
        window.PerformLayout();

        checkBox.IsChecked = true;

        Assert.AreEqual(0, executed[0]);
    }

    [TestMethod]
    public void RadioButton_GroupAutoUncheck_DoesNotExecuteCommand()
    {
        if (SkipOnNonWindows()) return;

        var first = new RadioButton { GroupName = "g", IsChecked = true };
        var second = new RadioButton { GroupName = "g" };
        var panel = new StackPanel().Children(first, second);

        var window = HeadlessWindow.Create();
        window.Content = panel;
        window.PerformLayout();

        var command = new Command("test.radio");
        int firstExecuted = 0;
        first.Commands.Register(command, () => firstExecuted++);
        first.Command = command;
        window.PerformLayout();

        second.OnAccessKey();

        Assert.IsTrue(second.IsChecked);
        Assert.IsFalse(first.IsChecked, "the group unchecks the previous button");
        Assert.AreEqual(0, firstExecuted, "an automatic group uncheck is not a user activation");
    }

    [TestMethod]
    public void ToggleButton_CanExecuteFalse_DisablesControl()
    {
        if (SkipOnNonWindows()) return;

        var toggle = new ToggleButton();
        var window = HeadlessWindow.Create();
        window.Content = toggle;
        window.PerformLayout();

        var command = new Command("test.gated");
        bool canExecute = false;
        toggle.Commands.Register(command, () => { }, () => canExecute);
        toggle.Command = command;
        window.PerformLayout();

        Assert.IsFalse(toggle.IsEffectivelyEnabled, "CanExecute false disables the toggle");

        canExecute = true;
        window.EvaluateCommandStates();
        window.PerformLayout();

        Assert.IsTrue(toggle.IsEffectivelyEnabled, "the toggle follows the command state back");
    }
}
