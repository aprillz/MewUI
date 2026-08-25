using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

/// <summary>
/// Measurement and verification coverage for the open command-system questions: evaluation pass
/// cost (proposal §67), RequerySuggested tree-walk cost, tracker release on window close, and
/// application-scope fallback under a running Application. Timing numbers are scoping figures
/// from a unit-test process, not rigorous benchmarks.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CommandMeasurementTests
{
    private sealed class FlagState
    {
        public bool Flag1 = true;
        public bool Flag2;
    }

    private static Window BuildCommandSourceWindow(int sourceCount, FlagState state)
    {
        var window = HeadlessWindow.Create();
        var panel = new StackPanel();

        for (int index = 0; index < sourceCount; index++)
        {
            var command = new Command($"measure.cmd{index}");
            window.Commands.Register(command, state,
                static ignored => { },
                static flagState => flagState.Flag1 && !flagState.Flag2);
            panel.Add(new Button { Command = command });
        }

        window.Content = panel;
        return window;
    }

    [TestMethod]
    public void Measure_EvaluationPassCost()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        foreach (int sourceCount in new[] { 10, 100, 500, 1000 })
        {
            var state = new FlagState();
            var window = BuildCommandSourceWindow(sourceCount, state);
            Assert.IsTrue(window.CommandStateTracker.HasSources);

            for (int warmup = 0; warmup < 50; warmup++)
            {
                window.EvaluateCommandStates();
            }

            const int ITERATIONS = 500;
            var stopwatch = Stopwatch.StartNew();
            for (int iteration = 0; iteration < ITERATIONS; iteration++)
            {
                window.EvaluateCommandStates();
            }

            stopwatch.Stop();

            double microsecondsPerPass = stopwatch.Elapsed.TotalMicroseconds / ITERATIONS;
            Console.WriteLine(
                $"EvaluateCommandStates: sources={sourceCount,5} pass={microsecondsPerPass,10:F2} us per-source={microsecondsPerPass * 1000 / sourceCount,8:F1} ns");
        }
    }

    [TestMethod]
    public void Measure_RequerySuggestedTreeWalkCost()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        foreach (int sourceCount in new[] { 100, 1000 })
        {
            var state = new FlagState();
            var window = BuildCommandSourceWindow(sourceCount, state);

            for (int warmup = 0; warmup < 20; warmup++)
            {
                window.RequerySuggested();
            }

            const int ITERATIONS = 200;
            var stopwatch = Stopwatch.StartNew();
            for (int iteration = 0; iteration < ITERATIONS; iteration++)
            {
                window.RequerySuggested();
            }

            stopwatch.Stop();

            double microsecondsPerPass = stopwatch.Elapsed.TotalMicroseconds / ITERATIONS;
            Console.WriteLine(
                $"RequerySuggested: elements~{sourceCount,5} pass={microsecondsPerPass,10:F2} us");
        }
    }

    [TestMethod]
    public void WindowCloseSequence_ReleasesCommandSources()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var button = new Button { Command = new Command("measure.close") };
        window.Content = button;
        window.PerformLayout();
        Assert.IsTrue(window.CommandStateTracker.HasSources);

        window.RaiseClosed();
        window.DisposeVisualTree();

        Assert.IsFalse(window.CommandStateTracker.HasSources,
            "closing a window must release its registered command sources; a retained source keeps the closed window graph alive through Button._commandSourceWindow");
    }

    [TestMethod]
    public void ApplicationScope_IsFinalFallbackWhileRunning()
    {
        TestPlatformHosts.EnsureRegistered();

        int executed = 0;
        bool? effectiveCopyGestureFound = null;
        var command = new Command("measure.appScope");

        TestPlatformHosts.Queue.Enqueue(new MinimalPlatformHost((app, mainWindow) =>
        {
            app.Commands.Register(command, () => executed++);

            bool invoked = mainWindow.CommandRouter.ExecuteAsync(command).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(invoked);

            // The default application InputMap participates in effective shortcut resolution.
            effectiveCopyGestureFound = InputMapResolver.TryGetEffectiveGesture(
                mainWindow, StandardCommands.Copy, origin: null, out _);
        }));

        Application.Run(new Window());

        Assert.AreEqual(1, executed, "routing falls through to the application scope");
        Assert.IsTrue(effectiveCopyGestureFound, "the default edit gestures resolve at the application level");
    }

    private sealed class MinimalPlatformHost(Action<Application, Window> onRun) : IPlatformHost
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
        public void Run(Application app, Window? mainWindow) => onRun(app, mainWindow!);
        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() { }
    }
}
