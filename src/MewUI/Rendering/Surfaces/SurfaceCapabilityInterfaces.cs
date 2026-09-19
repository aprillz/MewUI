namespace Aprillz.MewUI.Rendering;

public interface ICpuPixelSurface : IRenderSurface
{
    int StrideBytes { get; }

    ReadOnlySpan<byte> GetReadOnlyPixelSpan();

    Span<byte> GetWritablePixelSpan();

    byte[] CopyPixels();

    void IncrementVersion();

    /// <summary>
    /// Flushes pixels written through <see cref="GetWritablePixelSpan"/> to the surface's GPU resource so a
    /// later draw or sample reflects them. No-op for surfaces whose CPU buffer IS the drawn memory or that
    /// already upload from the CPU mirror on consume; surfaces that sample a GPU texture directly override
    /// it to upload. Called by the CPU
    /// filter executor after writing a result, on the render thread with the backend context current.
    /// </summary>
    void CommitCpuWrite() { }

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

public interface IGpuSampleableSurface : IRenderSurface
{
    bool YFlipped { get; }

    IDisposable RetainSampleHandle();
}

public interface INativeRenderSurface : IRenderSurface
{
    nint NativeHandle { get; }
}

/// <summary>
/// A surface whose contents can survive into the next frame instead of being cleared when a frame
/// begins on it, which is what lets a repaint redraw only the damaged part.
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

public interface IDeferredCpuReadableSurface : IRenderSurface
{
    bool HasPendingReadback { get; }

    IRenderOperation RequestReadback();

    bool TryFlushReadback();
}
