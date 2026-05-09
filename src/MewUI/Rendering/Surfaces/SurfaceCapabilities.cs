namespace Aprillz.MewUI.Rendering;

[Flags]
public enum SurfaceCapabilities
{
    None = 0,

    Renderable = 1 << 0,
    Presentable = 1 << 1,

    CpuReadable = 1 << 2,
    CpuWritable = 1 << 3,

    GpuSampleable = 1 << 4,
    FilterIntermediate = 1 << 5,
    CacheableImageSource = 1 << 6,

    ExternalHandle = 1 << 7,
    ExternallySynchronized = 1 << 8,

    DeferredReadback = 1 << 9,
    AsyncCompletion = 1 << 10,

    Alpha = 1 << 11,
    Premultiplied = 1 << 12,
}
