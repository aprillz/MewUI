using System.Numerics;

using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Draw helper that applies an <see cref="ImageOrientation"/> while drawing. Raw <c>DrawImage</c> calls do
/// not auto-orient (the backend only sees an <see cref="IImage"/> with no orientation, and the
/// apply-or-ignore choice belongs to the caller), so any consumer that wants oriented output - the
/// <see cref="Controls.Image"/> control, blurred backdrops, thumbnails - routes through this single
/// implementation instead of re-deriving the transform.
/// </summary>
public static class ImageOrientationDrawExtensions
{
    /// <summary>
    /// Draws <paramref name="orientedSrc"/> (a region in the image's oriented pixel space) into
    /// <paramref name="dest"/>, applying <paramref name="orientation"/>. Composes raw->oriented (the
    /// rotation/flip) with oriented->dest (scale/translate) so the backend rasterizes the rotation in one
    /// pass. <paramref name="rawWidth"/>/<paramref name="rawHeight"/> are the as-decoded pixel dimensions.
    /// </summary>
    public static void DrawImageOriented(
        this IGraphicsContext context,
        IImage image,
        ImageOrientation orientation,
        int rawWidth,
        int rawHeight,
        Rect orientedSrc,
        Rect dest)
    {
        orientation = OrientationTransform.Normalize(orientation);
        var rawSrc = OrientationTransform.OrientedToRaw(orientation, rawWidth, rawHeight, orientedSrc);

        if (orientation == ImageOrientation.Normal)
        {
            // No rotation: oriented space == raw space, so this is a plain source-to-dest draw.
            context.DrawImage(image, dest, rawSrc);
            return;
        }

        // Same source-rect-to-dest-rect form DrawImage uses internally, then prepend the rotation/flip.
        var orientedToDest =
            Matrix3x2.CreateTranslation((float)-orientedSrc.X, (float)-orientedSrc.Y) *
            Matrix3x2.CreateScale((float)(dest.Width / orientedSrc.Width), (float)(dest.Height / orientedSrc.Height)) *
            Matrix3x2.CreateTranslation((float)dest.X, (float)dest.Y);
        var rawToDest = OrientationTransform.CreateRawToOrientedMatrix(orientation, rawWidth, rawHeight) * orientedToDest;

        var previous = context.GetTransform();
        context.SetTransform(rawToDest * previous);
        // dest == src == rawSrc: DrawImage's own identity mapping leaves the composed transform to place and
        // orient the pixels.
        context.DrawImage(image, rawSrc, rawSrc);
        context.SetTransform(previous);
    }
}
