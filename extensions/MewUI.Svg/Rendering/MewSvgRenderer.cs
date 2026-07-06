using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

internal sealed class MewSvgRenderer : ISvgRenderer
{
    private readonly Stack<ISvgBoundable> _boundables = new();

    private int _saveDepth;

    public MewSvgRenderer(IGraphicsFactory graphicsFactory, IGraphicsContext graphicsContext)
    {
        GraphicsFactory = graphicsFactory;
        GraphicsContext = graphicsContext;
    }

    public float DpiY => (float)(GraphicsContext.DpiScale * 96.0);

    public IGraphicsContext GraphicsContext { get; }

    public IGraphicsFactory GraphicsFactory { get; }

    public Matrix3x2 Transform
    {
        get => GraphicsContext.GetTransform();
        set => GraphicsContext.SetTransform(value);
    }

    public float GlobalOpacity
    {
        get => GraphicsContext.GlobalAlpha;
        set => GraphicsContext.GlobalAlpha = Math.Clamp(value, 0f, 1f);
    }

    public void DrawImage(IImage image, Rect destRect, Rect srcRect, float opacity = 1f)
    {
        Save();
        try
        {
            GraphicsContext.GlobalAlpha *= Math.Clamp(opacity, 0f, 1f);
            GraphicsContext.DrawImage(image, destRect, srcRect);
        }
        finally
        {
            Restore();
        }
    }

    public void DrawImageUnscaled(IImage image, Point location, float opacity = 1f)
    {
        Save();
        try
        {
            GraphicsContext.GlobalAlpha *= Math.Clamp(opacity, 0f, 1f);
            GraphicsContext.DrawImage(image, location);
        }
        finally
        {
            Restore();
        }
    }

    public void DrawPath(IPen pen, PathGeometry path)
    {
        GraphicsContext.DrawPath(path, pen);
    }

    public void FillPath(IBrush brush, PathGeometry path)
    {
        GraphicsContext.FillPath(path, brush, path.FillRule);
    }

    public ISvgBoundable GetBoundable()
    {
        return _boundables.Count > 0 ? _boundables.Peek() : GenericBoundable.Empty;
    }

    public ISvgBoundable PopBoundable()
    {
        return _boundables.Count > 0 ? _boundables.Pop() : GenericBoundable.Empty;
    }

    public void SetBoundable(ISvgBoundable boundable)
    {
        _boundables.Push(boundable);
    }

    public void Save()
    {
        GraphicsContext.Save();
        _saveDepth++;
    }

    public void Restore()
    {
        // Mirrors GraphicsContextBase's own empty-stack guard, but at the renderer level:
        // never forward a Restore this renderer didn't Save for, so an unmatched Restore
        // elsewhere in the SVG tree can't consume a Save pushed by an unrelated ancestor.
        if (_saveDepth <= 0)
        {
            return;
        }

        GraphicsContext.Restore();
        _saveDepth--;
    }

    /// <summary>Restores down to this renderer's baseline. Called after a full document
    /// render so a mid-render exception (thrown between some element's Save and its
    /// intended Restore) can't leave pushed state on the context past the Render call.</summary>
    internal void DrainToBaseline()
    {
        while (_saveDepth > 0)
        {
            Restore();
        }
    }

    public void SetClip(Rect rect)
    {
        GraphicsContext.SetClip(rect);
    }

    public void IntersectClip(Rect rect)
    {
        GraphicsContext.IntersectClip(rect);
    }

    public void Dispose()
    {
    }
}
