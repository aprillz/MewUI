using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Contract coverage for the click predicate: how it combines with the local enabled value and with a
/// command's own answer, and when it is asked again.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CanClickTests
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

    [TestMethod]
    public void APredicateThatSaysNo_DisablesTheButtonWithoutTouchingIsEnabled()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var button = new Button().OnCanClick(() => false);
        window.Content = button;
        window.PerformLayout();

        Assert.IsFalse(button.IsEffectivelyEnabled, "the predicate did not reach the button");
        Assert.IsTrue(button.IsEnabled, "the predicate overwrote the value the application owns");
    }

    [TestMethod]
    public void TheLocalValueAndThePredicate_BothHaveToAgree()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        bool allowed = true;
        var button = new Button().OnCanClick(() => allowed);
        window.Content = button;
        window.PerformLayout();

        Assert.IsTrue(button.IsEffectivelyEnabled);

        button.IsEnabled = false;
        Assert.IsFalse(button.IsEffectivelyEnabled, "the local value stopped counting once a predicate was set");

        button.IsEnabled = true;
        allowed = false;
        window.RequerySuggested();
        Assert.IsFalse(button.IsEffectivelyEnabled, "a requery did not ask the predicate again");
    }

    [TestMethod]
    public void APredicateAndACommand_BothHaveToAgree()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var command = new Command("file.save", "Save");
        bool canExecute = true;

        var scope = new CommandScope();
        scope.Register(command, static () => { }, () => canExecute);
        window.CommandRouter.FallbackTarget = CommandTarget.From(scope);

        var button = new Button { Command = command }.OnCanClick(() => true);
        window.Content = button;
        window.PerformLayout();

        Assert.IsTrue(button.IsEffectivelyEnabled, "a button both sources allow came out disabled");

        canExecute = false;
        window.RequerySuggested();
        Assert.IsFalse(button.IsEffectivelyEnabled, "the command's answer stopped counting once a predicate was set");
    }

    [TestMethod]
    public void AMenuRow_AsksItsPredicateEachTimeTheMenuOpens()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var owner = new Border { Width = 50, Height = 50 };
        window.Content = owner;
        window.PerformLayout();

        bool allowed = false;
        var item = new MenuItem("Paste").OnCanClick(() => allowed);
        var menu = new ContextMenu();
        menu.Menu.Items.Add(item);

        menu.Show(owner, new Point(10, 10));
        window.PerformLayout();
        Assert.IsFalse(item.IsEffectivelyEnabled, "the row ignored a predicate that said no");

        menu.CloseTree(window);
        allowed = true;
        menu.Show(owner, new Point(10, 10));
        window.PerformLayout();
        Assert.IsTrue(item.IsEffectivelyEnabled, "opening the menu again did not ask the predicate");

        menu.CloseTree(window);
    }
}
