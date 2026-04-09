using System.Numerics;

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

        /// <summary>
        /// Hints the context that text rendering should produce correct alpha channel values.
        /// Backends that always produce correct alpha (e.g. Direct2D) may ignore this hint.
        /// </summary>
        bool EnableAlphaTextHint { get; set; }

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
        /// Sets a rounded-rectangle clipping region.
        /// Backends may fall back to a rectangular clip if rounded clips are not supported.
        /// </summary>
        void SetClipRoundedRect(Rect rect, double radiusX, double radiusY);

        /// <summary>
        /// Translates the origin of the coordinate system.
        /// </summary>
        void Translate(double dx, double dy);

        /// <summary>
        /// Rotates the coordinate system by <paramref name="angleRadians"/> around the current origin.
        /// Positive angles rotate clockwise (screen-space convention).
        /// </summary>
        void Rotate(double angleRadians);

        /// <summary>Scales the coordinate system by the given factors.</summary>
        void Scale(double sx, double sy);

        /// <summary>
        /// Replaces the current transform with the specified 2-D affine matrix.
        /// </summary>
        void SetTransform(Matrix3x2 matrix);

        /// <summary>Returns the current 2-D affine transform matrix.</summary>
        Matrix3x2 GetTransform();

        /// <summary>Resets the transform to the identity matrix.</summary>
        void ResetTransform();

        /// <summary>
        /// Gets or sets the global opacity multiplier applied to all subsequent drawing operations.
        /// Must be in the range [0, 1]. Defaults to <c>1f</c> (fully opaque).
        /// The value is saved and restored by <see cref="Save"/> / <see cref="Restore"/>.
        /// </summary>
        float GlobalAlpha { get; set; }

        /// <summary>
        /// Gets or sets whether text rendering snaps glyph positions to device pixels.
        /// When <c>true</c> (default), text is pixel-snapped for sharpness.
        /// Set to <c>false</c> during animations/transitions to avoid visible jumping.
        /// The value is saved and restored by <see cref="Save"/> / <see cref="Restore"/>.
        /// </summary>
        bool TextPixelSnap { get; set; }

        /// <summary>
        /// Removes all clipping regions set since the last <see cref="Save"/>.
        /// </summary>
        void ResetClip();

        /// <summary>
        /// Intersects the current clip region with <paramref name="rect"/>.
        /// Equivalent to <see cref="SetClip"/> when no clip has been set.
        /// </summary>
        void IntersectClip(Rect rect);

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

        /// <summary>
        /// Draws a path outline.
        /// </summary>
        void DrawPath(PathGeometry path, Color color, double thickness = 1);

        /// <summary>
        /// Fills a path with a solid color.
        /// </summary>
        void FillPath(PathGeometry path, Color color);

        /// <summary>
        /// Fills a path with a solid color using the specified fill rule.
        /// </summary>
        void FillPath(PathGeometry path, Color color, FillRule fillRule);

        #region Stroke Inset / Clip Inset / Line Snap

        /// <summary>
        /// Draws a rounded rectangle with the stroke inset within <paramref name="rect"/>.
        /// </summary>
        void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness, bool strokeInset);

        /// <summary>
        /// Draws a rectangle with the stroke inset within <paramref name="rect"/>.
        /// </summary>
        void DrawRectangle(Rect rect, Color color, double thickness, bool strokeInset);

        /// <summary>
        /// Draws an ellipse with the stroke inset within <paramref name="bounds"/>.
        /// </summary>
        void DrawEllipse(Rect bounds, Color color, double thickness, bool strokeInset);

        /// <summary>
        /// Sets a rounded-rectangle clipping region adjusted for a border's inner contour.
        /// </summary>
        void SetClipRoundedRect(Rect rect, double radiusX, double radiusY, double borderThickness);

        /// <summary>
        /// Draws a pixel-snapped line.
        /// </summary>
        void DrawLine(Point start, Point end, Color color, double thickness, bool pixelSnap);

        #endregion

        #region IPen / IBrush overloads

        /// <summary>Draws a line using a pen.</summary>
        void DrawLine(Point start, Point end, IPen pen);

        /// <summary>Draws a rectangle outline using a pen.</summary>
        void DrawRectangle(Rect rect, IPen pen);

        /// <summary>Fills a rectangle using a brush.</summary>
        void FillRectangle(Rect rect, IBrush brush);

        /// <summary>Draws a rounded rectangle outline using a pen.</summary>
        void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen);

        /// <summary>Fills a rounded rectangle using a brush.</summary>
        void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush);

        /// <summary>Draws an ellipse outline using a pen.</summary>
        void DrawEllipse(Rect bounds, IPen pen);

        /// <summary>Fills an ellipse using a brush.</summary>
        void FillEllipse(Rect bounds, IBrush brush);

        /// <summary>Draws a path outline using a pen.</summary>
        void DrawPath(PathGeometry path, IPen pen);

        /// <summary>Fills a path using a brush.</summary>
        void FillPath(PathGeometry path, IBrush brush);

        /// <summary>Fills a path using a brush with an explicit fill rule.</summary>
        void FillPath(PathGeometry path, IBrush brush, FillRule fillRule);

        #endregion

        /// <summary>
        /// Draws a box shadow around the specified bounds.
        /// </summary>
        void DrawBoxShadow(Rect bounds, double cornerRadius, double blurRadius,
            Color shadowColor, double offsetX = 0, double offsetY = 0);

        #endregion

        #region Text Rendering

        /// <summary>
        /// Computes text layout from format and constraints.
        /// Layout phase — must not perform any drawing.
        /// </summary>
        TextLayout? CreateTextLayout(ReadOnlySpan<char> text, TextFormat format, in TextLayoutConstraints constraints);

        /// <summary>
        /// Draws text using a precomputed <see cref="TextLayout"/>.
        /// Draw phase — must not re-measure or re-compute layout.
        /// </summary>
        void DrawTextLayout(ReadOnlySpan<char> text, TextFormat format, TextLayout layout, Color color);

        /// <summary>
        /// Draws text within the specified bounds with alignment options.
        /// Convenience facade — internally uses CreateTextLayout + DrawTextLayout.
        /// </summary>
        void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
            TextAlignment horizontalAlignment = TextAlignment.Left,
            TextAlignment verticalAlignment = TextAlignment.Top,
            TextWrapping wrapping = TextWrapping.NoWrap,
            TextTrimming trimming = TextTrimming.None);

        /// <summary>
        /// Measures the size of the specified text.
        /// Convenience facade — internally uses CreateTextLayout.
        /// </summary>
        Size MeasureText(ReadOnlySpan<char> text, IFont font);

        /// <summary>
        /// Measures the size of the specified text within a constrained width.
        /// Convenience facade — internally uses CreateTextLayout.
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
