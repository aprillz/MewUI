using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Platform;

/// <summary>
/// A window backend that can put a platform material behind the transparent parts of its window.
/// </summary>
internal interface IWindowSystemBackdropBackend
{
    /// <summary>
    /// Asks for <paramref name="material"/> behind <paramref name="area"/>, or behind the whole client area when it is
    /// null; <see cref="SystemBackdrop.None"/> removes it. The backend reports what it applied through
    /// <see cref="Window.SetActualSystemBackdrop"/>, at once or once its native window exists, lowering a material it
    /// cannot show to the nearest one it can.
    /// </summary>
    void SetSystemBackdrop(SystemBackdrop material, SystemBackdropArea? area);
}

/// <summary>The part of a window's client area a system backdrop fills, in DIPs, cut to a corner radius.</summary>
internal readonly record struct SystemBackdropArea(Rect Bounds, double CornerRadius);
