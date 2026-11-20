using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A tray icon appears on a later dispatcher turn with the properties set around <see cref="TrayIcon.IsVisible"/>,
/// leaves when hidden, disposed or when the run ends, and runs its command through its own scope first.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TrayIconTests
{
    [TestMethod]
    public void AppearsOnALaterTurn_WithPropertiesSetAfterIsVisible()
    {
        var host = new TrayPlatformHost((app, turns, backends) =>
        {
            var tray = new TrayIcon { IsVisible = true, ToolTipText = "set after IsVisible" };
            Assert.AreEqual(0, backends.Count, "the icon appeared before the properties around IsVisible were set");

            turns.RunAll();
            Assert.AreEqual(1, backends.Count);
            Assert.AreEqual("set after IsVisible", backends[0].ToolTipAtCreation);
            Assert.IsTrue(tray.IsShown);
        });

        host.Run();
    }

    [TestMethod]
    public void CreatedBeforeTheRun_AppearsOnceTheRunHasADispatcher()
    {
        TrayIcon? tray = null;
        var host = new TrayPlatformHost((app, turns, backends) =>
        {
            Assert.AreEqual(0, backends.Count);
            turns.RunAll();
            Assert.AreEqual(1, backends.Count);
            Assert.IsTrue(tray!.IsShown);
        });

        tray = new TrayIcon().IsVisible();
        host.Run();
    }

    [TestMethod]
    public void NothingAppears_UntilIsVisible()
    {
        var host = new TrayPlatformHost((app, turns, backends) =>
        {
            var tray = new TrayIcon { ToolTipText = "hidden" };
            turns.RunAll();
            Assert.AreEqual(0, backends.Count);
            Assert.IsFalse(tray.IsShown);
        });

        host.Run();
    }

    [TestMethod]
    public void LeavesWhenHiddenOrDisposed_AndAtTheEndOfTheRun()
    {
        var host = new TrayPlatformHost((app, turns, backends) =>
        {
            var hidden = new TrayIcon().IsVisible();
            var disposed = new TrayIcon().IsVisible();
            new TrayIcon().IsVisible();
            turns.RunAll();
            Assert.AreEqual(3, backends.Count);

            hidden.IsVisible = false;
            Assert.IsTrue(backends[0].Disposed, "hiding the icon did not remove it");

            disposed.Dispose();
            Assert.IsTrue(backends[1].Disposed, "disposing the icon did not remove it");
            Assert.IsFalse(backends[2].Disposed);
        });

        host.Run();
        Assert.IsTrue(host.Backends[2].Disposed, "the icon outlived the run");
    }

    [TestMethod]
    public void AppearanceChanges_ReachTheShownIcon()
    {
        var host = new TrayPlatformHost((app, turns, backends) =>
        {
            var tray = new TrayIcon().IsVisible();
            turns.RunAll();
            int before = backends[0].Updates;

            tray.ToolTipText = "changed";
            tray.IsMonochromeIcon = true;
            tray.ContextMenu = new ContextMenu();
            Assert.AreEqual(before + 3, backends[0].Updates);
        });

        host.Run();
    }

    [TestMethod]
    public void Click_RaisesTheEventThenRunsTheCommand_InTheIconScopeFirst()
    {
        var command = new Command("test.tray.click");
        var order = new List<string>();
        object? received = null;

        var host = new TrayPlatformHost((app, turns, backends) =>
        {
            app.Commands.Register<string>(command, _ => order.Add("application"));
            var tray = new TrayIcon().Command(command).CommandData("tray data");
            tray.Commands.Register<string>(command, value =>
            {
                received = value;
                order.Add("icon");
            });
            tray.Click += args => order.Add($"click {args.ScreenPosition.X},{args.ScreenPosition.Y} {args.Button}");

            tray.RaiseClick(new Point(10, 20), MouseButton.Left);
        });

        host.Run();
        CollectionAssert.AreEqual(new[] { "click 10,20 Left", "icon" }, order);
        Assert.AreEqual("tray data", received);
    }

    [TestMethod]
    public void MenuOpening_MayReplaceTheMenu()
    {
        var host = new TrayPlatformHost((app, turns, backends) =>
        {
            var replacement = new ContextMenu();
            var tray = new TrayIcon().ContextMenu(new ContextMenu());
            tray.MenuOpening += () => tray.ContextMenu = replacement;

            Assert.AreSame(replacement, tray.PrepareMenu());
        });

        host.Run();
    }

    [TestMethod]
    public void IsSupported_IsFalseBeforeARun()
        => Assert.IsFalse(TrayIcon.IsSupported);

    /// <summary>A run with a dispatcher whose turns the test drives and a tray that records its icons.</summary>
    internal sealed class TrayPlatformHost(Action<Application, QueuedDispatcher, List<RecordingTrayBackend>> body) : IPlatformHost
    {
        private Exception? _failure;

        public List<RecordingTrayBackend> Backends { get; } = new();

        public IMessageBoxService MessageBox => null!;
        public IFileDialogService FileDialog => null!;
        public IClipboardService Clipboard => null!;
        public string DefaultFontFamily => "Arial";
        public IReadOnlyList<string> DefaultFontFallbacks => [];
        public bool IsTrayIconSupported => true;

        public ITrayIconBackend? CreateTrayIconBackend(TrayIcon trayIcon)
        {
            var backend = new RecordingTrayBackend(trayIcon);
            Backends.Add(backend);
            return backend;
        }

        public IWindowBackend CreateWindowBackend(Window window) => throw new NotSupportedException();
        public IDispatcher CreateDispatcher(nint windowHandle) => throw new NotSupportedException();
        public uint GetSystemDpi() => 96;
        public ThemeVariant GetSystemThemeVariant() => ThemeVariant.Light;
        public uint GetDpiForWindow(nint windowHandle) => 96;
        public bool EnablePerMonitorDpiAwareness() => false;
        public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;
        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() { }

        public void Run(Application app, Window? mainWindow)
        {
            var turns = new QueuedDispatcher();
            app.Dispatcher = turns;
            try
            {
                body(app, turns, Backends);
            }
            catch (Exception exception)
            {
                _failure = exception;
            }
        }

        public void Run()
        {
            TestPlatformHosts.EnsureRegistered();
            TestPlatformHosts.Queue.Enqueue(this);
            Application.Run(() => { });
            if (_failure != null)
            {
                throw new AssertFailedException(_failure.Message, _failure);
            }
        }
    }

    internal sealed class RecordingTrayBackend(TrayIcon trayIcon) : ITrayIconBackend
    {
        public string ToolTipAtCreation { get; } = trayIcon.ToolTipText;
        public int Updates { get; private set; }
        public bool Disposed { get; private set; }

        public void Update() => Updates++;

        public void Dispose() => Disposed = true;
    }

    /// <summary>A dispatcher whose posted work runs only when the test runs its turns; timers never fire.</summary>
    internal sealed class QueuedDispatcher : IDispatcher, IDispatcherCore
    {
        private readonly Queue<Action> _pending = new();

        public bool IsOnUIThread => true;

        public bool PostMerged(DispatcherMergeKey mergeKey, Action action, DispatcherPriority priority)
        {
            _pending.Enqueue(action);
            return true;
        }

        public void ProcessWorkItems() => RunAll();

        public IDisposable Schedule(TimeSpan dueTime, Action action) => new NeverDue();

        public DispatcherOperation BeginInvoke(Action action) => BeginInvoke(DispatcherPriority.Normal, action);

        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action action)
        {
            _pending.Enqueue(action);
            return new DispatcherOperation(priority, action);
        }

        public void Invoke(Action action) => action();

        public void RunAll()
        {
            while (_pending.Count > 0)
            {
                _pending.Dequeue()();
            }
        }

        private sealed class NeverDue : IDisposable
        {
            public void Dispose() { }
        }
    }
}
