namespace Aprillz.MewUI.Rendering;

/// <summary>A surface whose BGRA32 pixels live in CPU memory.</summary>
public interface ICpuPixelSurface : IRenderSurface
{
    /// <summary>Gets the byte distance between the starts of consecutive rows.</summary>
    int StrideBytes { get; }

    /// <summary>Returns the pixel buffer for reading.</summary>
    ReadOnlySpan<byte> GetReadOnlyPixelSpan();

    /// <summary>
    /// Returns the pixel buffer for writing; call <see cref="IncrementVersion"/> and
    /// <see cref="CommitCpuWrite"/> afterwards so consumers see the change.
    /// </summary>
    Span<byte> GetWritablePixelSpan();

    /// <summary>Returns a copy of the pixel buffer.</summary>
    byte[] CopyPixels();

    /// <summary>Advances <see cref="IRenderSurface.Version"/> after the pixels were changed.</summary>
    void IncrementVersion();

    /// <summary>
    /// Flushes pixels written through <see cref="GetWritablePixelSpan"/> to the surface's GPU resource so
    /// a later draw or sample reflects them; a no-op for surfaces whose CPU buffer is the drawn memory or
    /// that upload from it when consumed.
    /// </summary>
    /// <remarks>
    /// The CPU filter executor calls this after writing a result, on the render thread with the backend
    /// context current.
    /// </remarks>
    void CommitCpuWrite() { }

    /// <summary>
    /// Fills every pixel with <paramref name="color"/>, premultiplying it when the surface is
    /// <see cref="SurfaceCapabilities.Premultiplied"/>.
    /// </summary>
    void Clear(Color color)
    {
        var span = GetWritablePixelSpan();
        byte a = color.A;
        bool premultiply = Capabilities.HasFlag(SurfaceCapabilities.Premultiplied);
        byte b = premultiply ? (byte)((color.B * a + 127) / 255) : color.B;
        byte g = premultiply ? (byte)((color.G * a + 127) / 255) : color.G;
        byte r = premultiply ? (byte)((color.R * a + 127) / 255) : color.R;
        for (int i = 0; i + 3 < span.Length; i += 4)
        {
            span[i + 0] = b;
            span[i + 1] = g;
            span[i + 2] = r;
            span[i + 3] = a;
        }
    }
}

/// <summary>A surface the GPU can sample as a texture.</summary>
public interface IGpuSampleableSurface : IRenderSurface
{
    /// <summary>Gets whether row 0 is the bottom of the image, as in a GL framebuffer.</summary>
    bool YFlipped { get; }

    /// <summary>Pins the texture for sampling; dispose the result once the draw that samples it has been issued.</summary>
    IDisposable RetainSampleHandle();
}

/// <summary>A surface that exposes its backend's native handle.</summary>
public interface INativeRenderSurface : IRenderSurface
{
    /// <summary>Gets the native handle; its concrete type is defined by the backend.</summary>
    nint NativeHandle { get; }
}

/// <summary>
/// A surface whose contents can survive into the next frame instead of being cleared when a frame
/// begins on it, which is what lets a repaint redraw only the dirty part.
/// </summary>
internal interface IPersistentFrameSurface
{
    /// <summary>When true the surface keeps the previous frame's pixels at the next BeginFrame.</summary>
    bool PreserveContentsOnBeginFrame { get; set; }
}

/// <summary>A factory whose surfaces can be rendered into while their contents are preserved.</summary>
internal interface IPersistentFrameGraphicsFactory
{
    /// <summary>
    /// True once the backend's persistent-surface replay has passed a real pixel check on its
    /// platform; a backend that only wires the plumbing keeps the window on the immediate path.
    /// </summary>
    bool IsPersistentFrameRenderingVerified { get; }

    /// <summary>
    /// True when a window target still holds the frame presented to it when the next frame begins, so
    /// only the areas that changed need to be copied onto it. A target whose buffer is swapped or
    /// discarded on present does not.
    /// </summary>
    bool WindowTargetKeepsPresentedFrame => false;

    /// <summary>
    /// True when the buffer a window target draws into is kept from frame to frame and can be erased in
    /// part, so an opaque window needs no separate surface to keep its frame in.
    /// </summary>
    bool DrawsWindowFramesInPlace => false;

    /// <summary>Makes the backend ready to render into a persistent surface; disposing restores the caller's state.</summary>
    IDisposable AcquirePersistentFrameRenderScope();
}

/// <summary>Scope for the backends whose persistent-frame rendering needs no extra setup.</summary>
internal sealed class PersistentFrameRenderScope : IDisposable
{
    internal static PersistentFrameRenderScope Instance { get; } = new();

    public void Dispose()
    {
    }
}

/// <summary>A surface whose CPU pixels are produced by an explicit readback.</summary>
public interface IDeferredCpuReadableSurface : IRenderSurface
{
    /// <summary>Gets whether a readback has been requested and not yet applied.</summary>
    bool HasPendingReadback { get; }

    /// <summary>Starts a readback and returns the operation that completes when the pixels are available.</summary>
    IRenderOperation RequestReadback();

    /// <summary>Applies a completed readback to the CPU pixels; returns <see langword="false"/> when none has completed.</summary>
    bool TryFlushReadback();
}
