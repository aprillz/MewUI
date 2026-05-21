namespace Aprillz.MewUI.Resources;

/// <summary>
/// Backend-agnostic representation of how a bitmap's alpha channel is interpreted.
/// </summary>
public enum BitmapAlphaMode
{
    /// <summary>
    /// Alpha channel is unused — every pixel is treated as fully opaque. Lets consumers
    /// skip blend math since the destination is overwritten outright. Use for video frames,
    /// JPEG-decoded images, 24-bit BMP, and any other source guaranteed opaque by construction.
    /// </summary>
    Ignore,

    /// <summary>
    /// Alpha channel carries straight (non-premultiplied) coverage — RGB values are the
    /// original colors, alpha multiplies them at sample time. Consumers typically have to
    /// premultiply on upload because most GPU pipelines expect premultiplied input.
    /// Sources: PNG decode (default), raw user byte buffers without explicit premultiply.
    /// </summary>
    Straight,

    /// <summary>
    /// Alpha channel carries premultiplied coverage — RGB values are already multiplied by
    /// alpha. The consumer samples and blends without an extra multiply step.
    /// </summary>
    Premultiplied,
}
