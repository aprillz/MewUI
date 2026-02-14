namespace Aprillz.MewUI.Platform;

/// <summary>
/// Platform-specific window backend responsible for native window lifetime, invalidation and input integration.
/// </summary>
public interface IWindowBackend : IDisposable
{
    /// <summary>
    /// Gets the native window handle.
    /// </summary>
    nint Handle { get; }

    /// <summary>
    /// Enables or disables window resizing.
    /// </summary>
    /// <param name="resizable"><see langword="true"/> to allow resizing; otherwise, <see langword="false"/>.</param>
    void SetResizable(bool resizable);

    /// <summary>
    /// Shows the native window.
    /// </summary>
    void Show();

    /// <summary>
    /// Hides the native window.
    /// </summary>
    void Hide();

    /// <summary>
    /// Requests the native window to close.
    /// </summary>
    void Close();

    /// <summary>
    /// Invalidates the window so it will be repainted.
    /// </summary>
    /// <param name="erase">Whether the background should be erased (platform dependent).</param>
    void Invalidate(bool erase);

    /// <summary>
    /// Sets the native window title.
    /// </summary>
    /// <param name="title">Window title.</param>
    void SetTitle(string title);

    /// <summary>
    /// Sets the native window icon.
    /// </summary>
    /// <param name="icon">Icon source, or <see langword="null"/> to clear.</param>
    void SetIcon(IconSource? icon);

    /// <summary>
    /// Sets the window client size in DIPs.
    /// </summary>
    /// <param name="widthDip">Width in DIPs.</param>
    /// <param name="heightDip">Height in DIPs.</param>
    void SetClientSize(double widthDip, double heightDip);

    /// <summary>
    /// Gets the window position in DIPs (screen coordinates).
    /// </summary>
    Point GetPosition();

    /// <summary>
    /// Sets the window position in DIPs (screen coordinates).
    /// </summary>
    /// <param name="leftDip">Left in DIPs.</param>
    /// <param name="topDip">Top in DIPs.</param>
    void SetPosition(double leftDip, double topDip);

    /// <summary>
    /// Captures mouse input at the native window level so the window continues to receive mouse events,
    /// even when the pointer leaves the client area (platform dependent).
    /// </summary>
    void CaptureMouse();

    /// <summary>
    /// Releases any active mouse capture.
    /// </summary>
    void ReleaseMouseCapture();

    /// <summary>
    /// Converts a point from window client coordinates (DIPs) to screen coordinates (device pixels).
    /// </summary>
    Point ClientToScreen(Point clientPointDip);

    /// <summary>
    /// Converts a point from screen coordinates (device pixels) to window client coordinates (DIPs).
    /// </summary>
    Point ScreenToClient(Point screenPointPx);

    /// <summary>
    /// Applies platform theme-related settings for the window.
    /// </summary>
    /// <param name="isDark"><see langword="true"/> for dark mode; otherwise, <see langword="false"/>.</param>
    void EnsureTheme(bool isDark);

    /// <summary>
    /// Attempts to activate the window (bring to front / focus), platform dependent.
    /// </summary>
    void Activate();

    /// <summary>
    /// Sets the owner window for this window (modal / transient relationship), platform dependent.
    /// </summary>
    /// <param name="ownerHandle">The backend handle of the owner window, or 0 to clear.</param>
    void SetOwner(nint ownerHandle);

    /// <summary>
    /// Enables or disables the window for input (used for modal dialogs).
    /// </summary>
    /// <param name="enabled"><see langword="true"/> to enable input; otherwise, <see langword="false"/>.</param>
    void SetEnabled(bool enabled);

    /// <summary>
    /// Sets the window opacity (0..1).
    /// </summary>
    /// <param name="opacity">Opacity in the range [0, 1].</param>
    void SetOpacity(double opacity);

    /// <summary>
    /// Enables or disables per-pixel transparency for the window (platform dependent).
    /// </summary>
    /// <param name="allowsTransparency"><see langword="true"/> to enable per-pixel transparency.</param>
    void SetAllowsTransparency(bool allowsTransparency);
}
