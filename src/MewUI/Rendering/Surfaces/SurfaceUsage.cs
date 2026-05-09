namespace Aprillz.MewUI.Rendering;

[Flags]
public enum SurfaceUsage
{
    None = 0,

    WindowBackBuffer = 1 << 0,
    Offscreen = 1 << 1,
    ImageSource = 1 << 2,
    FilterIntermediate = 1 << 3,
    FilterSource = 1 << 4,
    ReadbackSource = 1 << 5,
    PresenterIntermediate = 1 << 6,
    CachedImageSource = 1 << 7,
    ExternalSampleSource = 1 << 8,
    AsyncUploadSource = 1 << 9,
    DeferredReadbackSource = 1 << 10,
}
