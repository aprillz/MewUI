using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

[TestClass]
[DoNotParallelize]
public sealed class InputMapDispatchTests
{
    [TestMethod]
    public void WindowMapGesture_ExecutesBoundCommand()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        window.PerformLayout();

        var command = new Command("test.refresh");
        int executed = 0;
        window.Commands.Register(command, () => executed++);
        window.InputMap.Map(command, new KeyGesture(Key.F5));

        window.SendKeyDown(Key.F5);

        Assert.AreEqual(1, executed);
    }

    [TestMethod]
    public void LocalMap_ShadowsWindowMap()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button();
        window.Content = editor;
        window.PerformLayout();
        window.FocusManager.SetFocus(editor);

        var localCommand = new Command("test.runSelection");
        var windowCommand = new Command("test.refresh");
        int localExecuted = 0;
        int windowExecuted = 0;
        editor.Commands.Register(localCommand, () => localExecuted++);
        window.Commands.Register(windowCommand, () => windowExecuted++);
        editor.InputMap.Map(localCommand, new KeyGesture(Key.F5));
        window.InputMap.Map(windowCommand, new KeyGesture(Key.F5));

        window.SendKeyDown(Key.F5);

        Assert.AreEqual(1, localExecuted);
        Assert.AreEqual(0, windowExecuted);
    }

    [TestMethod]
    public void DisabledLocalCommand_DoesNotFallBackToOuterGesture()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button();
        window.Content = editor;
        window.PerformLayout();
        window.FocusManager.SetFocus(editor);

        var localCommand = new Command("test.runSelection");
        var windowCommand = new Command("test.refresh");
        int localExecuted = 0;
        int windowExecuted = 0;
        editor.Commands.Register(localCommand, () => localExecuted++, () => false);
        window.Commands.Register(windowCommand, () => windowExecuted++);
        editor.InputMap.Map(localCommand, new KeyGesture(Key.F5));
        window.InputMap.Map(windowCommand, new KeyGesture(Key.F5));

        window.SendKeyDown(Key.F5);

        Assert.AreEqual(0, localExecuted, "disabled command does not run");
        Assert.AreEqual(0, windowExecuted, "gesture meaning is claimed by the nearest map; no fallback");
    }

    [TestMethod]
    public void CallbackBinding_ExecutesAndRespectsCanExecute()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        window.PerformLayout();

        int executed = 0;
        bool allowed = true;
        window.InputMap.Map(new KeyGesture(Key.F12), () => executed++, () => allowed);

        window.SendKeyDown(Key.F12);
        Assert.AreEqual(1, executed);

        allowed = false;
        window.SendKeyDown(Key.F12);
        Assert.AreEqual(1, executed);
    }

    [TestMethod]
    public void AlternativeGesture_ExecutesAndPrimaryIsFirst()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        window.PerformLayout();

        var command = new Command("test.find");
        int executed = 0;
        window.Commands.Register(command, () => executed++);
        window.InputMap.Map(command,
            new KeyGesture(Key.F, ModifierKeys.Control),
            new KeyGesture(Key.F3));

        window.SendKeyDown(Key.F3);
        Assert.AreEqual(1, executed);

        Assert.IsTrue(window.InputMap.TryGetPrimaryGesture(command, out var primary));
        Assert.AreEqual(new KeyGesture(Key.F, ModifierKeys.Control), primary);
    }

    [TestMethod]
    public void RuntimeRemap_ReplacesGesturesAndRaisesChanged()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        window.PerformLayout();

        var command = new Command("test.save");
        int executed = 0;
        int changedCount = 0;
        window.Commands.Register(command, () => executed++);
        window.InputMap.Changed += () => changedCount++;

        window.InputMap.Map(command, new KeyGesture(Key.S, ModifierKeys.Control));
        Assert.AreEqual(1, changedCount);

        window.InputMap.Map(command, new KeyGesture(Key.F2));
        Assert.AreEqual(2, changedCount);

        window.SendKeyDown(Key.S, ModifierKeys.Control);
        Assert.AreEqual(0, executed, "the old gesture no longer maps to the command");

        window.SendKeyDown(Key.F2);
        Assert.AreEqual(1, executed);
    }

    [TestMethod]
    public void FocusedControlKeyHandling_TakesPriorityOverInputMap()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var button = new Button();
        window.Content = button;
        window.PerformLayout();
        window.FocusManager.SetFocus(button);

        // Button consumes Space in OnKeyDown; a Space mapping must never observe the key.
        int executed = 0;
        window.InputMap.Map(new KeyGesture(Key.Space), () => executed++);

        window.SendKeyDown(Key.Space);

        Assert.AreEqual(0, executed);
    }

    [TestMethod]
    public void TextEditGesture_CanBeRemappedWithoutLegacyKeyHandling()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var textBox = new TextBox { Text = "copy me" };
        textBox.Select(0, textBox.Text.Length);
        window.Content = textBox;
        window.PerformLayout();
        window.FocusManager.SetFocus(textBox);

        var remapped = new Command("test.remappedCopy");
        int executed = 0;
        textBox.Commands.Register(remapped, () => executed++);
        textBox.InputMap.Map(remapped, new KeyGesture(Key.C, ModifierKeys.Control));

        window.SendKeyDown(Key.C, ModifierKeys.Control);

        Assert.AreEqual(1, executed, "Ctrl+C must be resolved by the nearest InputMap");
    }

    [TestMethod]
    public void EffectiveGestureLookup_SkipsShadowedGesture()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var properties = new Button();
        window.Content = properties;
        window.PerformLayout();

        var findCommand = new Command("edit.find");
        var propertiesSearchCommand = new Command("properties.search");
        window.InputMap.Map(findCommand,
            new KeyGesture(Key.F, ModifierKeys.Control),
            new KeyGesture(Key.F3));
        properties.InputMap.Map(propertiesSearchCommand, new KeyGesture(Key.F, ModifierKeys.Control));

        // From the properties context, Ctrl+F means PropertiesSearch, so Find's effective gesture
        // is its alternative F3, never the shadowed Ctrl+F.
        Assert.IsTrue(InputMapResolver.TryGetEffectiveGesture(window, findCommand, properties, out var effective));
        Assert.AreEqual(new KeyGesture(Key.F3), effective);

        Assert.IsTrue(InputMapResolver.TryGetEffectiveGesture(window, propertiesSearchCommand, properties, out var local));
        Assert.AreEqual(new KeyGesture(Key.F, ModifierKeys.Control), local);
    }
}
