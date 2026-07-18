using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
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

    [TestMethod]
    public void FailedRuntime_ReleasesPreRunTimerSubscription()
    {
        EnsureRegistered();
        using var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10));
        timer.Start();
        Assert.IsTrue(timer.IsEnabled);

        Hosts.Enqueue(new FailurePlatformHost(throwFromFontDefaults: true));
        Assert.ThrowsExactly<InvalidOperationException>(() => Application.Run(new Window()));

        Assert.IsFalse(timer.IsEnabled);
    }

    [TestMethod]
    public void RuntimeEnd_ClearsDragRouterStaticState()
    {
        EnsureRegistered();
        var source = new Border { CanDrag = true };
        var mainWindow = new Window { Content = source };
        Hosts.Enqueue(new FailurePlatformHost(onRun: (_, window) =>
        {
            WindowDragDropRouter.OnMouseDown(window, new Point(1, 1), new Point(10, 10), source);
            Assert.IsTrue(WindowDragDropRouter.HasPendingState);
        }));

        Application.Run(mainWindow);

        Assert.IsFalse(WindowDragDropRouter.HasPendingState);
    }

    [TestMethod]
    public void ThemeBroadcast_UsesStableWindowAndAdornerSnapshots()
    {
        EnsureRegistered();
        int addedWindowNotifications = 0;
        var addedWindow = new Window();
        addedWindow.ThemeChanged += (_, _) => addedWindowNotifications++;

        var mainWindow = new Window();
        var adorned = new Border();
        var secondAdorner = new ThemeProbeElement();
        var firstAdorner = new ThemeProbeElement
        {
            Callback = () => mainWindow.RemoveAdornerInternal(secondAdorner),
        };
        mainWindow.AddAdornerInternal(adorned, firstAdorner);
        mainWindow.AddAdornerInternal(adorned, secondAdorner);

        Hosts.Enqueue(new FailurePlatformHost(onRun: (app, window) =>
        {
            window.ThemeChanged += (_, _) =>
            {
                app.UnregisterWindow(window);
                app.RegisterWindow(addedWindow);
            };
            app.SetTheme(ThemeVariant.Dark);
        }));

        Application.Run(mainWindow);

        Assert.AreEqual(1, firstAdorner.NotificationCount);
        Assert.AreEqual(1, secondAdorner.NotificationCount);
        Assert.AreEqual(0, addedWindowNotifications);
    }

    private static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        Application.RegisterPlatformHost(static () => Hosts.Dequeue(), Aprillz.MewUI.Platform.PlatformSurfaceKind.Win32, "Test");
        _registered = true;
    }

    private sealed class FailurePlatformHost(
        bool throwFromFontDefaults = false,
        bool throwFromRun = false,
        Action<Application, Window>? onRun = null) : IPlatformHost
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

            onRun?.Invoke(app, mainWindow);
        }

        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() => Disposed = true;
    }

    private sealed class ThemeProbeElement : ContentControl
    {
        public int NotificationCount { get; private set; }
        public Action? Callback { get; init; }

        protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
        {
            base.OnThemeChanged(oldTheme, newTheme);
            NotificationCount++;
            Callback?.Invoke();
        }
    }
}
