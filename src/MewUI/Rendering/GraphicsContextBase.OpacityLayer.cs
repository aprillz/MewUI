using System.Numerics;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// The opacity layer of a context that cannot blend a group itself: <see cref="BeginOpacity"/> opens a
/// surface of its own for the scope, and every call made through <see cref="IGraphicsContext"/> until
/// the matching <see cref="EndOpacity"/> draws there, so the scope fades once as a whole.
/// </summary>
internal abstract partial class GraphicsContextBase
{
    // Scopes opened on this context, innermost last: a layer, or null for one that fades call by call.
    private Stack<OpacityGroup?>? _opacityScopes;

    // The context of the open layer, which the calls made through the interface go to while it is open.
    private IGraphicsContext? _layerTarget;

    // Scopes opened inside the open layer, which close there before the layer itself does.
    private int _forwardedOpacityDepth;

    /// <summary>
    /// The factory a layer takes its surface from. Without one, a scope multiplies its opacity into each
    /// call, which differs where the scope's drawing overlaps itself.
    /// </summary>
    internal IGraphicsFactory? OpacityLayerFactory { get; set; }

    /// <summary>
    /// Opens a scope whose drawing fades as one group: on a surface of its own when this context has a
    /// <see cref="OpacityLayerFactory"/>, otherwise by multiplying the opacity into each call.
    /// </summary>
    public virtual void BeginOpacity(double opacity)
    {
        var factory = OpacityLayerFactory;
        var reach = OpacityLayerReach();
        if (factory != null && IsActive && reach.Width > 0 && reach.Height > 0)
        {
            var group = OpacityGroup.Begin(this, factory, opacity, reach);
            var target = group.Target;
            target.TextPixelSnap = TextPixelSnap;
            target.EnableAlphaTextHint = EnableAlphaTextHint;
            (_opacityScopes ??= new Stack<OpacityGroup?>()).Push(group);
            _layerTarget = target;
            _forwardedOpacityDepth = 0;
            return;
        }

        Save();
        GlobalAlpha *= (float)Math.Clamp(opacity, 0.0, 1.0);
        (_opacityScopes ??= new Stack<OpacityGroup?>()).Push(null);
    }

    /// <summary>Ends the innermost <see cref="BeginOpacity"/> scope, putting a layer onto this context at its opacity.</summary>
    public virtual void EndOpacity()
    {
        if (_opacityScopes == null || _opacityScopes.Count == 0)
        {
            // Unmatched, as before layers: the Restore guards against an unbalanced stack itself.
            Restore();
            return;
        }

        var scope = _opacityScopes.Pop();
        if (scope.HasValue)
        {
            _layerTarget = null;
            _forwardedOpacityDepth = 0;
            scope.Value.End();
        }
        else
        {
            Restore();
        }
    }

    /// <summary>Closes the scopes a frame left open, so their layers reach the target before it ends.</summary>
    private void CloseOpacityScopes()
    {
        while (_opacityScopes != null && _opacityScopes.Count > 0)
        {
            EndOpacity();
        }
    }

    /// <summary>What a layer can draw on, in the coordinates of the surface this context's drawing is meant for.</summary>
    private Rect OpacityLayerReach()
    {
        double scale = DpiScale > 0 ? DpiScale : 1.0;
        var frame = new Rect(0, 0, FramePixelWidth / scale, FramePixelHeight / scale);
        if (_hasRootTransform)
        {
            frame = TransformRect(frame, _rootTransformInverse);
        }

        if (_cullRect.Equals(InfiniteCullRect))
        {
            return frame;
        }

        return frame.Intersect(TransformRect(_cullRect, GetTransform()));
    }

    void IGraphicsContext.BeginOpacity(double opacity)
    {
        var layer = _layerTarget;
        if (layer != null)
        {
            _forwardedOpacityDepth++;
            layer.BeginOpacity(opacity);
        }
        else
        {
            BeginOpacity(opacity);
        }
    }

    void IGraphicsContext.EndOpacity()
    {
        var layer = _layerTarget;
        if (layer != null && _forwardedOpacityDepth > 0)
        {
            _forwardedOpacityDepth--;
            layer.EndOpacity();
        }
        else
        {
            EndOpacity();
        }
    }

    ITextRenderContext IGraphicsContext.Text => _layerTarget?.Text ?? Text;

    bool IGraphicsContext.EnableAlphaTextHint
    {
        get => _layerTarget?.EnableAlphaTextHint ?? EnableAlphaTextHint;
        set
        {
            var layer = _layerTarget;
            if (layer != null)
            {
                layer.EnableAlphaTextHint = value;
            }
            else
            {
                EnableAlphaTextHint = value;
            }
        }
    }

