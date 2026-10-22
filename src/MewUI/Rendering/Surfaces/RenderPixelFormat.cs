namespace Aprillz.MewUI.Rendering;

/// <summary>Pixel layout of a surface or external raster.</summary>
public enum RenderPixelFormat
{
    /// <summary>Not specified.</summary>
    Unknown = 0,
    /// <summary>8 bits per channel in B, G, R, A order with straight alpha.</summary>
    Bgra8888,
    /// <summary>8 bits per channel in B, G, R, A order with premultiplied alpha.</summary>
    Bgra8888Premultiplied,
    /// <summary>8 bits per channel in R, G, B, A order with straight alpha.</summary>
    Rgba8888,
    /// <summary>8 bits per channel in R, G, B, A order with premultiplied alpha.</summary>
    Rgba8888Premultiplied,
    /// <summary>A single 8-bit alpha channel.</summary>
    Alpha8,
}
