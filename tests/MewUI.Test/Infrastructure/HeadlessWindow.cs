using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;

namespace MewUI.Test.Infrastructure;

/// <summary>
/// Creates windows that run the full layout/popup pipeline headless: a fake backend passes
/// the Handle gate and the GDI factory provides real text measurement. Windows-only at
/// runtime (GDI); callers should guard with <see cref="OperatingSystem.IsWindows"/>.
/// </summary>
internal static class HeadlessWindow
{
    private static bool _factoryRegistered;

    public static Window Create(double width = 800, double height = 600)
    {
        if (!_factoryRegistered)
        {
            Application.DefaultGraphicsFactory = new GdiGraphicsFactory();
            _factoryRegistered = true;
        }

        var window = new Window();
        window.AttachBackend(new HeadlessWindowBackend());
        window.SetClientSizeDip(width, height);
        return window;
    }
}
