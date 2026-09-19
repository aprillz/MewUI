using System.Numerics;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Graphics context that records every call into the current content slot and forwards it to the
/// real context, so one paint both draws the frame and captures it.
/// </summary>
internal sealed class RenderDataRecorder : IGraphicsContext
{
    private readonly IGraphicsContext _inner;
    private readonly RecordingTextContext _text;

    internal RenderDataRecorder(IGraphicsContext inner)
    {
        _inner = inner;
        _text = new RecordingTextContext(this, inner.Text);
    }

    /// <summary>Slot the following calls are recorded into. Calls are dropped while this is null.</summary>
    internal RenderDataBuilder? Slot { get; set; }

    /// <summary>
    /// While true the drawing calls are recorded but not drawn, so a pass can update the scene
    /// before anything reaches the surface. Graphics state still reaches the context, which is what
    /// keeps the transform and clip a visual reads during recording correct.
    /// </summary>
    internal bool SuppressDrawing { get; set; }

    internal bool Draws => !SuppressDrawing;

    internal IGraphicsContext Inner => _inner;

    public ITextRenderContext Text => _text;

    public double DpiScale => _inner.DpiScale;

    public bool EnableAlphaTextHint
    {
        get => _inner.EnableAlphaTextHint;
        set
        {
            Record(RenderCommandKind.SetAlphaTextHint, flags: BooleanFlag(value));
            _inner.EnableAlphaTextHint = value;
        }
    }

    public ImageScaleQuality ImageScaleQuality
    {
        get => _inner.ImageScaleQuality;
        set
        {
            Record(RenderCommandKind.SetImageScaleQuality, values: [(int)value]);
            _inner.ImageScaleQuality = value;
        }
    }

    public float GlobalAlpha
    {
        get => _inner.GlobalAlpha;
        set
        {
            Record(RenderCommandKind.SetGlobalAlpha, values: [value]);
            _inner.GlobalAlpha = value;
        }
    }

    public bool TextPixelSnap
    {
        get => _inner.TextPixelSnap;
        set
        {
            Record(RenderCommandKind.SetTextPixelSnap, flags: BooleanFlag(value));
            _inner.TextPixelSnap = value;
        }
    }

    public void BeginFrame(IRenderTarget target)
    {
        Slot?.Reject("a visual opened a frame inside its content");
        _inner.BeginFrame(target);
    }

    public void EndFrame()
    {
        Slot?.Reject("a visual ended the frame inside its content");
        _inner.EndFrame();
    }

    public void Clear(Color color)
    {
        Slot?.Reject("a visual cleared the whole surface inside its content");
        _inner.Clear(color);
    }

    public void Save()
    {
        Record(RenderCommandKind.Save);
        _inner.Save();
    }

    public void Restore()
    {
        Record(RenderCommandKind.Restore);
        _inner.Restore();
    }

    public void SetClip(Rect rect)
    {
        Record(RenderCommandKind.SetClip, rect);
        _inner.SetClip(rect);
    }

    public void BeginOpaqueBackdrop()
    {
        Record(RenderCommandKind.BeginOpaqueBackdrop);
        _inner.BeginOpaqueBackdrop();
    }

    public void EndOpaqueBackdrop()
    {
        Record(RenderCommandKind.EndOpaqueBackdrop);
        _inner.EndOpaqueBackdrop();
    }

    public void BeginOpacity(double opacity)
    {
        Record(RenderCommandKind.BeginOpacity, values: [opacity]);
        _inner.BeginOpacity(opacity);
    }

