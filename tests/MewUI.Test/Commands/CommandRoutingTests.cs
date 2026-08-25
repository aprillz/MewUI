using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

[TestClass]
[DoNotParallelize]
public sealed class CommandRoutingTests
{
    [TestMethod]
    public async Task FocusedBinding_Executes()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var button = new Button();
        window.Content = button;
        window.PerformLayout();
        window.FocusManager.SetFocus(button);

        var command = new Command("test.run");
        int executed = 0;
        button.Commands.Register(command, () => executed++);

        bool result = await window.CommandRouter.ExecuteAsync(command);

        Assert.IsTrue(result);
        Assert.AreEqual(1, executed);
    }

    [TestMethod]
    public async Task ExplicitTarget_ExecutesWithoutFocus()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var target = new Button();
        window.Content = target;
        window.PerformLayout();

        var command = new Command("test.explicit");
        int executed = 0;
        target.Commands.Register(command, () => executed++);

        bool result = await window.CommandRouter.ExecuteAsync(command, CommandTarget.From(target));

        Assert.IsTrue(result);
        Assert.AreEqual(1, executed);
    }

    [TestMethod]
    public async Task FallbackTarget_UsedWhenFocusedContextHasNoBinding()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var focused = new Button();
        window.Content = focused;
        window.PerformLayout();
        window.FocusManager.SetFocus(focused);

        var command = new Command("test.fallback");
        int executed = 0;
        var documentScope = new CommandScope();
        documentScope.Register(command, () => executed++);
        window.CommandRouter.FallbackTarget = CommandTarget.From(documentScope);

        bool result = await window.CommandRouter.ExecuteAsync(command);

        Assert.IsTrue(result);
        Assert.AreEqual(1, executed);
    }

    [TestMethod]
    public async Task WindowScope_IsFinalWindowLevelFallback()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var focused = new Button();
        window.Content = focused;
        window.PerformLayout();
        window.FocusManager.SetFocus(focused);

        var command = new Command("test.window");
        int executed = 0;
        window.Commands.Register(command, () => executed++);

        bool result = await window.CommandRouter.ExecuteAsync(command);

        Assert.IsTrue(result);
        Assert.AreEqual(1, executed);
    }

    [TestMethod]
    public async Task NoBinding_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        window.PerformLayout();

        bool result = await window.CommandRouter.ExecuteAsync(new Command("test.unbound"));

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task DisabledNearestBinding_ShadowsEnabledFallback()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var focused = new Button();
        window.Content = focused;
        window.PerformLayout();
        window.FocusManager.SetFocus(focused);

        var command = new Command("test.shadow");
        int nearExecuted = 0;
        int farExecuted = 0;
        focused.Commands.Register(command, () => nearExecuted++, () => false);

        var fallbackScope = new CommandScope();
        fallbackScope.Register(command, () => farExecuted++, () => true);
        window.CommandRouter.FallbackTarget = CommandTarget.From(fallbackScope);

        Assert.IsFalse(window.CommandRouter.CanExecute(command));
        bool result = await window.CommandRouter.ExecuteAsync(command);

        Assert.IsFalse(result);
        Assert.AreEqual(0, nearExecuted);
        Assert.AreEqual(0, farExecuted);
    }

    [TestMethod]
    public async Task ScopeParentChain_Resolves()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        window.PerformLayout();

        var command = new Command("test.parentScope");
        int executed = 0;
        var workspaceScope = new CommandScope();
        workspaceScope.Register(command, () => executed++);
        var documentScope = new CommandScope(workspaceScope);

        bool result = await window.CommandRouter.ExecuteAsync(command, CommandTarget.From(documentScope));

        Assert.IsTrue(result);
        Assert.AreEqual(1, executed);
    }

    [TestMethod]
    public void DuplicateBind_Throws()
    {
        var scope = new CommandScope();
        var command = new Command("test.duplicate");
        scope.Register(command, static () => { });

        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Register(command, static () => { }));
    }

    [TestMethod]
    public void UnbindThenRebind_Succeeds()
    {
        var scope = new CommandScope();
        var command = new Command("test.rebind");
        scope.Register(command, static () => { });

        Assert.IsTrue(scope.Unregister(command));
        Assert.IsFalse(scope.Contains(command));
        scope.Register(command, static () => { });
        Assert.IsTrue(scope.Contains(command));
    }

    [TestMethod]
    public void RegistrationDispose_RemovesOnlyItsOwnBinding()
    {
        var scope = new CommandScope();
        var command = new Command("test.registration");
        var registration = scope.Register(command, static () => { });

        registration.Dispose();
        Assert.IsFalse(scope.Contains(command));

        // A stale token must not remove a newer handler for the same command.
        scope.Register(command, static () => { });
        registration.Dispose();
        Assert.IsTrue(scope.Contains(command));
    }

    [TestMethod]
    public void DisposedScope_RejectsBinding()
    {
        var scope = new CommandScope();
        scope.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => scope.Register(new Command("test.disposed"), static () => { }));
    }
}
