using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Render target for offscreen bitmap rendering.
/// Implementations manage platform-specific resources.
/// Use <see cref="IGraphicsFactory.CreateBitmapRenderTarget"/> to create instances.
/// </summary>
public interface IBitmapRenderTarget : IRenderTarget, IPixelBufferSource, IDisposable
{
    /// <summary>
    /// Gets the pixel width. Resolves ambiguity between IRenderTarget and IPixelBufferSource.
    /// </summary>
    new int PixelWidth { get; }

    /// <summary>
    /// Gets the pixel height. Resolves ambiguity between IRenderTarget and IPixelBufferSource.
    /// </summary>
    new int PixelHeight { get; }

    /// <summary>
    /// Gets the pixel format of the bitmap (always BGRA32).
    /// </summary>
    new BitmapPixelFormat PixelFormat { get; }

    /// <summary>
    /// Copies the rendered pixels to a new array.
    /// </summary>
    /// <returns>A copy of the pixel buffer in BGRA32 format, or empty array if disposed.</returns>
    byte[] CopyPixels();

    /// <summary>
    /// Gets a span over the pixel buffer for direct access.
    /// </summary>
    /// <returns>A span over the pixels, or empty span if disposed.</returns>
    Span<byte> GetPixelSpan();

    /// <summary>
    /// Clears the pixel buffer to the specified color.
    /// </summary>
    void Clear(Color color);

    /// <summary>
    /// Increments the version to signal that pixels have changed.
    /// Call this after modifying pixels via GetPixelSpan() or IGraphicsContext.
    /// </summary>
    void IncrementVersion();
}