    public void EndOpacity()
    {
        Record(RenderCommandKind.EndOpacity);
        _inner.EndOpacity();
    }

    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY)
    {
        Record(RenderCommandKind.SetClipRoundedRect, rect, values: [radiusX, radiusY]);
        _inner.SetClipRoundedRect(rect, radiusX, radiusY);
    }

    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY, double borderThickness)
    {
        Record(
            RenderCommandKind.SetClipRoundedRect,
            rect,
            flags: RenderCommandFlags.UsesBooleanOverload,
            values: [radiusX, radiusY, borderThickness]);
        _inner.SetClipRoundedRect(rect, radiusX, radiusY, borderThickness);
    }

    public void SetClipPath(PathGeometry path)
    {
        Record(RenderCommandKind.SetClipPath, path.GetBounds(), GeometryPolicy(path), resource: path);
        _inner.SetClipPath(path);
    }

    public void Translate(double dx, double dy)
    {
        Record(RenderCommandKind.Translate, values: [dx, dy]);
        _inner.Translate(dx, dy);
    }

    public void Rotate(double angleRadians)
    {
        Record(RenderCommandKind.Rotate, values: [angleRadians]);
        _inner.Rotate(angleRadians);
    }

    public void Scale(double sx, double sy)
    {
        Record(RenderCommandKind.Scale, values: [sx, sy]);
        _inner.Scale(sx, sy);
    }

    public void SetTransform(Matrix3x2 matrix)
    {
        Record(
            RenderCommandKind.SetTransform,
            values: [matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32]);
        _inner.SetTransform(matrix);
    }

    public Matrix3x2 GetTransform() => _inner.GetTransform();

    public void ResetTransform()
    {
        Record(RenderCommandKind.ResetTransform);
        _inner.ResetTransform();
    }

    public void ResetClip()
    {
        Record(RenderCommandKind.ResetClip);
        _inner.ResetClip();
    }

    public void IntersectClip(Rect rect)
    {
        Record(RenderCommandKind.IntersectClip, rect);
        _inner.IntersectClip(rect);
    }

    public Rect? GetClipBoundsLocal() => _inner.GetClipBoundsLocal();

    public void DrawLine(Point start, Point end, Color color, double thickness = 1)
    {
        Record(
            RenderCommandKind.DrawLine,
            LineBounds(start, end, thickness),
            color: color,
            values: [start.X, start.Y, end.X, end.Y, thickness]);
        if (Draws)
        {
            _inner.DrawLine(start, end, color, thickness);
        }
    }

    public void DrawLine(Point start, Point end, Color color, double thickness, bool pixelSnap)
    {
        Record(
            RenderCommandKind.DrawLine,
            LineBounds(start, end, thickness),
            flags: RenderCommandFlags.UsesBooleanOverload | BooleanFlag(pixelSnap),
            color: color,
            values: [start.X, start.Y, end.X, end.Y, thickness]);
        if (Draws)
        {
            _inner.DrawLine(start, end, color, thickness, pixelSnap);
        }
    }

    public void DrawLine(Point start, Point end, Pen pen)
    {
        Record(
            RenderCommandKind.DrawLine,
            LineBounds(start, end, pen.Thickness),
            RenderResourcePolicy.ImmutableDescriptor,
            paint: pen,
            values: [start.X, start.Y, end.X, end.Y]);
        if (Draws)
        {
            _inner.DrawLine(start, end, pen);
        }
    }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    {
        Record(RenderCommandKind.DrawRectangle, rect, color: color, values: [thickness], ink: StrokeBounds(rect, thickness));
        if (Draws)
        {
            _inner.DrawRectangle(rect, color, thickness);
        }
    }

    public void DrawRectangle(Rect rect, Color color, double thickness, bool strokeInset)
    {
        Record(
            RenderCommandKind.DrawRectangle,
            rect,
            flags: RenderCommandFlags.UsesBooleanOverload | BooleanFlag(strokeInset),
            color: color,
            values: [thickness],
            ink: StrokeBounds(rect, thickness, strokeInset));
        if (Draws)
        {
            _inner.DrawRectangle(rect, color, thickness, strokeInset);
        }
    }

    public void DrawRectangle(Rect rect, Pen pen)
    {
        Record(RenderCommandKind.DrawRectangle, rect, RenderResourcePolicy.ImmutableDescriptor, paint: pen, ink: StrokeBounds(rect, pen.Thickness));
        if (Draws)
        {
            _inner.DrawRectangle(rect, pen);
        }
    }

    public void FillRectangle(Rect rect, Color color)
    {
        Record(RenderCommandKind.FillRectangle, rect, color: color);
        if (Draws)
        {
            _inner.FillRectangle(rect, color);
        }
    }

    public void FillRectangle(Rect rect, Brush brush)
    {
        Record(RenderCommandKind.FillRectangle, rect, RenderResourcePolicy.ImmutableDescriptor, paint: brush);
        if (Draws)
        {
            _inner.FillRectangle(rect, brush);
        }
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        Record(
            RenderCommandKind.DrawRoundedRectangle,
            rect,
            color: color,
            values: [radiusX, radiusY, thickness],
            ink: StrokeBounds(rect, thickness));
        if (Draws)
        {
            _inner.DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness);
        }
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness, bool strokeInset)
    {
        Record(
            RenderCommandKind.DrawRoundedRectangle,
            rect,
            flags: RenderCommandFlags.UsesBooleanOverload | BooleanFlag(strokeInset),
            color: color,
            values: [radiusX, radiusY, thickness],
            ink: StrokeBounds(rect, thickness, strokeInset));
        if (Draws)
        {
            _inner.DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness, strokeInset);
        }
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Pen pen)
    {
        Record(
            RenderCommandKind.DrawRoundedRectangle,
            rect,
            RenderResourcePolicy.ImmutableDescriptor,
            paint: pen,
            values: [radiusX, radiusY],
            ink: StrokeBounds(rect, pen.Thickness));
        if (Draws)
        {
            _inner.DrawRoundedRectangle(rect, radiusX, radiusY, pen);
        }
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        Record(RenderCommandKind.FillRoundedRectangle, rect, color: color, values: [radiusX, radiusY]);
        if (Draws)
        {
            _inner.FillRoundedRectangle(rect, radiusX, radiusY, color);
        }
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Brush brush)
    {
        Record(
            RenderCommandKind.FillRoundedRectangle,
            rect,
            RenderResourcePolicy.ImmutableDescriptor,
            paint: brush,
            values: [radiusX, radiusY]);
        if (Draws)
        {
            _inner.FillRoundedRectangle(rect, radiusX, radiusY, brush);
        }
    }

    public void DrawEllipse(Rect bounds, Color color, double thickness = 1)
    {
        Record(RenderCommandKind.DrawEllipse, bounds, color: color, values: [thickness], ink: StrokeBounds(bounds, thickness));
        if (Draws)
        {
            _inner.DrawEllipse(bounds, color, thickness);
        }
    }

    public void DrawEllipse(Rect bounds, Color color, double thickness, bool strokeInset)
    {
        Record(
            RenderCommandKind.DrawEllipse,
            bounds,
            flags: RenderCommandFlags.UsesBooleanOverload | BooleanFlag(strokeInset),
            color: color,
            values: [thickness],
            ink: StrokeBounds(bounds, thickness, strokeInset));
        if (Draws)
        {
            _inner.DrawEllipse(bounds, color, thickness, strokeInset);
        }
    }

    public void DrawEllipse(Rect bounds, Pen pen)
    {
        Record(RenderCommandKind.DrawEllipse, bounds, RenderResourcePolicy.ImmutableDescriptor, paint: pen, ink: StrokeBounds(bounds, pen.Thickness));
        if (Draws)
        {
            _inner.DrawEllipse(bounds, pen);
        }
    }

    public void FillEllipse(Rect bounds, Color color)
    {
        Record(RenderCommandKind.FillEllipse, bounds, color: color);
        if (Draws)
        {
            _inner.FillEllipse(bounds, color);
        }
    }

    public void FillEllipse(Rect bounds, Brush brush)
    {
        Record(RenderCommandKind.FillEllipse, bounds, RenderResourcePolicy.ImmutableDescriptor, paint: brush);
        if (Draws)
        {
            _inner.FillEllipse(bounds, brush);
        }
    }

    public void DrawPath(PathGeometry path, Color color, double thickness = 1)
    {
        Record(
            RenderCommandKind.DrawPath,
            path.GetBounds(),
            GeometryPolicy(path),
            color: color,
            resource: path,
            values: [thickness],
            ink: StrokeBounds(path.GetBounds(), thickness));
        if (Draws)
        {
            _inner.DrawPath(path, color, thickness);
        }
    }

    public void DrawPath(PathGeometry path, Pen pen)
    {
        Record(
            RenderCommandKind.DrawPath,
            path.GetBounds(),
            GeometryPolicy(path) | RenderResourcePolicy.ImmutableDescriptor,
            resource: path,
            paint: pen,
            ink: StrokeBounds(path.GetBounds(), pen.Thickness));
        if (Draws)
        {
            _inner.DrawPath(path, pen);
        }
    }

    public void FillPath(PathGeometry path, Color color)
    {
        Record(RenderCommandKind.FillPath, path.GetBounds(), GeometryPolicy(path), color: color, resource: path);
        if (Draws)
        {
            _inner.FillPath(path, color);
        }
    }

    public void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        Record(
            RenderCommandKind.FillPath,
            path.GetBounds(),
            GeometryPolicy(path),
            flags: RenderCommand.Encode(fillRule),
            color: color,
            resource: path);
        if (Draws)
        {
            _inner.FillPath(path, color, fillRule);
        }
    }

    public void FillPath(PathGeometry path, Brush brush)
    {
        Record(
            RenderCommandKind.FillPath,
            path.GetBounds(),
            GeometryPolicy(path) | RenderResourcePolicy.ImmutableDescriptor,
            resource: path,
            paint: brush);
        if (Draws)
        {
            _inner.FillPath(path, brush);
        }
    }

    public void FillPath(PathGeometry path, Brush brush, FillRule fillRule)
    {
        Record(
            RenderCommandKind.FillPath,
            path.GetBounds(),
            GeometryPolicy(path) | RenderResourcePolicy.ImmutableDescriptor,
            flags: RenderCommand.Encode(fillRule),
            resource: path,
            paint: brush);
        if (Draws)
        {
            _inner.FillPath(path, brush, fillRule);
        }
    }

    public void DrawBoxShadow(Rect bounds, double cornerRadius, double blurRadius, Color shadowColor, double offsetX = 0, double offsetY = 0)
    {
        double expansion = Math.Max(0, blurRadius) * 0.5;
        var shadowBounds = new Rect(
            bounds.X + offsetX - expansion,
            bounds.Y + offsetY - expansion,
            bounds.Width + expansion * 2,
            bounds.Height + expansion * 2);
        Record(
            RenderCommandKind.DrawBoxShadow,
            shadowBounds,
            color: shadowColor,
            values: [cornerRadius, blurRadius, offsetX, offsetY, bounds.X, bounds.Y, bounds.Width, bounds.Height]);
        if (Draws)
        {
            _inner.DrawBoxShadow(bounds, cornerRadius, blurRadius, shadowColor, offsetX, offsetY);
        }
    }

    public void DrawImage(IImage image, Point location)
    {
        Record(
            RenderCommandKind.DrawImage,
            new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight),
            RenderResourcePolicy.ImageLeaseRequired,
            flags: RenderCommand.Encode(RenderImageVariant.Location),
            resource: image);
        if (Draws)
        {
            _inner.DrawImage(image, location);
        }
    }

    public void DrawImage(IImage image, Rect destRect)
    {
        Record(
            RenderCommandKind.DrawImage,
            destRect,
            RenderResourcePolicy.ImageLeaseRequired,
            flags: RenderCommand.Encode(RenderImageVariant.Destination),
            resource: image);
        if (Draws)
        {
            _inner.DrawImage(image, destRect);
        }
    }

    public void DrawImage(IImage image, Rect destRect, Rect sourceRect)
    {
        Record(
            RenderCommandKind.DrawImage,
            destRect,
            RenderResourcePolicy.ImageLeaseRequired,
            flags: RenderCommand.Encode(RenderImageVariant.DestinationAndSource),
            resource: image,
            values: [sourceRect.X, sourceRect.Y, sourceRect.Width, sourceRect.Height]);
        if (Draws)
        {
            _inner.DrawImage(image, destRect, sourceRect);
        }
    }

    public void Dispose()
    {
        // The inner context belongs to the frame, not to this wrapper.
    }

    private void Record(
        RenderCommandKind kind,
        Rect bounds = default,
        RenderResourcePolicy resourcePolicy = RenderResourcePolicy.Value,
        RenderCommandFlags flags = RenderCommandFlags.None,
        Color color = default,
        object? resource = null,
        object? paint = null,
        ReadOnlySpan<double> values = default,
        Rect ink = default)
        => Slot?.Add(kind, bounds, resourcePolicy, flags, color, resource, paint, values, ink);

    private static RenderCommandFlags BooleanFlag(bool value)
        => value ? RenderCommandFlags.BooleanValue : RenderCommandFlags.None;

    private static RenderResourcePolicy GeometryPolicy(PathGeometry path)
        => path.IsFrozen ? RenderResourcePolicy.FrozenGeometry : RenderResourcePolicy.Value;

    /// <summary>
    /// The area a stroked shape actually inks. A centred stroke puts half its width outside the
    /// shape, and a frame that repaints only part of the surface has to redraw that ink too.
    /// </summary>
    private static Rect StrokeBounds(Rect shape, double thickness, bool strokeInset = false)
    {
        if (strokeInset)
        {
            return shape;
        }

        double overhang = Math.Max(0, thickness) * 0.5;
        return new Rect(
            shape.X - overhang,
            shape.Y - overhang,
            shape.Width + overhang * 2,
            shape.Height + overhang * 2);
    }

    private static Rect LineBounds(Point start, Point end, double thickness)
    {
        double halfThickness = Math.Max(0, thickness) * 0.5;
        return new Rect(
            Math.Min(start.X, end.X) - halfThickness,
            Math.Min(start.Y, end.Y) - halfThickness,
            Math.Abs(end.X - start.X) + halfThickness * 2,
            Math.Abs(end.Y - start.Y) + halfThickness * 2);
    }

    private sealed class RecordingTextContext(RenderDataRecorder recorder, ITextRenderContext inner) : ITextRenderContext
    {
        public IGraphicsContext Graphics => recorder;

        public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options)
        {
            AddTextCommand(RenderCommandKind.DrawText, layout, origin, in options);
            if (recorder.Draws)
            {
                inner.Draw(layout, origin, in options);
            }
        }

        public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options)
        {
            AddTextCommand(RenderCommandKind.DrawTextBackground, layout, origin, in options);
            if (recorder.Draws)
            {
                inner.DrawBackground(layout, origin, in options);
            }
        }

        public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options)
        {
            AddTextCommand(RenderCommandKind.DrawTextForeground, layout, origin, in options);
            if (recorder.Draws)
            {
                inner.DrawForeground(layout, origin, in options);
            }
        }

        private void AddTextCommand(RenderCommandKind kind, ITextLayout layout, Point origin, in TextDrawOptions options)
        {
            recorder.Slot?.AddText(
                kind,
                new Rect(origin.X, origin.Y, layout.MeasuredSize.Width, layout.MeasuredSize.Height),
                layout,
                in options);
        }
    }
}
