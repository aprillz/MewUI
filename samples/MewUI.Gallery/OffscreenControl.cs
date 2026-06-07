using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

public abstract class OffscreenControl : Control
{
    private IRenderSurface? _surface;
    private IGraphicsContext? _context;
    private IImage? _view;

    private int _surfaceWidth, _surfaceHeight;
    private double _surfaceDpiScale;
    private bool _dirty = true;

    protected abstract void RenderOffscreen(IGraphicsContext context, Rect bounds);

    protected void InvalidateOffscreen()
    {
        _dirty = true;
        InvalidateVisual();
    }

    private void EnsureSurface()
    {
        double dpiScale = GetDpi() / 96.0;
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpiScale));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpiScale));

        if (_surface is not null && _surfaceWidth == pixelWidth && _surfaceHeight == pixelHeight && _surfaceDpiScale == dpiScale)
        {
            return;
        }

        _context?.Dispose();
        _view?.Dispose();
        _surface?.Dispose();

        var factory = GetGraphicsFactory();
        _surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixelWidth, pixelHeight, dpiScale));
        _view = factory.CreateImageView(_surface);
        _context = factory.CreateContext(_surface);

        _surfaceWidth = pixelWidth;
        _surfaceHeight = pixelHeight;
        _surfaceDpiScale = dpiScale;

        _dirty = true;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        EnsureSurface();

        if (_surface is null || _context is null || _view is null)
        {
            return;
        }

        if (_dirty)
        {
            _context.BeginFrame(_surface);
            RenderOffscreen(_context, new Rect(0, 0, ActualWidth, ActualHeight));
            _context.EndFrame();

            _dirty = false;
        }

        context.DrawImage(_view, Bounds);
    }

    protected override void OnDispose()
    {
        base.OnDispose();

        _context?.Dispose();
        _view?.Dispose();
        _surface?.Dispose();
    }
}
