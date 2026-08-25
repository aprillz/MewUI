using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

/// <summary>
/// Acceptance coverage for the pure evaluation model: plain CLR state drives command presentation
/// with no notification API, and invocation always re-queries CanExecute.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CommandEvaluationTests
{
    private sealed class TestDocument
    {
        public bool IsDirty;
        public int SaveCount;

        public void Save() => SaveCount++;
    }

    [TestMethod]
    public void PlainStateChange_UpdatesButtonEnabled_WithoutNotification()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var command = new Command("file.save", "Save");
        var document = new TestDocument();
        window.Commands.Register(command, document,
            static doc => doc.Save(),
            static doc => doc.IsDirty);

        var button = new Button
        {
            Command = command,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = button;
        window.PerformLayout();

        Assert.IsFalse(button.IsEffectivelyEnabled, "clean document leaves Save disabled");

        document.IsDirty = true;
        window.RequerySuggested();

        Assert.IsTrue(button.IsEffectivelyEnabled, "framework evaluation picks up the plain field change");
    }

    [TestMethod]
    public void StalePresentation_ClickDoesNotExecute()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var command = new Command("file.save", "Save");
        var document = new TestDocument { IsDirty = true };
        window.Commands.Register(command, document,
            static doc => doc.Save(),
            static doc => doc.IsDirty);

        var button = new Button
        {
            Command = command,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = button;
        window.PerformLayout();
        window.FocusManager.SetFocus(button);
        Assert.IsTrue(button.IsEffectivelyEnabled);

        // State flips without any evaluation pass; the button visual is stale-enabled.
        document.IsDirty = false;

        window.SendClick(button.CenterOf());

        Assert.AreEqual(0, document.SaveCount, "invocation re-queries CanExecute and refuses to run");
    }

    [TestMethod]
    public void EnabledClick_ExecutesThroughCommand()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var command = new Command("file.save", "Save");
        var document = new TestDocument { IsDirty = true };
        window.Commands.Register(command, document,
            static doc => doc.Save(),
            static doc => doc.IsDirty);

        var button = new Button
        {
            Command = command,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = button;
        window.PerformLayout();

        window.SendClick(button.CenterOf());

        Assert.AreEqual(1, document.SaveCount);
    }

    [TestMethod]
    public void FallbackTargetChange_ReevaluatesCommandSources()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var command = new Command("test.contextual");
        var button = new Button
        {
            Command = command,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = button;
        window.PerformLayout();

        Assert.IsFalse(button.IsEffectivelyEnabled, "no handler anywhere leaves the command button disabled");

        var documentScope = new CommandScope();
        documentScope.Register(command, static () => { });
        window.CommandRouter.FallbackTarget = CommandTarget.From(documentScope);

        Assert.IsTrue(button.IsEffectivelyEnabled, "setting FallbackTarget triggers a command state evaluation");
    }

    [TestMethod]
    public void ButtonDetach_UnregistersFromTracker()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var command = new Command("test.detach");
        var button = new Button { Command = command };
        window.Content = button;
        window.PerformLayout();
        Assert.IsTrue(window.CommandStateTracker.HasSources);

        window.Content = null;

        Assert.IsFalse(window.CommandStateTracker.HasSources);
    }
}