    float IGraphicsContext.GlobalAlpha
    {
        get => _layerTarget?.GlobalAlpha ?? GlobalAlpha;
        set
        {
            var layer = _layerTarget;
            if (layer != null)
            {
                layer.GlobalAlpha = value;
            }
            else
            {
                GlobalAlpha = value;
            }
        }
    }

    bool IGraphicsContext.TextPixelSnap
    {
        get => _layerTarget?.TextPixelSnap ?? TextPixelSnap;
        set
        {
            var layer = _layerTarget;
            if (layer != null)
            {
                layer.TextPixelSnap = value;
            }
            else
            {
                TextPixelSnap = value;
            }
        }
    }

    ImageScaleQuality IGraphicsContext.ImageScaleQuality
    {
        get => _layerTarget?.ImageScaleQuality ?? ImageScaleQuality;
        set
        {
            var layer = _layerTarget;
            if (layer != null)
            {
                layer.ImageScaleQuality = value;
            }
            else
            {
                ImageScaleQuality = value;
            }
        }
    }

    void IGraphicsContext.Save() => Forward(static layer => layer.Save(), static self => self.Save());

    void IGraphicsContext.Restore() => Forward(static layer => layer.Restore(), static self => self.Restore());

    void IGraphicsContext.SetClip(Rect rect)
    {
        var layer = _layerTarget;
        if (layer != null) layer.SetClip(rect); else SetClip(rect);
    }

    void IGraphicsContext.BeginOpaqueBackdrop() => Forward(static layer => layer.BeginOpaqueBackdrop(), static self => self.BeginOpaqueBackdrop());

    void IGraphicsContext.EndOpaqueBackdrop() => Forward(static layer => layer.EndOpaqueBackdrop(), static self => self.EndOpaqueBackdrop());

    void IGraphicsContext.SetClipRoundedRect(Rect rect, double radiusX, double radiusY)
    {
        var layer = _layerTarget;
        if (layer != null) layer.SetClipRoundedRect(rect, radiusX, radiusY); else SetClipRoundedRect(rect, radiusX, radiusY);
    }

    void IGraphicsContext.SetClipRoundedRect(Rect rect, double radiusX, double radiusY, double borderThickness)
    {
        var layer = _layerTarget;
        if (layer != null) layer.SetClipRoundedRect(rect, radiusX, radiusY, borderThickness); else SetClipRoundedRect(rect, radiusX, radiusY, borderThickness);
    }

    void IGraphicsContext.SetClipPath(PathGeometry path)
    {
        var layer = _layerTarget;
        if (layer != null) layer.SetClipPath(path); else SetClipPath(path);
    }

    void IGraphicsContext.Translate(double dx, double dy)
    {
        var layer = _layerTarget;
        if (layer != null) layer.Translate(dx, dy); else Translate(dx, dy);
    }

    void IGraphicsContext.Rotate(double angleRadians)
    {
        var layer = _layerTarget;
        if (layer != null) layer.Rotate(angleRadians); else Rotate(angleRadians);
    }

    void IGraphicsContext.Scale(double sx, double sy)
    {
        var layer = _layerTarget;
        if (layer != null) layer.Scale(sx, sy); else Scale(sx, sy);
    }

    void IGraphicsContext.SetTransform(Matrix3x2 matrix)
    {
        var layer = _layerTarget;
        if (layer != null) layer.SetTransform(matrix); else SetTransform(matrix);
    }

    Matrix3x2 IGraphicsContext.GetTransform() => _layerTarget?.GetTransform() ?? GetTransform();

    void IGraphicsContext.ResetTransform() => Forward(static layer => layer.ResetTransform(), static self => self.ResetTransform());

    void IGraphicsContext.ResetClip() => Forward(static layer => layer.ResetClip(), static self => self.ResetClip());

    void IGraphicsContext.IntersectClip(Rect rect)
    {
        var layer = _layerTarget;
        if (layer != null) layer.IntersectClip(rect); else IntersectClip(rect);
    }

    Rect? IGraphicsContext.GetClipBoundsLocal()
    {
        var layer = _layerTarget;
        if (layer != null)
        {
            return layer.GetClipBoundsLocal();
        }
        else
        {
            return GetClipBoundsLocal();
        }
    }

    void IGraphicsContext.Clear(Color color)
    {
        var layer = _layerTarget;
        if (layer != null) layer.Clear(color); else Clear(color);
    }

