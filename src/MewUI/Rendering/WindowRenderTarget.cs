namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Render target for window-based rendering.
/// Contains platform-specific handles needed by graphics backends.
/// </summary>
/// <remarks>
/// On Windows: Hwnd is the window handle (HWND), DeviceContext is HDC.
/// On X11/Linux: Hwnd is the X11 Window (Drawable), DeviceContext is Display*.
/// </remarks>
internal sealed class WindowRenderTarget : IRenderTarget
{
    /// <summary>
    /// Gets the native window handle.
    /// </summary>
    public nint Hwnd { get; }

    /// <summary>
    /// Gets the device context or display pointer.
    /// </summary>
    public nint DeviceContext { get; }

    /// <inheritdoc/>
    public int PixelWidth { get; }

    /// <inheritdoc/>
    public int PixelHeight { get; }

    /// <inheritdoc/>
    public double DpiScale { get; }

    public WindowRenderTarget(nint hwnd, nint deviceContext, int pixelWidth, int pixelHeight, double dpiScale)
    {
        Hwnd = hwnd;
        DeviceContext = deviceContext;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale;
    }
}
