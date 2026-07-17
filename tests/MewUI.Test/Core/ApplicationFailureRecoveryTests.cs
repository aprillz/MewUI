using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace MewUI.Test.Core;

[TestClass]
[DoNotParallelize]
public sealed class ApplicationFailureRecoveryTests
{
    private static readonly Queue<FailurePlatformHost> Hosts = new();
    private static bool _registered;

    [TestMethod]
    public void StartupAndRunFailures_DoNotPreventAnotherRun()
    {
        EnsureRegistered();

        var startupFailure = new FailurePlatformHost(throwFromFontDefaults: true);
        Hosts.Enqueue(startupFailure);
        Assert.ThrowsExactly<InvalidOperationException>(() => Application.Run(new Window()));
        Assert.IsFalse(Application.IsRunning);
        Assert.IsTrue(startupFailure.Disposed);

        var loopFailure = new FailurePlatformHost(throwFromRun: true);
        Hosts.Enqueue(loopFailure);
        Assert.ThrowsExactly<InvalidOperationException>(() => Application.Run(new Window()));
        Assert.IsFalse(Application.IsRunning);
        Assert.IsTrue(loopFailure.Disposed);
        Assert.IsNotNull(loopFailure.RunningApplication);
        Assert.IsEmpty(loopFailure.RunningApplication.AllWindows);

        var successful = new FailurePlatformHost();
        Hosts.Enqueue(successful);
        Application.Run(new Window());
        Assert.IsFalse(Application.IsRunning);
        Assert.IsTrue(successful.Disposed);
    }

    private static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        Application.RegisterPlatformHost(static () => Hosts.Dequeue());
        _registered = true;
    }

    private sealed class FailurePlatformHost(bool throwFromFontDefaults = false, bool throwFromRun = false) : IPlatformHost
    {
        public bool Disposed { get; private set; }
        public Application? RunningApplication { get; private set; }
        public IMessageBoxService MessageBox => null!;
        public IFileDialogService FileDialog => null!;
        public IClipboardService Clipboard => null!;
        public string DefaultFontFamily => throwFromFontDefaults
            ? throw new InvalidOperationException("startup failure")
            : "Arial";
        public IReadOnlyList<string> DefaultFontFallbacks => [];
        public IWindowBackend CreateWindowBackend(Window window) => throw new NotSupportedException();
        public IDispatcher CreateDispatcher(nint windowHandle) => throw new NotSupportedException();
        public uint GetSystemDpi() => 96;
        public ThemeVariant GetSystemThemeVariant() => ThemeVariant.Light;
        public uint GetDpiForWindow(nint windowHandle) => 96;
        public bool EnablePerMonitorDpiAwareness() => false;
        public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

        public void Run(Application app, Window mainWindow)
        {
            RunningApplication = app;
            if (throwFromRun)
            {
                throw new InvalidOperationException("run failure");
            }
        }

        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() => Disposed = true;
    }

}
