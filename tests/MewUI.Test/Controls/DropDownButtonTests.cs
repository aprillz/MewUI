using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class DropDownButtonTests
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

    private static Menu TwoItemMenu(out Command first)
    {
        first = new Command("test.first", "First");
        return new Menu().Item(first).Item(new Command("test.second", "Second"));
    }

    private static (Window window, DropDownButton button) Host(Menu? menu)
    {
        var window = HeadlessWindow.Create();
        var button = new DropDownButton
        {
            Content = new TextBlock { Text = "actions" },
            DropDownMenu = menu,
        };
        window.Content = button;
        window.PerformLayout();
        return (window, button);
    }

    [TestMethod]
    public void PublicSurface_HasNoPrimaryCommand()
    {
        var type = typeof(DropDownButton);

        Assert.IsNull(type.GetProperty("Command"), "a drop-down button carries no primary command");
        Assert.IsNull(type.GetProperty("CommandPresentationMode"));
        Assert.IsNull(type.GetEvent("Click"));
    }

    [TestMethod]
    public void DefaultTemplate_ProjectsContentThroughFacePart()
    {
        if (SkipOnNonWindows()) return;

        var content = new TextBlock { Text = "actions" };
        var window = HeadlessWindow.Create();
        var button = new DropDownButton { Content = content };
        window.Content = button;
        window.PerformLayout();

        Assert.IsInstanceOfType<ContentPresenter>(content.Parent, "the default template projects the content");
        Assert.AreSame(button, content.LogicalParent);
    }

    [TestMethod]
    public void PartialNamedStyle_PreservesRuntimeDefaultTemplate()
    {
        if (SkipOnNonWindows()) return;

        var sheet = new StyleSheet();
        sheet.Define("partial-dropdown", () => new Style(typeof(DropDownButton))
        {
            Setters = [Setter.Create(Control.BackgroundProperty, Color.FromRgb(10, 20, 30))],
        });
        var window = HeadlessWindow.Create();
        window.StyleSheet = sheet;
        var button = new DropDownButton
        {
            StyleName = "partial-dropdown",
            Content = new TextBlock { Text = "actions" },
        };

        window.Content = button;
        window.PerformLayout();

        Assert.IsTrue(button.HasTemplateInstance,
            "a partial named style is layered over the DropDownButton default Template");
        Assert.AreEqual(new Thickness(8, 4, 8, 4), button.Padding);
    }

    [TestMethod]
    public void OverridesDefaultStyle_RemovesRuntimeDefaultTemplate()
    {
        if (SkipOnNonWindows()) return;

        var sheet = new StyleSheet();
        sheet.Define("replacement-dropdown", () => new Style(typeof(DropDownButton))
        {
            OverridesDefaultStyle = true,
            Setters = [Setter.Create(Control.BackgroundProperty, Color.FromRgb(10, 20, 30))],
        });
        var window = HeadlessWindow.Create();
        window.StyleSheet = sheet;
        var button = new DropDownButton
        {
            StyleName = "replacement-dropdown",
            Content = new TextBlock { Text = "actions" },
        };

        window.Content = button;
        window.PerformLayout();

        Assert.IsFalse(button.HasTemplateInstance);
        Assert.IsNull(button.Template,
            "a full replacement style is responsible for supplying its own Template");
        Assert.AreEqual(default, button.Padding);
    }

    [TestMethod]
    public void OpenMenu_KeepsAccentChromeWhileFocusMovesToMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host(TwoItemMenu(out _));
        window.SetIsActive(true);
        window.FocusManager.SetFocus(button);
        button.ForceStyleSnap();
        window.UpdateVisualStates();

        Assert.IsTrue(button.IsFocused, "the owner is the focus unit");
        Assert.AreEqual(button.ThemeInternal.Palette.Accent, button.BorderBrush,
            "a focused drop-down button rings accent");

        button.IsDropDownOpen = true;
        window.UpdateVisualStates();

        Assert.IsFalse(button.IsFocused, "focus moved into the menu");
        Assert.AreEqual(button.ThemeInternal.Palette.Accent, button.BorderBrush,
            "the open state keeps the ring, the way a ComboBox does");
    }

    [TestMethod]
    public void Click_OpensAndReopensMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host(TwoItemMenu(out _));

        window.SendClick(button.CenterOf());
        Assert.IsTrue(button.IsDropDownOpen, "clicking the face opens the menu");

        window.SendClick(button.CenterOf());
        Assert.IsFalse(button.IsDropDownOpen, "clicking again closes it through the popup policy");

        window.SendClick(button.CenterOf());
        Assert.IsTrue(button.IsDropDownOpen, "the menu reopens after a close");
    }

    [TestMethod]
    public void KeyboardAndAccessKey_OpenMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host(TwoItemMenu(out _));
        window.FocusManager.SetFocus(button);

        window.SendKeyDown(Key.Space);
        Assert.IsTrue(button.IsDropDownOpen, "Space opens the menu");
        button.IsDropDownOpen = false;

        window.FocusManager.SetFocus(button);
        window.SendKeyDown(Key.F4);
        Assert.IsTrue(button.IsDropDownOpen, "F4 opens the menu");
        button.IsDropDownOpen = false;

        button.OnAccessKey();
        Assert.IsTrue(button.IsDropDownOpen, "the access key opens the menu");
    }

    [TestMethod]
    public void EmptyOrMissingMenu_KeepsStateClosed()
    {
        if (SkipOnNonWindows()) return;

        var (_, button) = Host(menu: null);

        button.IsDropDownOpen = true;
        Assert.IsFalse(button.IsDropDownOpen, "a missing menu leaves the state closed");

        button.DropDownMenu = new Menu();
        button.IsDropDownOpen = true;
        Assert.IsFalse(button.IsDropDownOpen, "an empty menu leaves the state closed");
    }

    [TestMethod]
    public void WithoutVisualRoot_KeepsStateClosed()
    {
        if (SkipOnNonWindows()) return;

        var button = new DropDownButton { DropDownMenu = TwoItemMenu(out _) };

        button.IsDropDownOpen = true;

        Assert.IsFalse(button.IsDropDownOpen, "there is no window to host the popup");
    }

    [TestMethod]
    public void DropDownOpening_CanRebuildAndReplaceMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host(menu: null);
        int openings = 0;
        button.DropDownOpening += () =>
        {
            openings++;
            button.DropDownMenu = new Menu().Item(new Command($"test.late{openings}", "Late"));
        };

        button.IsDropDownOpen = true;

        Assert.AreEqual(1, openings);
        Assert.IsTrue(button.IsDropDownOpen, "the menu supplied by the handler is used for this open");
    }

    [TestMethod]
    public void CloseRaisesDropDownClosedOnce()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host(TwoItemMenu(out _));
        int closed = 0;
        button.DropDownClosed += () => closed++;

        button.IsDropDownOpen = true;
        button.IsDropDownOpen = false;
        Assert.AreEqual(1, closed);

        button.IsDropDownOpen = false;
        Assert.AreEqual(1, closed, "closing an already closed menu raises nothing");
    }

    [TestMethod]
    public void MenuReplacement_ClosesOpenMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host(TwoItemMenu(out _));

        button.IsDropDownOpen = true;
        Assert.IsTrue(button.IsDropDownOpen);

        button.DropDownMenu = new Menu().Item(new Command("test.other", "Other"));

        Assert.IsFalse(button.IsDropDownOpen, "replacing the model closes the menu built from the old one");
    }

    [TestMethod]
    public void TemplateReplacement_WiresOnlyTheNewPart()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host(TwoItemMenu(out _));

        button.Template = new DelegateControlTemplate<DropDownButton>(static (owner, ctx) =>
        {
            var face = new Button { Focusable = false, IsTabStop = false, Content = new ContentPresenter() };
            ctx.Register(DropDownButton.PART_DROP_DOWN_BUTTON, face);
            return face;
        });
        window.PerformLayout();

        window.SendClick(button.CenterOf());

        Assert.IsTrue(button.IsDropDownOpen, "the replaced part opens the menu");
    }

    [TestMethod]
    public void MissingRequiredPart_Throws()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var button = new DropDownButton
        {
            Template = new DelegateControlTemplate<DropDownButton>(static (owner, ctx) => new Border()),
        };
        window.Content = button;

        Assert.ThrowsExactly<InvalidOperationException>(() => window.PerformLayout());
    }
}
