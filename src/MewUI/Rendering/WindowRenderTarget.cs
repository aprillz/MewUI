namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Render target for window-based rendering.
/// Contains a platform-specific <see cref="Platform.IWindowSurface"/> needed by graphics backends.
/// </summary>
/// <remarks>
/// Graphics backends should prefer using <see cref="Surface"/> instead of relying on legacy handle tuples.
/// </remarks>
internal sealed class WindowRenderTarget : IRenderTarget
{
    /// <inheritdoc/>
    public int PixelWidth { get; }

    /// <inheritdoc/>
    public int PixelHeight { get; }

    /// <inheritdoc/>
    public double DpiScale { get; }

    public Platform.IWindowSurface Surface { get; }

    public WindowRenderTarget(Platform.IWindowSurface surface)
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        PixelWidth = Math.Max(1, surface.PixelWidth);
        PixelHeight = Math.Max(1, surface.PixelHeight);
        DpiScale = surface.DpiScale <= 0 ? 1.0 : surface.DpiScale;
    }
}
