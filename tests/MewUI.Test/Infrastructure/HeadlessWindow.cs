using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Infrastructure;

/// <summary>
/// Creates windows that run the full layout/popup pipeline headless: a fake backend passes
/// the Handle gate and the GDI factory (registered once in <see cref="AssemblyFixture"/>)
/// provides real text measurement. Windows-only at runtime (GDI); callers should guard
/// with <see cref="OperatingSystem.IsWindows"/>.
/// </summary>
internal static class HeadlessWindow
{
    public static Window Create(double width = 800, double height = 600)
    {
        var window = new Window();
        window.AttachBackend(new HeadlessWindowBackend());
        window.SetClientSizeDip(width, height);
        return window;
    }
}
