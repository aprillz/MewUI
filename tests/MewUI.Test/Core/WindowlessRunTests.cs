using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Core;

[TestClass]
[DoNotParallelize]
public sealed class WindowlessRunTests
{
    private static Queue<IPlatformHost> Hosts => TestPlatformHosts.Queue;

    [TestMethod]
    public void Run_StartupHasDispatcherAndNoMainWindow()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        int calls = 0;

        Application.Run(() =>
        {
            calls++;
            Assert.AreSame(host.Dispatcher, Application.Current.Dispatcher);
            Assert.AreSame(host.SynchronizationContext, SynchronizationContext.Current);
            Assert.IsTrue(Application.Current.Dispatcher!.IsOnUIThread);
            Assert.IsEmpty(Application.Current.AllWindows);
            Application.Shutdown();
        });

        Assert.AreEqual(1, calls);
        Assert.IsNull(host.MainWindow);
        Assert.IsTrue(host.QuitCalled);
        Assert.IsTrue(host.Disposed);
    }

    [TestMethod]
    public void Run_WindowStartupRunsBeforeMainWindowIsShown()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The headless window test backend uses the Windows GDI graphics factory.");
            return;
        }

        EnsureRegistered();
        var host = new LifecyclePlatformHost(showMainWindow: true);
        Hosts.Enqueue(host);
        var window = HeadlessWindow.Create();
        bool loaded = false;
        bool startupRanBeforeLoaded = false;
        window.Loaded += () => loaded = true;

        Application.Run(window, () => startupRanBeforeLoaded = !loaded);

        Assert.IsTrue(startupRanBeforeLoaded);
        Assert.IsTrue(loaded);
        Assert.AreSame(window, host.MainWindow);
    }

    [TestMethod]
    public void Run_StartupFailureDoesNotPreventNextRun()
    {
        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            Application.Run(() => throw new InvalidOperationException("startup failure")));
        Assert.IsFalse(Application.IsRunning);

        var successful = new LifecyclePlatformHost();
        Hosts.Enqueue(successful);
        Application.Run(Application.Shutdown);

        Assert.IsFalse(Application.IsRunning);
        Assert.IsTrue(successful.QuitCalled);
    }

    [TestMethod]
    public void Run_StartupShownWindowIsRegistered()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The headless window test backend uses the Windows GDI graphics factory.");
            return;
        }

        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());
        var window = HeadlessWindow.Create();

        Application.Run(() =>
        {
            window.Show();
            CollectionAssert.Contains(Application.Current.AllWindows.ToArray(), window);
            Application.Shutdown();
        });
    }

    [TestMethod]
    public void Run_StartupRejectsNestedRun()
    {
        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());
        InvalidOperationException? nested = null;

        Application.Run(() =>
        {
            nested = Assert.ThrowsExactly<InvalidOperationException>(
                () => Application.Run(Application.Shutdown));
            Application.Shutdown();
        });

        Assert.IsNotNull(nested);
        StringAssert.Contains(nested.Message, "already running");
    }

    [TestMethod]
    public void Run_RejectsNullStartup()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Application.Run((Window)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Application.Run((Action)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Application.Run((Action<string[]>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Application.Run(new Window(), (Action)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Application.Run(new Window(), (Action<string[]>)null!));
    }

    [TestMethod]
    public void Run_StartupReceivesCommandLineArgumentsWithoutExecutablePath()
    {
        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());
        string[]? received = null;

        Application.Run(args =>
        {
            received = args;
            Application.Shutdown();
        });

        Assert.IsNotNull(received);
        // The framework supplies the arguments, so the callback sees what Main(string[] args) would.
        CollectionAssert.AreEqual(Environment.GetCommandLineArgs()[1..], received);
    }

    [TestMethod]
    public void Builder_OnStartupWithArgumentsRunsWithoutFactory()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        string[]? received = null;

        Application.Create()
            .OnStartup(args =>
            {
                received = args;
                Application.Shutdown();
            })
            .Run();

        Assert.IsNotNull(received);
        Assert.IsNull(host.MainWindow);
    }

    [TestMethod]
    public void Builder_WithShutdownModeAppliesBeforeStartup()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        ShutdownMode observed = default;

        Application.Create()
            .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
            .OnStartup(() =>
            {
                observed = Application.Current.ShutdownMode;
                var window = new Window();
                Application.Current.RegisterWindow(window);
                Application.Current.UnregisterWindow(window);
            })
            .Run();

        Assert.AreEqual(ShutdownMode.OnExplicitShutdown, observed);
        Assert.IsFalse(host.QuitCalled);
    }

    [TestMethod]
    public void ShutdownMode_DoesNotLeakIntoTheNextRun()
    {
        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());
        Application.Create()
            .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
            .OnStartup(Application.Shutdown)
            .Run();

        Hosts.Enqueue(new LifecyclePlatformHost());
        ShutdownMode observed = default;
        Application.Run(() =>
        {
            observed = Application.Current.ShutdownMode;
            Application.Shutdown();
        });

        Assert.AreEqual(ShutdownMode.OnLastWindowClose, observed);
    }

    [TestMethod]
    public void MainWindow_IsNullWithoutAMainWindowAndReflectsTheRunWindow()
    {
        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());
        Window? windowlessMainWindow = null;

        Application.Run(() =>
        {
            windowlessMainWindow = Application.Current.MainWindow;
            Application.Shutdown();
        });

        Assert.IsNull(windowlessMainWindow);

        Hosts.Enqueue(new LifecyclePlatformHost());
        var mainWindow = new Window();
        Window? observed = null;

        Application.Run(mainWindow, () => observed = Application.Current.MainWindow);

        Assert.AreSame(mainWindow, observed);
    }

    [TestMethod]
    public void MainWindow_AssignedInStartupDrivesOnMainWindowClose()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);

        Application.Create()
            .WithShutdownMode(ShutdownMode.OnMainWindowClose)
            .OnStartup(() =>
            {
                var promoted = new Window();
                Application.Current.RegisterWindow(promoted);
                Application.Current.RegisterWindow(new Window());

                // Without the promotion this mode has no main-window identity and never triggers.
                Application.Current.MainWindow = promoted;
                Application.Current.UnregisterWindow(promoted);
            })
            .Run();

        Assert.IsTrue(host.QuitCalled);
    }

    [TestMethod]
    public void MainWindow_StartupAssignmentOverridesTheBuilderFactoryWindow()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        var factoryWindow = new Window();
        var replacement = new Window();

        Application.Create()
            .WithShutdownMode(ShutdownMode.OnMainWindowClose)
            .BuildMainWindow(() => factoryWindow)
            .OnStartup(() =>
            {
                Assert.AreSame(factoryWindow, Application.Current.MainWindow);
                // Startup runs after the run window is recorded, so a later assignment wins.
                Application.Current.MainWindow = replacement;
                Application.Current.RegisterWindow(replacement);
                Application.Current.UnregisterWindow(factoryWindow);
                Assert.IsFalse(host.QuitCalled);
                Application.Current.UnregisterWindow(replacement);
            })
            .Run();

        Assert.IsTrue(host.QuitCalled);
    }

    [TestMethod]
    public void MainWindow_DoesNotLeakIntoTheNextRun()
    {
        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());
        Application.Run(new Window(), Application.Shutdown);

        Hosts.Enqueue(new LifecyclePlatformHost());
        Window? observed = null;
        Application.Run(() =>
        {
            observed = Application.Current.MainWindow;
            Application.Shutdown();
        });

        Assert.IsNull(observed);
    }

    [TestMethod]
    public void Shutdown_AssignsProcessExitCode()
    {
        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());
        var previous = Environment.ExitCode;

        try
        {
            Application.Run(() => Application.Shutdown(3));

            Assert.AreEqual(3, Environment.ExitCode);
        }
        finally
        {
            // Leaving a non-zero code behind would fail the test host process.
            Environment.ExitCode = previous;
        }
    }

    [TestMethod]
    public void Shutdown_WithoutARunStillReportsTheExitCode()
    {
        var previous = Environment.ExitCode;

        try
        {
            // A request that raced the end of the loop must not lose the code it asked for.
            Application.Shutdown(4);

            Assert.IsFalse(Application.IsRunning);
            Assert.AreEqual(4, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [TestMethod]
    public void Run_ResetsTheExitCodeFromThePreviousRun()
    {
        EnsureRegistered();
        var previous = Environment.ExitCode;

        try
        {
            Hosts.Enqueue(new LifecyclePlatformHost());
            Application.Run(() => Application.Shutdown(5));
            Assert.AreEqual(5, Environment.ExitCode);

            Hosts.Enqueue(new LifecyclePlatformHost());
            int observed = -1;
            Application.Run(() =>
            {
                observed = Environment.ExitCode;
                Application.Shutdown();
            });

            Assert.AreEqual(0, observed);
            Assert.AreEqual(0, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [TestMethod]
    public void DefaultShutdownMode_WindowlessLastWindowCloseRequestsQuit()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);

        Application.Run(() =>
        {
            // The default needs no assignment; a fresh run starts at OnLastWindowClose.
            Assert.AreEqual(ShutdownMode.OnLastWindowClose, Application.Current.ShutdownMode);
            var window = new Window();
            Application.Current.RegisterWindow(window);
            Application.Current.UnregisterWindow(window);
        });

        Assert.IsTrue(host.QuitCalled);
    }

    [TestMethod]
    public void ExplicitShutdown_WindowlessLastWindowCloseKeepsRunningUntilQuit()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);

        Application.Run(() =>
        {
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new Window();
            Application.Current.RegisterWindow(window);
            Application.Current.UnregisterWindow(window);
            Assert.IsFalse(host.QuitCalled);
            Application.Shutdown();
        });

        Assert.IsTrue(host.QuitCalled);
    }

    [TestMethod]
    public void MainWindowShutdown_WindowlessCloseKeepsRunningUntilQuit()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);

        Application.Run(() =>
        {
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new Window();
            Application.Current.RegisterWindow(window);
            Application.Current.UnregisterWindow(window);
            Assert.IsFalse(host.QuitCalled);
            Application.Shutdown();
        });

        Assert.IsTrue(host.QuitCalled);
    }

    [TestMethod]
    public void Builder_OnStartupReplacesPreviousCallbackAndRunsWithoutFactory()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        int result = 0;

        Application.Create()
            .OnStartup(() => result = 1)
            .OnStartup(() =>
            {
                result = 2;
                Application.Shutdown();
            })
            .Run();

        Assert.AreEqual(2, result);
        Assert.IsNull(host.MainWindow);
    }

    [TestMethod]
    public void Builder_FactoryAndStartupUseWindowedRun()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        var window = new Window();
        bool startupCalled = false;

        Application.Create()
            .BuildMainWindow(() => window)
            .OnStartup(() => startupCalled = true)
            .Run();

        Assert.IsTrue(startupCalled);
        Assert.AreSame(window, host.MainWindow);
    }

    [TestMethod]
    public void Builder_RunWindowAppliesStartup()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        var window = new Window();
        bool startupCalled = false;

        Application.Create()
            .OnStartup(() => startupCalled = true)
            .Run(window);

        Assert.IsTrue(startupCalled);
        Assert.AreSame(window, host.MainWindow);
    }

    [TestMethod]
    public void Builder_RunGenericAppliesStartup()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        bool startupCalled = false;

        Application.Create()
            .OnStartup(() => startupCalled = true)
            .Run<Window>();

        Assert.IsTrue(startupCalled);
        Assert.IsInstanceOfType<Window>(host.MainWindow);
    }

    [TestMethod]
    public void Builder_RunWithoutFactoryOrStartupIsRejected()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => Application.Create().Run());

        StringAssert.Contains(error.Message, "BuildMainWindow");
        StringAssert.Contains(error.Message, "OnStartup");
    }

    [TestMethod]
    public void ShowDialog_WithoutRunningApplicationIsRejected()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => new Window().ShowDialog());

        StringAssert.Contains(error.Message, "running application loop");
    }

    [TestMethod]
    public void Run_StartupShowDialogEntersNestedLoop()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The headless window test backend uses the Windows GDI graphics factory.");
            return;
        }

        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        var dialog = HeadlessWindow.Create();
        bool returnedFromDialog = false;

        Application.Run(() =>
        {
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Assert.IsTrue(Application.IsRunning);
            host.NestedFrame = dialog.RaiseClosed;
            dialog.ShowDialog();
            returnedFromDialog = true;
            Application.Shutdown();
        });

        Assert.IsTrue(returnedFromDialog);
        Assert.AreEqual(1, host.NestedLoopCalls);
        Assert.AreEqual(1, host.NestedFrames);
    }

    [TestMethod]
    public void Run_StartupDialogCloseUnderDefaultShutdownModeRequestsQuit()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The headless window test backend uses the Windows GDI graphics factory.");
            return;
        }

        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        var dialog = HeadlessWindow.Create();

        Application.Run(() =>
        {
            host.NestedFrame = dialog.RaiseClosed;
            dialog.ShowDialog();

            // The startup dialog was the only window, so OnLastWindowClose already requested shutdown.
            Assert.IsTrue(host.QuitCalled);
            Assert.IsEmpty(Application.Current.AllWindows);
        });
    }

    [TestMethod]
    public void Run_ExplicitShutdownKeepsRunningAfterStartupDialogCloses()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The headless window test backend uses the Windows GDI graphics factory.");
            return;
        }

        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        var dialog = HeadlessWindow.Create();
        var mainWindow = HeadlessWindow.Create();

        Application.Run(() =>
        {
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            host.NestedFrame = dialog.RaiseClosed;
            dialog.ShowDialog();
            Assert.IsFalse(host.QuitCalled);

            mainWindow.Show();
            CollectionAssert.Contains(Application.Current.AllWindows.ToArray(), mainWindow);
            Application.Shutdown();
        });

        Assert.IsTrue(host.QuitCalled);
    }

    [TestMethod]
    public void Run_StartupNativeMessageBoxUsesManagedPrompt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The headless window test backend uses the Windows GDI graphics factory.");
            return;
        }

        EnsureRegistered();
        var host = new LifecyclePlatformHost { MessageBox = new UnavailableMessageBoxService() };
        Hosts.Enqueue(host);
        bool? result = null;

        Application.Run(() =>
        {
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            host.NestedFrame = CloseNewestWindow;
            result = NativeMessageBox.Show(
                "prompt from startup",
                "caption",
                NativeMessageBoxButtons.YesNo,
                NativeMessageBoxIcon.Question);
            Application.Shutdown();
        });

        // Dismissing without a button press leaves no result; what matters is that the managed
        // prompt ran its nested loop from startup instead of throwing.
        Assert.IsNull(result);
        Assert.AreEqual(1, host.NestedLoopCalls);
    }

    private static void CloseNewestWindow()
    {
        var windows = Application.Current.AllWindows;
        if (windows.Count > 0)
        {
            windows[^1].RaiseClosed();
        }
    }

    private static void EnsureRegistered() => TestPlatformHosts.EnsureRegistered();

    private sealed class LifecyclePlatformHost(bool showMainWindow = false) : IPlatformHost
    {
        // A nested loop whose exit condition never turns false would hang the test run.
        private const int MAX_NESTED_FRAMES = 100;

        private bool _loopRunning;

        public bool Disposed { get; private set; }
        public bool QuitCalled { get; private set; }
        public Window? MainWindow { get; private set; }
        public int NestedLoopCalls { get; private set; }
        public int NestedFrames { get; private set; }

        // Invoked once per nested frame so a test can close the dialog it opened.
        public Action? NestedFrame { get; set; }

        public ImmediateDispatcher Dispatcher { get; } = new();
        public SynchronizationContext SynchronizationContext { get; } = new();
        public IMessageBoxService MessageBox { get; init; } = null!;
        public IFileDialogService FileDialog => null!;
        public IClipboardService Clipboard => null!;
        public string DefaultFontFamily => "Arial";
        public IReadOnlyList<string> DefaultFontFallbacks => [];
        public IWindowBackend CreateWindowBackend(Window window) => new HeadlessWindowBackend();
        public IDispatcher CreateDispatcher(nint windowHandle) => Dispatcher;
        public uint GetSystemDpi() => 96;
        public ThemeVariant GetSystemThemeVariant() => ThemeVariant.Light;
        public uint GetDpiForWindow(nint windowHandle) => 96;
        public bool EnablePerMonitorDpiAwareness() => false;
        public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

        public void Run(Application app, Window? mainWindow)
        {
            MainWindow = mainWindow;
            var previous = System.Threading.SynchronizationContext.Current;
            app.Dispatcher = Dispatcher;
            System.Threading.SynchronizationContext.SetSynchronizationContext(SynchronizationContext);
            // Win32PlatformHost marks the loop live before invoking startup, so a startup callback can
            // already enter a nested modal loop. Mirror that ordering here.
            _loopRunning = true;
            try
            {
                app.OnHostLoopStarting(showMainWindow ? mainWindow : null);
            }
            finally
            {
                _loopRunning = false;
                app.Dispatcher = null;
                System.Threading.SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        public void RunNestedLoop(Func<bool> keepRunning)
        {
            ArgumentNullException.ThrowIfNull(keepRunning);
            if (!_loopRunning)
            {
                return;
            }

            NestedLoopCalls++;
            while (_loopRunning && keepRunning())
            {
                NestedFrames++;
                Assert.IsLessThanOrEqualTo(MAX_NESTED_FRAMES, NestedFrames, "Nested loop did not exit.");
                NestedFrame?.Invoke();
            }
        }

        public void Quit(Application app)
        {
            QuitCalled = true;
            _loopRunning = false;
        }

        public void DoEvents() { }
        public void Dispose() => Disposed = true;
    }

    private sealed class UnavailableMessageBoxService : IMessageBoxService
    {
        public bool IsNativeDialogAvailable() => false;

        public bool? Show(nint owner, string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
            => throw new NotSupportedException();
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool IsOnUIThread => true;
        public DispatcherOperation BeginInvoke(Action action) => throw new NotSupportedException();
        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action action) => throw new NotSupportedException();
        public void Invoke(Action action) => action();
    }
}
