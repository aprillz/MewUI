using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Contract coverage for the tooltip a command source builds from its command: which parts
/// <see cref="CommandSourceControl.CommandToolTipMode"/> collects, and what it does when a part it was asked
/// for has nothing behind it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CommandToolTipTests
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

    private static (Window window, Button button) Host(Command command, CommandToolTipMode mode)
    {
        var window = HeadlessWindow.Create();
        var button = new Button { Command = command, CommandToolTipMode = mode };
        window.Content = button;
        window.PerformLayout();
        return (window, button);
    }

    private static string? TextOf(Control control)
        => (control.ResolveToolTipContentInternal() as TextBlock)?.Text;

    private static Command Save() => new("file.save", "Save") { Presentation = { Description = "Writes the file." } };

    [TestMethod]
    public void TextAndShortcut_ShareOneLineWithTheShortcutInBrackets()
    {
        if (SkipOnNonWindows()) return;

        var command = Save();
        var (window, button) = Host(command, CommandToolTipMode.Text | CommandToolTipMode.Shortcut);
        window.InputMap.Map(command, new KeyGesture(Key.S, ModifierKeys.Control));

        Assert.AreEqual("Save (Ctrl+S)", TextOf(button));
    }

    [TestMethod]
    public void Description_GoesOnItsOwnLineUnderTheName()
    {
        if (SkipOnNonWindows()) return;

        var (_, button) = Host(Save(), CommandToolTipMode.Text | CommandToolTipMode.Description);

        Assert.AreEqual("Save\nWrites the file.", TextOf(button));
    }

    [TestMethod]
    public void AskedForPartsWithNothingBehindThem_AreLeftOutRatherThanSubstituted()
    {
        if (SkipOnNonWindows()) return;

        // Everything is asked for, but the command answers to no gesture and carries no description.
        var (_, button) = Host(new Command("file.open", "Open"),
            CommandToolTipMode.Text | CommandToolTipMode.Shortcut | CommandToolTipMode.Description);

        Assert.AreEqual("Open", TextOf(button), "a part with no material behind it was filled from another");
    }

    [TestMethod]
    public void NothingToCollect_ShowsNoToolTipAtAll()
    {
        if (SkipOnNonWindows()) return;

        // Description is the only part asked for and the command has none: the name does not stand in.
        var (_, button) = Host(new Command("file.open", "Open"), CommandToolTipMode.Description);

        Assert.IsNull(button.ResolveToolTipContentInternal(), "an empty tooltip was built instead of none");
    }

    [TestMethod]
    public void NoneMode_BuildsNothingEvenWhenTheCommandCanSayPlenty()
    {
        if (SkipOnNonWindows()) return;

        var (_, button) = Host(Save(), CommandToolTipMode.None);

        Assert.IsNull(button.ResolveToolTipContentInternal());
    }

    [TestMethod]
    public void AToolTipOfItsOwn_WinsOverEveryMode()
    {
        if (SkipOnNonWindows()) return;

        var (_, button) = Host(Save(), CommandToolTipMode.Text | CommandToolTipMode.Description);
        button.ToolTip = new TextBlock { Text = "Mine" };

        Assert.AreEqual("Mine", TextOf(button), "the command's parts were mixed into an explicit tooltip");
    }

    [TestMethod]
    public void AToolBarEntry_TakesTheToolBarsModeAndItsOwnContent()
    {
        if (SkipOnNonWindows()) return;

        var plain = new ToolBarItem(Save());
        var spoken = new ToolBarItem(Save()).ToolTip("Mine");
        var window = HeadlessWindow.Create();
        var bar = new ToolBar { ItemToolTipMode = CommandToolTipMode.Text }
            .Band(new ToolBarGroup(plain, spoken));
        window.Content = bar;
        window.PerformLayout();

        var entries = bar.VisualsInternal[0].Groups[0].Entries;

        Assert.AreEqual("Save", TextOf((Control)entries[0]), "the entry did not take the toolbar's mode");
        Assert.AreEqual("Mine", TextOf((Control)entries[1]), "the entry's own tooltip lost to the mode");
    }
}