    void IGraphicsContext.DrawLine(Point start, Point end, Color color, double thickness)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawLine(start, end, color, thickness); else DrawLine(start, end, color, thickness);
    }

    void IGraphicsContext.DrawLine(Point start, Point end, Color color, double thickness, bool pixelSnap)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawLine(start, end, color, thickness, pixelSnap); else DrawLine(start, end, color, thickness, pixelSnap);
    }

    void IGraphicsContext.DrawLine(Point start, Point end, Pen pen)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawLine(start, end, pen); else DrawLine(start, end, pen);
    }

    void IGraphicsContext.DrawRectangle(Rect rect, Color color, double thickness)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawRectangle(rect, color, thickness); else DrawRectangle(rect, color, thickness);
    }

    void IGraphicsContext.DrawRectangle(Rect rect, Color color, double thickness, bool strokeInset)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawRectangle(rect, color, thickness, strokeInset); else DrawRectangle(rect, color, thickness, strokeInset);
    }

    void IGraphicsContext.DrawRectangle(Rect rect, Pen pen)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawRectangle(rect, pen); else DrawRectangle(rect, pen);
    }

    void IGraphicsContext.FillRectangle(Rect rect, Color color)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillRectangle(rect, color); else FillRectangle(rect, color);
    }

    void IGraphicsContext.FillRectangle(Rect rect, Brush brush)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillRectangle(rect, brush); else FillRectangle(rect, brush);
    }

    void IGraphicsContext.DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness); else DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness);
    }

    void IGraphicsContext.DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness, bool strokeInset)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness, strokeInset); else DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness, strokeInset);
    }

    void IGraphicsContext.DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Pen pen)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawRoundedRectangle(rect, radiusX, radiusY, pen); else DrawRoundedRectangle(rect, radiusX, radiusY, pen);
    }

    void IGraphicsContext.FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillRoundedRectangle(rect, radiusX, radiusY, color); else FillRoundedRectangle(rect, radiusX, radiusY, color);
    }

    void IGraphicsContext.FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Brush brush)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillRoundedRectangle(rect, radiusX, radiusY, brush); else FillRoundedRectangle(rect, radiusX, radiusY, brush);
    }

    void IGraphicsContext.DrawEllipse(Rect bounds, Color color, double thickness)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawEllipse(bounds, color, thickness); else DrawEllipse(bounds, color, thickness);
    }

    void IGraphicsContext.DrawEllipse(Rect bounds, Color color, double thickness, bool strokeInset)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawEllipse(bounds, color, thickness, strokeInset); else DrawEllipse(bounds, color, thickness, strokeInset);
    }

    void IGraphicsContext.DrawEllipse(Rect bounds, Pen pen)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawEllipse(bounds, pen); else DrawEllipse(bounds, pen);
    }

    void IGraphicsContext.FillEllipse(Rect bounds, Color color)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillEllipse(bounds, color); else FillEllipse(bounds, color);
    }

    void IGraphicsContext.FillEllipse(Rect bounds, Brush brush)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillEllipse(bounds, brush); else FillEllipse(bounds, brush);
    }

    void IGraphicsContext.DrawPath(PathGeometry path, Color color, double thickness)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawPath(path, color, thickness); else DrawPath(path, color, thickness);
    }

    void IGraphicsContext.DrawPath(PathGeometry path, Pen pen)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawPath(path, pen); else DrawPath(path, pen);
    }

    void IGraphicsContext.FillPath(PathGeometry path, Color color)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillPath(path, color); else FillPath(path, color);
    }

    void IGraphicsContext.FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillPath(path, color, fillRule); else FillPath(path, color, fillRule);
    }

    void IGraphicsContext.FillPath(PathGeometry path, Brush brush)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillPath(path, brush); else FillPath(path, brush);
    }

    void IGraphicsContext.FillPath(PathGeometry path, Brush brush, FillRule fillRule)
    {
        var layer = _layerTarget;
        if (layer != null) layer.FillPath(path, brush, fillRule); else FillPath(path, brush, fillRule);
    }

    void IGraphicsContext.DrawBoxShadow(Rect bounds, double cornerRadius, double blurRadius, Color shadowColor, double offsetX, double offsetY)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawBoxShadow(bounds, cornerRadius, blurRadius, shadowColor, offsetX, offsetY); else DrawBoxShadow(bounds, cornerRadius, blurRadius, shadowColor, offsetX, offsetY);
    }

    void IGraphicsContext.DrawImage(IImage image, Point location)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawImage(image, location); else DrawImage(image, location);
    }

    void IGraphicsContext.DrawImage(IImage image, Rect destRect)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawImage(image, destRect); else DrawImage(image, destRect);
    }

    void IGraphicsContext.DrawImage(IImage image, Rect destRect, Rect sourceRect)
    {
        var layer = _layerTarget;
        if (layer != null) layer.DrawImage(image, destRect, sourceRect); else DrawImage(image, destRect, sourceRect);
    }

    private void Forward(Action<IGraphicsContext> toLayer, Action<GraphicsContextBase> toSelf)
    {
        var layer = _layerTarget;
        if (layer != null)
        {
            toLayer(layer);
        }
        else
        {
            toSelf(this);
        }
    }
}
