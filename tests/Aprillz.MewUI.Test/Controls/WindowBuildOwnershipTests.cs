using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// Build ownership: a composition-site callback owns the window build when set; otherwise the
/// virtual OnBuild hook runs once before the first show. Attaching a callback to a window that
/// overrides OnBuild is a DEBUG-guarded misuse.
/// </summary>
[TestClass]
public sealed class WindowBuildOwnershipTests
{
    private sealed class HookWindow : Window
    {
        public int BuildCount { get; private set; }

        protected override void OnBuild()
        {
            BuildCount++;
            this.Title("hook");
        }
    }

    [TestMethod]
    public void VirtualHook_RunsOnceBeforeShow()
    {
        var window = new HookWindow();
        Assert.AreEqual(0, window.BuildCount);

        window.RunBuildHookBeforeShow();
        Assert.AreEqual(1, window.BuildCount);
        Assert.AreEqual("hook", window.Title);

        window.RunBuildHookBeforeShow();
        Assert.AreEqual(1, window.BuildCount);
    }

    [TestMethod]
    public void Callback_RunsImmediatelyAndOwnsTheBuild()
    {
        int callbackRuns = 0;
        var window = new Window().Build(x =>
        {
            callbackRuns++;
            x.Title("callback");
        });

        Assert.AreEqual(1, callbackRuns);
        Assert.IsNotNull(window.BuildCallback);
        Assert.AreEqual("callback", window.Title);
    }

    [TestMethod]
    public void CallbackPresent_SkipsVirtualHook()
    {
        var window = new HookWindow();
        window.SetBuildCallback(x => x.Title("callback"));

        window.RunBuildHookBeforeShow();

        Assert.AreEqual(0, window.BuildCount);
    }

    [TestMethod]
    public void FluentBuild_OnOverriddenWindow_ThrowsInDebug()
    {
        var window = new HookWindow();

        Assert.ThrowsExactly<InvalidOperationException>(() => window.Build(x => x.Title("misuse")));
    }

    [TestMethod]
    public void InvokeOnBuildHook_RerunsTheHook()
    {
        var window = new HookWindow();
        window.RunBuildHookBeforeShow();

        window.InvokeOnBuildHook();

        Assert.AreEqual(2, window.BuildCount);
    }
}
