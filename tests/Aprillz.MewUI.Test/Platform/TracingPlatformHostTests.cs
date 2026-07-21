using System.Reflection;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace MewUI.Test.Platform;

[TestClass]
public sealed class TracingPlatformHostTests
{
    [TestMethod]
    public void WindowBackend_ForwardsEveryDefaultInterfaceMember()
    {
        var inner = new RecordingWindowBackend();
        using var host = new TracingPlatformHost(new TestPlatformHost(inner));
        IWindowBackend traced = host.CreateWindowBackend(new Window { Title = "trace-test" });

        InterfaceMapping map = traced.GetType().GetInterfaceMap(typeof(IWindowBackend));
        MethodInfo[] defaultMembers = typeof(IWindowBackend).GetMethods()
            .Where(static method => method.GetMethodBody() != null)
            .ToArray();

        Assert.IsNotEmpty(defaultMembers);
        foreach (MethodInfo member in defaultMembers)
        {
            int index = Array.IndexOf(map.InterfaceMethods, member);
            Assert.IsGreaterThanOrEqualTo(0, index, $"Missing interface map entry for {member.Name}.");
            Assert.AreEqual(traced.GetType(), map.TargetMethods[index].DeclaringType,
                $"{member.Name} is using the interface default instead of forwarding to the inner backend.");
        }

        traced.SetWindowState(WindowState.Maximized);
        traced.SetCanMinimize(false);
        traced.SetCanMaximize(false);
        traced.SetCanClose(false);
        traced.SetTopmost(true);
        traced.SetShowInTaskbar(false);
        traced.BeginDragMove();
        traced.BeginDragResize(ResizeEdge.Left);
        traced.SetExtendClientAreaToTitleBar(32);
        traced.SetBorderless(true);
        traced.SetWindowBorderColor(Color.FromRgb(1, 2, 3));
        traced.SetPlatformOptions(null);
        traced.SetAllowDrop(true);

        Assert.AreEqual(13, inner.DefaultMemberCallCount);
        Assert.AreEqual(inner.ChromeCapabilities, traced.ChromeCapabilities);
        Assert.AreEqual(inner.NativeChromeButtonInset, traced.NativeChromeButtonInset);
    }

    private sealed class TestPlatformHost(RecordingWindowBackend backend) : IPlatformHost
    {
        public IMessageBoxService MessageBox => null!;
        public IFileDialogService FileDialog => null!;
        public IClipboardService Clipboard => null!;
        public string DefaultFontFamily => "Arial";
        public IReadOnlyList<string> DefaultFontFallbacks => [];
        public IWindowBackend CreateWindowBackend(Window window) => backend;
        public IDispatcher CreateDispatcher(nint windowHandle) => throw new NotSupportedException();
        public uint GetSystemDpi() => 96;
        public ThemeVariant GetSystemThemeVariant() => ThemeVariant.Light;
        public uint GetDpiForWindow(nint windowHandle) => 96;
        public bool EnablePerMonitorDpiAwareness() => false;
        public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;
        public void Run(Application app, Window mainWindow) { }
        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() { }
    }

    private sealed class RecordingWindowBackend : IWindowBackend
    {
        public int DefaultMemberCallCount { get; private set; }
        public nint Handle => 1;
        public WindowChromeCapabilities ChromeCapabilities => WindowChromeCapabilities.NativeChromeButtons;
        public Thickness NativeChromeButtonInset => new(1, 2, 3, 4);

        public void SetWindowState(WindowState state) => DefaultMemberCallCount++;
        public void SetCanMinimize(bool value) => DefaultMemberCallCount++;
        public void SetCanMaximize(bool value) => DefaultMemberCallCount++;
        public void SetCanClose(bool value) => DefaultMemberCallCount++;
        public void SetTopmost(bool value) => DefaultMemberCallCount++;
        public void SetShowInTaskbar(bool value) => DefaultMemberCallCount++;
        public void BeginDragMove() => DefaultMemberCallCount++;
        public void BeginDragResize(ResizeEdge edge) => DefaultMemberCallCount++;
        public void SetExtendClientAreaToTitleBar(double titleBarHeight) => DefaultMemberCallCount++;
        public void SetBorderless(bool value) => DefaultMemberCallCount++;
        public void SetWindowBorderColor(Color? color) => DefaultMemberCallCount++;
        public void SetPlatformOptions(PlatformWindowOptions? options) => DefaultMemberCallCount++;
        public void SetAllowDrop(bool allow) => DefaultMemberCallCount++;

        public void SetResizable(bool resizable) { }
        public void PresentSurface() { }
        public void Hide() { }
        public void Close() { }
        public void Invalidate(bool erase) { }
        public void SetTitle(string title) { }
        public void SetIcon(IconSource? icon) { }
        public void SetClientSize(double widthDip, double heightDip) { }
        public Point GetPosition() => default;
        public void SetPosition(double leftDip, double topDip) { }
        public void CaptureMouse() { }
        public void ReleaseMouseCapture() { }
        public Point ClientToScreen(Point clientPointDip) => clientPointDip;
        public Point ScreenToClient(Point screenPointPx) => screenPointPx;
        public void EnsureTheme(bool isDark) { }
        public void CenterOnOwner() { }
        public void Activate() { }
        public void SetOwner(nint ownerHandle) { }
        public void SetEnabled(bool enabled) { }
        public void SetOpacity(double opacity) { }
        public void SetAllowsTransparency(bool allowsTransparency) { }
        public void SetCursor(CursorType cursorType) { }
        public void SetImeMode(ImeMode mode) { }
        public void CancelImeComposition() { }
        public void Dispose() { }
    }
}
