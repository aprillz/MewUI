using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class SplitButtonTests
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

    private static Menu OneItemMenu()
        => new Menu().Item(new Command("test.item", "Item"));

    private static (Window window, SplitButton button) Host()
    {
        var window = HeadlessWindow.Create();
        var button = new SplitButton
        {
            Content = new TextBlock { Text = "save" },
            DropDownMenu = OneItemMenu(),
            Width = 200,
            Height = 32,
        };
        window.Content = button;
        window.PerformLayout();
        return (window, button);
    }

    private static Button PrimaryPart(SplitButton button)
        => (Button)VisualTree.Find(button, e => e is Button b && b.Content is ContentPresenter)!;

    [TestMethod]
    public void PublicSurface_KeepsPrimaryCommandAndClick()
    {
        var type = typeof(SplitButton);

        Assert.IsNotNull(type.GetProperty("Command"), "a split button keeps the primary command");
        Assert.IsNotNull(type.GetProperty("CommandPresentationMode"));
        Assert.IsNotNull(type.GetEvent("Click"));
    }

    [TestMethod]
    public void PrimaryClick_RunsClickOnce_AndDoesNotOpenMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host();
        int clicks = 0;
        button.Click += () => clicks++;

        window.SendClick(PrimaryPart(button).CenterOf());

        Assert.AreEqual(1, clicks, "the primary face runs the click exactly once");
        Assert.IsFalse(button.IsDropDownOpen, "the primary face does not open the menu");
    }

    [TestMethod]
    public void DropDownClick_OpensMenuWithoutRunningClick()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host();
        int clicks = 0;
        button.Click += () => clicks++;

        var dropDownCenter = new Point(button.Bounds.Right - 12, button.Bounds.Y + button.Bounds.Height / 2);
        window.SendClick(dropDownCenter);

        Assert.IsTrue(button.IsDropDownOpen, "the drop-down face opens the menu");
        Assert.AreEqual(0, clicks, "the drop-down face does not run the primary action");
    }

    [TestMethod]
    public void OpenMenu_KeepsAccentChromeWhileFocusMovesToMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host();
        window.SetIsActive(true);
        window.FocusManager.SetFocus(button);
        button.ForceStyleSnap();
        window.UpdateVisualStates();

        Assert.AreEqual(button.ThemeInternal.Palette.Accent, button.BorderBrush,
            "a focused split button rings accent");

        button.IsDropDownOpen = true;
        window.UpdateVisualStates();

        Assert.IsFalse(button.IsFocused, "focus moved into the menu");
        Assert.AreEqual(button.ThemeInternal.Palette.Accent, button.BorderBrush,
            "the open state keeps the ring, the way a ComboBox does");
    }

    [TestMethod]
    public void HoveredFace_FillsAtOrdinaryButtonStrength()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host();
        window.SetIsActive(true);
        var primary = PrimaryPart(button);

        // Zero alpha rather than a specific constant: the idle fill carries the hover hue so the
        // transition only ramps alpha, and what matters is that it paints nothing.
        Assert.AreEqual(0, primary.Background.A,
            "an idle face lets the owner's chrome show through");

        window.SendMouseMove(primary.CenterOf());
        primary.ForceStyleSnap();
        window.UpdateVisualStates();

        Assert.AreEqual(button.ThemeInternal.Palette.ButtonHoverBackground, primary.Background,
            "a face hovers at the same strength as an ordinary button, not a dimmed flat one");
    }

    [TestMethod]
    public void CanExecuteFalse_DisablesPrimaryOnly()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host();
        var command = new Command("test.gated");
        bool canExecute = false;
        button.Commands.Register(command, () => { }, () => canExecute);
        button.Command = command;
        window.PerformLayout();

        Assert.IsTrue(button.IsEffectivelyEnabled, "the control itself stays enabled");
        Assert.IsFalse(PrimaryPart(button).IsEnabled, "only the primary face goes inactive");

        button.IsDropDownOpen = true;
        Assert.IsTrue(button.IsDropDownOpen, "the menu is still reachable");
    }

    [TestMethod]
    public void CanExecuteFalse_BlocksEveryPrimaryActivationPath()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host();
        int clicks = 0;
        int executed = 0;
        button.Click += () => clicks++;

        var command = new Command("test.gated");
        bool canExecute = false;
        button.Commands.Register(command, () => executed++, () => canExecute);
        button.Command = command;
        window.PerformLayout();

        window.FocusManager.SetFocus(button);
        window.SendKeyPress(Key.Space);
        window.SendKeyPress(Key.Enter);
        button.OnAccessKey();
        button.RaiseClick();
        window.SendClick(PrimaryPart(button).CenterOf());

        Assert.AreEqual(0, clicks, "no activation path raises Click while the command cannot execute");
        Assert.AreEqual(0, executed, "and none of them execute the command");
    }

    [TestMethod]
    public void ExplicitDisable_TurnsOffBothFaces()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host();
        button.IsEnabled = false;
        window.PerformLayout();

        Assert.IsFalse(button.IsEffectivelyEnabled);
        Assert.IsFalse(PrimaryPart(button).IsEffectivelyEnabled, "the primary face follows the owner");

        button.IsDropDownOpen = true;
        Assert.IsTrue(button.IsDropDownOpen, "programmatic opening is unaffected by the disabled visual");
    }

    [TestMethod]
    public void KeyboardActivation_RunsPrimary_AndAltDownOpensMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, button) = Host();
        int clicks = 0;
        button.Click += () => clicks++;
        window.FocusManager.SetFocus(button);

        window.SendKeyPress(Key.Space);
        Assert.AreEqual(1, clicks, "Space runs the primary action");
        Assert.IsFalse(button.IsDropDownOpen);

        window.SendKeyDown(Key.Down, ModifierKeys.Alt);
        Assert.IsTrue(button.IsDropDownOpen, "Alt+Down opens the menu");
        Assert.AreEqual(1, clicks, "opening the menu does not run the primary action");
    }

    [TestMethod]
    public void FluentChain_KeepsTheSplitButtonType()
    {
        if (SkipOnNonWindows()) return;

        var command = new Command("test.save", "Save");

        // Compiles only while every step returns SplitButton: a step inherited from Button that returned
        // Button would end the chain, since the drop-down extensions take a SplitButton.
        SplitButton button = new SplitButton()
            .Content("Save")
            .Command(command, CommandPresentationMode.Text)
            .OnClick(() => { })
            .DropDownMenu(OneItemMenu())
            .MaxDropDownHeight(240);

        Assert.AreEqual(240, button.MaxDropDownHeight);
        Assert.AreSame(command, button.Command);
    }

    [TestMethod]
    public void CommandPresentation_IsOptInAsOnButton()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var command = new Command("test.save", "Save");
        var button = new SplitButton { Command = command, DropDownMenu = OneItemMenu() };
        window.Content = button;
        window.PerformLayout();

        Assert.AreEqual(CommandPresentationMode.None, button.CommandPresentationMode);
        Assert.IsNull(button.EffectiveContent, "a command alone filled the content without a presentation mode");

        button.CommandPresentationMode = CommandPresentationMode.TextAndIcon;
        window.PerformLayout();

        Assert.IsNotNull(button.EffectiveContent, "the presentation mode did not supply the primary content");
    }

    [TestMethod]
    public void ButtonBasedNamedStyle_PreservesSplitDefaultTemplate()
    {
        if (SkipOnNonWindows()) return;

        var sheet = new StyleSheet();
        sheet.Define("flat-split", () => Style.DeriveFromDefault<Button>(
            setters: [Setter.Create(Control.BorderThicknessProperty, 0.0)]));
        var window = HeadlessWindow.Create();
        window.StyleSheet = sheet;
        var button = new SplitButton
        {
            StyleName = "flat-split",
            Content = new TextBlock { Text = "save" },
            DropDownMenu = OneItemMenu(),
        };

        window.Content = button;
        window.PerformLayout();

        Assert.IsTrue(button.HasTemplateInstance,
            "the actual SplitButton default stays below the Button-targeted named style");
        Assert.IsNotNull(PrimaryPart(button));
    }

    [TestMethod]
    public void MissingRequiredPart_Throws()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var button = new SplitButton
        {
            Template = new DelegateControlTemplate<SplitButton>(static (owner, ctx) => new Border()),
        };
        window.Content = button;

        Assert.ThrowsExactly<InvalidOperationException>(() => window.PerformLayout());
    }
}
