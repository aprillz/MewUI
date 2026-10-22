namespace Aprillz.MewUI.Rendering;

/// <summary>Flags describing what a surface can do.</summary>
[Flags]
public enum SurfaceCapabilities
{
    /// <summary>No capability.</summary>
    None = 0,

    /// <summary>A context can draw into the surface.</summary>
    Renderable = 1 << 0,
    /// <summary>The surface can be presented to a window.</summary>
    Presentable = 1 << 1,

    /// <summary>The pixels can be read on the CPU (see <see cref="IRenderDevice.TryReadPixels"/>).</summary>
    CpuReadable = 1 << 2,
    /// <summary>The pixels can be written on the CPU through <see cref="ICpuPixelSurface"/>.</summary>
    CpuWritable = 1 << 3,

    /// <summary>The GPU can sample the surface as a texture (see <see cref="IGpuSampleableSurface"/>).</summary>
    GpuSampleable = 1 << 4,
    /// <summary>The surface has the exact size and layout filter graphs require for intermediates.</summary>
    FilterIntermediate = 1 << 5,
    /// <summary>The surface may be retained as a cached image source.</summary>
    CacheableImageSource = 1 << 6,

    /// <summary>The surface wraps a native resource owned outside the backend.</summary>
    ExternalHandle = 1 << 7,
    /// <summary>Access to the surface is synchronized by its external owner, typically through a lease.</summary>
    ExternallySynchronized = 1 << 8,

    /// <summary>CPU pixels become available only after <see cref="IRenderDevice.RequestReadback"/> completes.</summary>
    DeferredReadback = 1 << 9,
    /// <summary>Writes complete asynchronously; wait on <see cref="IRenderDevice.FlushAsyncWork"/> before reading.</summary>
    AsyncCompletion = 1 << 10,

    /// <summary>The surface has an alpha channel.</summary>
    Alpha = 1 << 11,
    /// <summary>Color channels are premultiplied by alpha.</summary>
    Premultiplied = 1 << 12,
    /// <summary>External code can write into the surface through <see cref="IExternalWritableGpuSurface"/>.</summary>
    ExternalGpuWritable = 1 << 13,
}
