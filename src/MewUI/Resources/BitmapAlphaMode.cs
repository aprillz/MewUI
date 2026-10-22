namespace Aprillz.MewUI.Resources;

/// <summary>
/// Backend-agnostic representation of how a bitmap's alpha channel is interpreted.
/// </summary>
public enum BitmapAlphaMode
{
    /// <summary>
    /// The alpha channel is unused and every pixel is treated as fully opaque.
    /// </summary>
    /// <remarks>
    /// Lets consumers skip blend math since the destination is overwritten outright. Use for video frames,
    /// JPEG-decoded images, 24-bit BMP, and any other source guaranteed opaque by construction.
    /// </remarks>
    Ignore,

    /// <summary>
    /// The alpha channel carries straight (non-premultiplied) coverage; RGB values are the original colors
    /// and alpha multiplies them at sample time.
    /// </summary>
    /// <remarks>
    /// Consumers typically premultiply on upload because most GPU pipelines expect premultiplied input.
    /// Sources: PNG decode (default), raw user byte buffers without explicit premultiply.
    /// </remarks>
    Straight,

    /// <summary>
    /// The alpha channel carries premultiplied coverage; RGB values are already multiplied by alpha, so the
    /// consumer blends without an extra multiply step.
    /// </summary>
    Premultiplied,
}
