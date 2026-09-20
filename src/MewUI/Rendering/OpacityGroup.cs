using System.Numerics;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Marks a context whose opacity scope blends what is drawn inside it once, as a group. A context
/// without it multiplies the opacity into every drawing call, which shows wherever two of them overlap.
/// </summary>
internal interface IGroupOpacityContext
{
}

/// <summary>
/// Fades what is drawn between <see cref="Begin"/> and <see cref="End"/> as one group. A context that
/// blends groups itself is used as it is; any other gets a surface of its own for the group, which is
/// drawn back onto the context at the group's opacity.
/// </summary>
internal struct OpacityGroup
{
    private IGraphicsContext _outer;
    private IGraphicsFactory? _factory;
    private IRenderSurface? _surface;
    private IGraphicsContext? _inner;
    private Rect _placement;
    private int _pixelWidth;
    private int _pixelHeight;
    private double _opacity;

    /// <summary>The context the group was opened on.</summary>
    internal readonly IGraphicsContext Outer => _outer;

    /// <summary>The context to draw the group's content into.</summary>
    internal IGraphicsContext Target { get; private set; }

    /// <summary>
    /// Opens a group. <paramref name="reach"/> is everything the group can draw on, in the coordinates
    /// of the surface <paramref name="context"/> draws to; what falls outside it is lost.
    /// </summary>
    internal static OpacityGroup Begin(IGraphicsContext context, IGraphicsFactory? factory, double opacity, Rect reach)
    {
        var group = new OpacityGroup { _outer = context, Target = context, _opacity = opacity };
        if (context is IGroupOpacityContext || factory == null || context is not GraphicsContextBase)
        {
            context.BeginOpacity(opacity);
            return group;
        }

        double scale = context.DpiScale;
        double left = Math.Floor(reach.X * scale) / scale;
        double top = Math.Floor(reach.Y * scale) / scale;
        int pixelWidth = (int)Math.Ceiling((reach.Right - left) * scale);
        int pixelHeight = (int)Math.Ceiling((reach.Bottom - top) * scale);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            // Nothing of the group reaches the surface, and the scope still has to pair with End.
            context.BeginOpacity(opacity);
            return group;
        }

        var surface = factory.AcquireScratchSurface(pixelWidth, pixelHeight, scale, hasAlpha: true, debugName: "OpacityGroup");
        IGraphicsContext? inner = null;
        try
        {
            inner = factory.CreateContext(surface);
            inner.BeginFrame(surface);
            inner.Clear(Color.Transparent);
            if (inner is GraphicsContextBase innerBase)
            {
                innerBase.SetRootTransform(Matrix3x2.CreateTranslation((float)-left, (float)-top));
            }
            else
            {
                inner.Translate(-left, -top);
            }

            inner.SetTransform(context.GetTransform());
            inner.ImageScaleQuality = context.ImageScaleQuality;
        }
        catch
        {
            inner?.Dispose();
            factory.ReleaseScratchSurface(surface);
            throw;
        }

        group._factory = factory;
        group._surface = surface;
        group._inner = inner;
        group._placement = new Rect(left, top, pixelWidth / scale, pixelHeight / scale);
        group._pixelWidth = pixelWidth;
        group._pixelHeight = pixelHeight;
        group.Target = inner;
        return group;
    }

    /// <summary>Closes the group and puts it on the context it was opened on.</summary>
    internal void End()
    {
        if (_inner == null || _surface == null || _factory == null)
        {
            _outer.EndOpacity();
            return;
        }

        var factory = _factory;
        var surface = _surface;
        IImage? image = null;
        try
        {
            _inner.EndFrame();
            _inner.Dispose();
            _inner = null;

            image = factory.CreateImageView(surface);
            _outer.Save();
            try
            {
                _outer.SetTransform(Matrix3x2.Identity);
                _outer.GlobalAlpha *= (float)Math.Clamp(_opacity, 0.0, 1.0);
                _outer.DrawImage(image, _placement, new Rect(0, 0, _pixelWidth, _pixelHeight));
            }
            finally
            {
                _outer.Restore();
            }
        }
        finally
        {
            _inner?.Dispose();
            _surface = null;

            // The context may not have drawn the group yet, so the surface cannot go back for reuse before its frame ends.
            var drawnWith = image;
            ((GraphicsContextBase)_outer).ReleaseWhenFrameEnds(() =>
            {
                drawnWith?.Dispose();
                factory.ReleaseScratchSurface(surface);
            });
        }
    }
}
