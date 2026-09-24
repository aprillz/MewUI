using System.Reflection;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using MewUI.Test.Infrastructure;

namespace MewUI.MewDock.Test;

/// <summary>
/// A window that lays out and tracks focus without a native window: the no-op backend of the core tests is attached the
/// way the core tests attach it, through the window's internal hooks.
/// </summary>
internal static class DockWindow
{
    private static readonly MethodInfo AttachBackend =
        typeof(Window).GetMethod("AttachBackend", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo SetClientSizeDip =
        typeof(Window).GetMethod("SetClientSizeDip", BindingFlags.NonPublic | BindingFlags.Instance)!;

    public static Window Create(UIElement content, double width = 1000, double height = 700)
    {
        var window = new Window();
        AttachBackend.Invoke(window, [new HeadlessWindowBackend()]);
        SetClientSizeDip.Invoke(window, [width, height]);
        window.Content = content;
        window.PerformLayout();
        return window;
    }
}
