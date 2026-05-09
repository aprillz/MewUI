namespace Aprillz.MewUI.Resources;

/// <summary>
/// Backend-agnostic representation of how a bitmap's alpha channel is interpreted by
/// rendering backends. Wraps the equivalent native enums (D2D1_ALPHA_MODE,
/// MTLPixelFormat alpha conventions, etc.) so MewUI public surfaces don't leak
/// backend-specific types.
/// </summary>
public enum BitmapAlphaMode
{
    /// <summary>
    /// Alpha channel is unused — every pixel is treated as fully opaque. Backends can pick
    /// the equivalent of D2D1_ALPHA_MODE_IGNORE: blend math is skipped on the GPU since the
    /// destination is overwritten outright. Use for video frames, JPEG-decoded images,
    /// 24-bit BMP, and any other source guaranteed opaque by construction.
    /// </summary>
    Ignore,

    /// <summary>
    /// Alpha channel carries straight (non-premultiplied) coverage — RGB values are the
    /// original colors, alpha multiplies them at sample time. Backends typically have to
    /// premultiply on upload because most GPU pipelines expect premultiplied input.
    /// Sources: PNG decode (default), raw user byte buffers without explicit premultiply.
    /// </summary>
    Straight,

    /// <summary>
    /// Alpha channel carries premultiplied coverage — RGB values are already multiplied by
    /// alpha. Standard for D2D, Metal/GL render-target output, and NVG's
    /// <c>NVG_IMAGE_PREMULTIPLIED</c> flag. The GPU samples and blends without an extra
    /// multiply step.
    /// </summary>
    Premultiplied,
}
