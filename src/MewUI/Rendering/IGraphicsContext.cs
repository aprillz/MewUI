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
        void Rotate(double angleRadians) { }

        /// <summary>Scales the coordinate system by the given factors.</summary>
        void Scale(double sx, double sy) { }

        /// <summary>
        /// Replaces the current transform with the specified 2-D affine matrix.
        /// </summary>
        void SetTransform(Matrix3x2 matrix) { }

        /// <summary>Returns the current 2-D affine transform matrix.</summary>
        Matrix3x2 GetTransform() => Matrix3x2.Identity;

        /// <summary>Resets the transform to the identity matrix.</summary>
        void ResetTransform() { }

        /// <summary>
        /// Gets or sets the global opacity multiplier applied to all subsequent drawing operations.
        /// Must be in the range [0, 1]. Defaults to <c>1f</c> (fully opaque).
        /// The value is saved and restored by <see cref="Save"/> / <see cref="Restore"/>.
        /// </summary>
        float GlobalAlpha { get => 1f; set { } }

        /// <summary>
        /// Removes all clipping regions set since the last <see cref="Save"/>.
        /// </summary>
        void ResetClip() { }

        /// <summary>
        /// Intersects the current clip region with <paramref name="rect"/>.
        /// Equivalent to <see cref="SetClip"/> when no clip has been set.
        /// </summary>
        void IntersectClip(Rect rect) => SetClip(rect);

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
        /// <remarks>
        /// The default implementation ignores <paramref name="fillRule"/> and uses
        /// the non-zero winding rule.  Backends that support fill rules should override this method.
        /// </remarks>
        void FillPath(PathGeometry path, Color color, FillRule fillRule)
        {
            FillPath(path, color);
        }

        // ── Brush / Pen overloads ──────────────────────────────────────────────────
        // Default interface method implementations delegate to the Color-based overloads so that
        // existing backend implementations compile without modification.
        // Backends may override individual methods for full StrokeStyle / FillRule support.

        /// <summary>Draws a line using a pen.</summary>
        void DrawLine(Point start, Point end, IPen pen)
        {
            if (pen.Brush is ISolidColorBrush s) DrawLine(start, end, s.Color, pen.Thickness);
        }

        /// <summary>Draws a rectangle outline using a pen.</summary>
        void DrawRectangle(Rect rect, IPen pen)
        {
            if (pen.Brush is ISolidColorBrush s) DrawRectangle(rect, s.Color, pen.Thickness);
        }

        /// <summary>Fills a rectangle using a brush.</summary>
        void FillRectangle(Rect rect, IBrush brush)
        {
            if (brush is ISolidColorBrush s) FillRectangle(rect, s.Color);
            else if (brush is IGradientBrush g) FillRectangle(rect, g.GetRepresentativeColor());
        }

        /// <summary>Draws a rounded rectangle outline using a pen.</summary>
        void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen)
        {
            if (pen.Brush is ISolidColorBrush s) DrawRoundedRectangle(rect, radiusX, radiusY, s.Color, pen.Thickness);
        }

        /// <summary>Fills a rounded rectangle using a brush.</summary>
        void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush)
        {
            if (brush is ISolidColorBrush s) FillRoundedRectangle(rect, radiusX, radiusY, s.Color);
            else if (brush is IGradientBrush g) FillRoundedRectangle(rect, radiusX, radiusY, g.GetRepresentativeColor());
        }

        /// <summary>Draws an ellipse outline using a pen.</summary>
        void DrawEllipse(Rect bounds, IPen pen)
        {
            if (pen.Brush is ISolidColorBrush s) DrawEllipse(bounds, s.Color, pen.Thickness);
        }

        /// <summary>Fills an ellipse using a brush.</summary>
        void FillEllipse(Rect bounds, IBrush brush)
        {
            if (brush is ISolidColorBrush s) FillEllipse(bounds, s.Color);
            else if (brush is IGradientBrush g) FillEllipse(bounds, g.GetRepresentativeColor());
        }

        /// <summary>
        /// Draws a path outline using a pen.
        /// <para>
        /// Backends should override this to honour <see cref="StrokeStyle"/> (cap / join).
        /// The default falls back to <see cref="DrawPath(PathGeometry, Color, double)"/>.
        /// </para>
        /// </summary>
        void DrawPath(PathGeometry path, IPen pen)
        {
            if (pen.Brush is ISolidColorBrush s) DrawPath(path, s.Color, pen.Thickness);
        }

        /// <summary>
        /// Fills a path using a brush.  The fill rule is taken from <see cref="PathGeometry.FillRule"/>.
        /// </summary>
        void FillPath(PathGeometry path, IBrush brush)
        {
            if (brush is ISolidColorBrush s) FillPath(path, s.Color, path.FillRule);
            else if (brush is IGradientBrush g) FillPath(path, g.GetRepresentativeColor(), path.FillRule);
        }

        /// <summary>
        /// Fills a path using a brush with an explicit fill rule, overriding <see cref="PathGeometry.FillRule"/>.
        /// </summary>
        void FillPath(PathGeometry path, IBrush brush, FillRule fillRule)
        {
            if (brush is ISolidColorBrush s) FillPath(path, s.Color, fillRule);
            else if (brush is IGradientBrush g) FillPath(path, g.GetRepresentativeColor(), fillRule);
        }

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
