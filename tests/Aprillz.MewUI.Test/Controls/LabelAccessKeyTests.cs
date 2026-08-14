using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A label carries the access key for the field beside it, which is what <see cref="Label.Target"/> is
/// for: the label has nothing to activate itself, so the key has to land on the control it names.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LabelAccessKeyTests
{
    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("Access keys are not used on macOS, and the GDI backend is Windows-only.");
        return true;
    }

    [TestMethod]
    public void ALabelsAccessKeyFocusesTheFieldItNames()
    {
        if (SkipOnNonWindows()) return;

        var box = new TextBox { Width = 160 };
        var label = new Label { Text = "_Name:" }.AccessKeyTarget(box);

        var window = HeadlessWindow.Create();
        window.Content = new StackPanel().Horizontal().Spacing(8).Children(label, box);
        window.PerformLayout();

        // Alt reveals the keys, then Alt+N lands on the label and is handed to its target.
        window.ProcessAccessKeyDown(new KeyEventArgs(Key.None, 0, ModifierKeys.Alt));
        window.ProcessAccessKeyDown(new KeyEventArgs(Key.N, 0, ModifierKeys.Alt));

        Assert.IsTrue(box.IsFocused, "the field the label names did not take focus");

        window.Close();
    }

    [TestMethod]
    public void AnyFocusableTargetTakesTheKey()
    {
        if (SkipOnNonWindows()) return;

        var list = new ListBox().Items("One", "Two");
        var slider = new Slider();
        var tree = new TreeView();

        var window = HeadlessWindow.Create();
        window.Content = new StackPanel()
            .Vertical()
            .Children(
                new Label { Text = "_List:" }.AccessKeyTarget(list), list,
                new Label { Text = "_Volume:" }.AccessKeyTarget(slider), slider,
                new Label { Text = "_Tree:" }.AccessKeyTarget(tree), tree);
        window.PerformLayout();

        // None of these run anything on a key, so focus is what the key can do; without it the key
        // bubbles past the control and is lost.
        foreach (var (key, target) in new (Key Key, Control Target)[]
        {
            (Key.L, list),
            (Key.V, slider),
            (Key.T, tree),
        })
        {
            window.ProcessAccessKeyDown(new KeyEventArgs(Key.None, 0, ModifierKeys.Alt));
            window.ProcessAccessKeyDown(new KeyEventArgs(key, 0, ModifierKeys.Alt));

            Assert.IsTrue(target.IsFocused, $"{target.GetType().Name} ignored the key its label carries");
        }

        window.Close();
    }

    [TestMethod]
    public void TheKeyGoesToTheNamedFieldRatherThanTheOneBeforeIt()
    {
        if (SkipOnNonWindows()) return;

        var first = new TextBox { Width = 160 };
        var second = new TextBox { Width = 160 };
        var label = new Label { Text = "_Team:" }.AccessKeyTarget(second);

        var window = HeadlessWindow.Create();
        window.Content = new StackPanel()
            .Vertical()
            .Children(first, new StackPanel().Horizontal().Children(label, second));
        window.PerformLayout();

        window.ProcessAccessKeyDown(new KeyEventArgs(Key.None, 0, ModifierKeys.Alt));
        window.ProcessAccessKeyDown(new KeyEventArgs(Key.T, 0, ModifierKeys.Alt));

        Assert.IsTrue(second.IsFocused, "the key did not reach the field the label points at");
        Assert.IsFalse(first.IsFocused, "the key focused a field the label does not name");

        window.Close();
    }
}
