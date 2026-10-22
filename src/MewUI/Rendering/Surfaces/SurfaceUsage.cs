namespace Aprillz.MewUI.Rendering;

/// <summary>Flags naming what a surface is created for, so a backend can pick a matching allocation.</summary>
[Flags]
public enum SurfaceUsage
{
    /// <summary>No usage.</summary>
    None = 0,

    /// <summary>The back buffer of a window.</summary>
    WindowBackBuffer = 1 << 0,
    /// <summary>An offscreen drawing target.</summary>
    Offscreen = 1 << 1,
    /// <summary>Drawn as an image through an image view.</summary>
    ImageSource = 1 << 2,
    /// <summary>An intermediate result of a filter graph.</summary>
    FilterIntermediate = 1 << 3,
    /// <summary>The source layer a filter graph reads.</summary>
    FilterSource = 1 << 4,
    /// <summary>Read back to the CPU.</summary>
    ReadbackSource = 1 << 5,
    /// <summary>A staging surface for presenting a frame to a window.</summary>
    PresenterIntermediate = 1 << 6,
    /// <summary>Retained across frames as a cached image.</summary>
    CachedImageSource = 1 << 7,
    /// <summary>Sampled from an externally owned resource.</summary>
    ExternalSampleSource = 1 << 8,
    /// <summary>Uploaded to the GPU asynchronously.</summary>
    AsyncUploadSource = 1 << 9,
    /// <summary>Read back to the CPU on request rather than immediately.</summary>
    DeferredReadbackSource = 1 << 10,
}
