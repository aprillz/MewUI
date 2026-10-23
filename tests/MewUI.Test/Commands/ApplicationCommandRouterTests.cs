using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

/// <summary>
/// The application's router runs commands while no window is open, as a tray icon's menu does: it resolves the
/// target's scope chain, then the application scope, and hands handlers no window.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ApplicationCommandRouterTests
{
    private static void RunWindowless(Action<Application> body)
    {
        TestPlatformHosts.EnsureRegistered();
        Exception? failure = null;
        TestPlatformHosts.Queue.Enqueue(new WindowlessPlatformHost(app =>
        {
            try
            {
                body(app);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }));

        Application.Run(() => { });
        if (failure != null)
        {
            throw new AssertFailedException(failure.Message, failure);
        }
    }

    [TestMethod]
    public void RunsAnApplicationCommand_WithoutAWindow()
    {
        var command = new Command("test.appRouter.run");
        int executed = 0;
        bool sawWindow = true;

        RunWindowless(app =>
        {
            Assert.AreEqual(0, app.AllWindows.Count);
            app.Commands.Register(command, context =>
            {
                executed++;
                sawWindow = context.Window != null;
            });

            var router = app.CommandRouter;
            Assert.IsTrue(router.CaptureTarget().IsEmpty, "the application router has no focus to capture");
            Assert.IsTrue(router.CanExecute(command));
            Assert.IsTrue(router.ExecuteAsync(command).AsTask().GetAwaiter().GetResult());
        });

        Assert.AreEqual(1, executed);
        Assert.IsFalse(sawWindow, "a command run by the application router has no window");
    }

    [TestMethod]
    public void ResolvesTheTargetScope_BeforeTheApplicationScope()
    {
        var command = new Command("test.appRouter.scope");
        string? ran = null;

        RunWindowless(app =>
        {
            var trayScope = new CommandScope(app.Commands);
            trayScope.Register(command, () => ran = "tray");
            app.Commands.Register(command, () => ran = "application");

            Assert.IsTrue(app.CommandRouter.ExecuteAsync(command, CommandTarget.From(trayScope)).AsTask().GetAwaiter().GetResult());
            Assert.AreEqual("tray", ran);

            Assert.IsTrue(app.CommandRouter.ExecuteAsync(command).AsTask().GetAwaiter().GetResult());
            Assert.AreEqual("application", ran);
        });
    }

    [TestMethod]
    public void PassesAGivenArgument_ToATypedHandler()
    {
        var command = new Command("test.appRouter.argument");
        string? received = null;

        RunWindowless(app =>
        {
            app.Commands.Register<string>(command, value => received = value);
            var router = app.CommandRouter;
            var target = CommandTarget.From(app.Commands);

            Assert.IsFalse(router.CanExecute(command, target), "a typed handler needs an operand the target does not supply");
            Assert.IsTrue(router.CanExecute(command, target, "item data"));
            Assert.IsTrue(router.ExecuteAsync(command, target, "item data", source: null).AsTask().GetAwaiter().GetResult());
        });

        Assert.AreEqual("item data", received);
    }

    [TestMethod]
    public void ReportsFalse_WhenNoHandlerExists()
    {
        var command = new Command("test.appRouter.missing");

        RunWindowless(app =>
        {
            Assert.IsFalse(app.CommandRouter.CanExecute(command));
            Assert.IsFalse(app.CommandRouter.ExecuteAsync(command).AsTask().GetAwaiter().GetResult());
        });
    }

    private sealed class WindowlessPlatformHost(Action<Application> onRun) : IPlatformHost
    {
        public IMessageBoxService MessageBox => null!;
        public IFileDialogService FileDialog => null!;
        public IClipboardService Clipboard => null!;
        public string DefaultFontFamily => "Arial";
        public IReadOnlyList<string> DefaultFontFallbacks => [];
        public IWindowBackend CreateWindowBackend(Window window) => throw new NotSupportedException();
        public IDispatcher CreateDispatcher(nint windowHandle) => throw new NotSupportedException();
        public uint GetSystemDpi() => 96;
        public ThemeVariant GetSystemThemeVariant() => ThemeVariant.Light;
        public uint GetDpiForWindow(nint windowHandle) => 96;
        public bool EnablePerMonitorDpiAwareness() => false;
        public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;
        public void Run(Application app, Window? mainWindow) => onRun(app);
        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() { }
    }
}
