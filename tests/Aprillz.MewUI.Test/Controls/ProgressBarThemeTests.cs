using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class ProgressBarThemeTests
{
    private sealed class ThemeTrackingElement : ContentControl
    {
        public int ThemeChangeCount { get; private set; }
        public Theme? LastTheme { get; private set; }

        protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
        {
            base.OnThemeChanged(oldTheme, newTheme);
            ThemeChangeCount++;
            LastTheme = newTheme;
        }
    }

    [TestMethod]
    public void PrecreatedDetachedElement_ReceivesCurrentThemeOnFirstAttach()
    {
        TestPlatformHosts.EnsureRegistered();

        var detached = new ThemeTrackingElement();
        var window = new Window { Content = new Border() };
        window.AttachBackend(new HeadlessWindowBackend());
        window.SetClientSizeDip(400, 300);
        Theme? darkTheme = null;

        TestPlatformHosts.Queue.Enqueue(new ThemeSwitchHost(onRun: (app, mainWindow) =>
        {
            app.SetTheme(ThemeVariant.Dark);
            darkTheme = app.Theme;
            mainWindow.Content = detached;
            mainWindow.PerformLayout();
        }));

        Application.Run(window);

        Assert.AreEqual(1, detached.ThemeChangeCount);
        Assert.AreEqual(darkTheme!.Palette.WindowBackground, detached.LastTheme!.Palette.WindowBackground);
    }

    [TestMethod]
    public void ProgressBar_RefreshesThemeAfterDetachedThemeSwitch()
    {
        TestPlatformHosts.EnsureRegistered();

        var bar = new ProgressBar().Value(20);
        var indeterminate = new ProgressBar().IsIndeterminate();
        var panel = new StackPanel().Vertical().Children(bar, indeterminate);
        var window = new Window { Content = panel };
        window.AttachBackend(new HeadlessWindowBackend());
        window.SetClientSizeDip(400, 300);

        Color barDark = default, barLight = default, indeterminateLight = default;
        Theme? lightTheme = null;

        TestPlatformHosts.Queue.Enqueue(new ThemeSwitchHost(onRun: (app, mainWindow) =>
        {
            app.SetTheme(ThemeVariant.Dark);
            mainWindow.PerformLayout();
            barDark = bar.Background;

            // Detach the page, switch the theme while detached, then re-attach
            // (mirrors toggling the theme on another gallery page and navigating back).
            mainWindow.Content = new Border();
            mainWindow.PerformLayout();

            app.SetTheme(ThemeVariant.Light);
            mainWindow.PerformLayout();

            mainWindow.Content = panel;
            mainWindow.PerformLayout();

            lightTheme = app.Theme;
            barLight = bar.Background;
            indeterminateLight = indeterminate.Background;
        }));

        Application.Run(window);

        Assert.AreNotEqual(barDark, barLight, "ProgressBar track kept the previous theme color after re-attach.");
        Assert.AreEqual(lightTheme!.Palette.ControlBackground, barLight);
        Assert.AreEqual(lightTheme.Palette.ControlBackground, indeterminateLight);
    }

    private sealed class ThemeSwitchHost(Action<Application, Window>? onRun = null) : IPlatformHost
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
        public void Run(Application app, Window? mainWindow) => onRun?.Invoke(app, mainWindow!);
        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() { }
    }
}
