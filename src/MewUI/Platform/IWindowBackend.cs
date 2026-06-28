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
    /// Sets the window display state (Normal, Minimized, Maximized).
    /// </summary>
    void SetWindowState(Controls.WindowState state) { }

    /// <summary>
    /// Enables or disables the minimize button in the native chrome.
    /// </summary>
    void SetCanMinimize(bool value) { }

    /// <summary>
    /// Enables or disables the maximize button in the native chrome.
    /// </summary>
    void SetCanMaximize(bool value) { }

    /// <summary>
    /// Sets whether the window stays on top of other windows.
    /// </summary>
    void SetTopmost(bool value) { }

    /// <summary>
    /// Sets whether the window appears in the taskbar.
    /// </summary>
    void SetShowInTaskbar(bool value) { }

    /// <summary>
    /// Initiates a window drag move operation using the platform's native mechanism.
    /// </summary>
    void BeginDragMove() { }

    /// <summary>
    /// Initiates a window resize operation from the specified edge using the platform's native mechanism.
    /// </summary>
    void BeginDragResize(Controls.ResizeEdge edge) { }

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
    /// Centers the window on its owner window (platform-specific coordinate handling).
    /// </summary>
    void CenterOnOwner();

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

    /// <summary>
    /// Sets the mouse cursor for the window.
    /// </summary>
    /// <param name="cursorType">The cursor type to display.</param>
    void SetCursor(CursorType cursorType);

    /// <summary>
    /// Sets the IME mode for the window.
    /// </summary>
    void SetImeMode(Input.ImeMode mode);

    /// <summary>
    /// Cancels the current IME composition without committing.
    /// </summary>
    void CancelImeComposition();

    /// <summary>
    /// Extends the client area into the title bar, hiding the default title bar
    /// while preserving the native frame (rounded corners, shadow, system buttons).
    /// </summary>
    /// <param name="titleBarHeight">The height (in DIPs) of the custom title bar area.
    /// Use 0 to restore the default title bar.</param>
    void SetExtendClientAreaToTitleBar(double titleBarHeight) { }

    /// <summary>
    /// Removes (or restores) the entire native non-client area (title bar and border).
    /// Independent of <see cref="SetWindowState"/>; the value is preserved across fullscreen transitions.
    /// </summary>
    /// <param name="value"><see langword="true"/> for a borderless window; otherwise, <see langword="false"/>.</param>
    void SetBorderless(bool value) { }

    /// <summary>
    /// Sets the native window border color (Win11+, macOS).
    /// Use <see langword="null"/> to restore the default.
    /// </summary>
    void SetWindowBorderColor(Color? color) { }

    /// <summary>
    /// Sets the platform-specific window options.
    /// Throws <see cref="InvalidOperationException"/> if the options type does not match the platform.
    /// </summary>
    void SetPlatformOptions(Controls.PlatformWindowOptions? options) { }

    /// <summary>
    /// Gets the native window chrome capabilities supported by this platform.
    /// </summary>
    WindowChromeCapabilities ChromeCapabilities => WindowChromeCapabilities.None;

    /// <summary>
    /// Gets the reserved area (in DIPs) for native chrome buttons when
    /// <see cref="WindowChromeCapabilities.NativeChromeButtons"/> is supported.
    /// Content should avoid overlapping this area.
    /// Returns <see cref="Thickness.Zero"/> if not applicable.
    /// </summary>
    Thickness NativeChromeButtonInset => default;

    /// <summary>
    /// Enables or disables the platform drag-and-drop drop-target registration for this window.
    /// Mirrors <see cref="Controls.UIElement.AllowDrop"/> semantics of WinForms/WPF: a window only acts
    /// as a drop target when <paramref name="allow"/> is <see langword="true"/>.
    /// </summary>
    void SetAllowDrop(bool allow) { }
}
