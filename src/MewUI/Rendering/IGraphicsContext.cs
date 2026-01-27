namespace Aprillz.MewUI.Rendering
{
    /// <summary>
    /// Abstract interface for graphics rendering operations.
    /// Allows swapping the underlying graphics library (GDI, Direct2D, Skia, etc.)
    /// </summary>
    public interface IGraphicsContext : IDisposable
    {
        /// <summary>
        /// Gets the current DPI scale factor.
        /// </summary>
        double DpiScale { get; }

        #region State Management

        /// <summary>
        /// Saves the current graphics state.
        /// </summary>
        void Save();

        /// <summary>
        /// Restores the previously saved graphics state.
        /// </summary>
        void Restore();

        /// <summary>
        /// Sets the clipping region.
        /// </summary>
        void SetClip(Rect rect);

        /// <summary>
        /// Translates the origin of the coordinate system.
        /// </summary>
        void Translate(double dx, double dy);

        #endregion

        #region Drawing Primitives

        /// <summary>
        /// Clears the drawing surface with the specified color.
        /// </summary>
        void Clear(Color color);

        /// <summary>
        /// Draws a line between two points.
        /// </summary>
        void DrawLine(Point start, Point end, Color color, double thickness = 1);

        /// <summary>
        /// Draws a rectangle outline.
        /// </summary>
        void DrawRectangle(Rect rect, Color color, double thickness = 1);

        /// <summary>
        /// Fills a rectangle with a solid color.
        /// </summary>
        void FillRectangle(Rect rect, Color color);

        /// <summary>
        /// Draws a rounded rectangle outline.
        /// </summary>
        void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1);

        /// <summary>
        /// Fills a rounded rectangle with a solid color.
        /// </summary>
        void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color);

        /// <summary>
        /// Draws an ellipse outline.
        /// </summary>
        void DrawEllipse(Rect bounds, Color color, double thickness = 1);

        /// <summary>
        /// Fills an ellipse with a solid color.
        /// </summary>
        void FillEllipse(Rect bounds, Color color);

        #endregion

        #region Text Rendering

        /// <summary>
        /// Draws text at the specified location.
        /// </summary>
        void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color);

        /// <summary>
        /// Draws text within the specified bounds with alignment options.
        /// </summary>
        void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
            TextAlignment horizontalAlignment = TextAlignment.Left,
            TextAlignment verticalAlignment = TextAlignment.Top,
            TextWrapping wrapping = TextWrapping.NoWrap);

        /// <summary>
        /// Measures the size of the specified text.
        /// </summary>
        Size MeasureText(ReadOnlySpan<char> text, IFont font);

        /// <summary>
        /// Measures the size of the specified text within a constrained width.
        /// </summary>
        Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth);

        #endregion

        #region Image Rendering

        /// <summary>
        /// Gets or sets the image interpolation mode used when drawing images.
        /// Backends may treat <see cref="ImageScaleQuality.Default"/> as an alias for their default mode.
        /// </summary>
        ImageScaleQuality ImageScaleQuality { get; set; }

        /// <summary>
        /// Draws an image at the specified location.
        /// </summary>
        void DrawImage(IImage image, Point location);

        /// <summary>
        /// Draws an image scaled to fit within the specified bounds.
        /// </summary>
        void DrawImage(IImage image, Rect destRect);

        /// <summary>
        /// Draws a portion of an image to the specified destination.
        /// </summary>
        void DrawImage(IImage image, Rect destRect, Rect sourceRect);

        #endregion
    }
}

namespace Aprillz.MewUI
{
    /// <summary>
    /// Text horizontal/vertical alignment options.
    /// </summary>
    public enum TextAlignment
    {
        Left,
        Center,
        Right,
        Top = Left,
        Bottom = Right
    }

    /// <summary>
    /// Text wrapping options.
    /// </summary>
    public enum TextWrapping
    {
        NoWrap,
        Wrap,
        WrapWithOverflow
    }
}
