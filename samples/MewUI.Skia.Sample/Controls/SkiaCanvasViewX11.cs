using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Skia.Sample.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Sample.Controls;

/// <summary>
/// X11 Skia host. When the active backend is MewVG.X11, Skia renders into a GL FBO whose
/// color texture is sampled by MewVG within the same GL context — no readback. Demotes to
/// the base CPU path on any other backend.
/// </summary>
public sealed class SkiaCanvasViewX11 : SkiaCanvasView
{
    private const string MewVGX11BackendName = "MewVG.X11";

    private SkiaGLSurfaceHost? _glHost;
    private bool _resolved;

    public override string PathDescription => _glHost is not null
        ? "GPU zero-copy (Skia GL → MewVG GL)"
        : (IsGpuPath ? "Pending" : "CPU upload (Skia → byte[] → backend)");

    protected override bool TryRenderGpu(IGraphicsContext context, int width, int height)
    {
        if (!_resolved)
        {
            _resolved = true;
            var factory = GetGraphicsFactory();
            if (factory.Backend.Equals(MewVGX11BackendName, StringComparison.OrdinalIgnoreCase))
            {
                _glHost = new SkiaGLSurfaceHost(factory);
            }
        }

        if (_glHost is null) return false;

        try
        {
            if (!_glHost.EnsureSurface(width, height)) return false;

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var image = _glHost.Paint(surface =>
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                InvokePaint(canvas, info);
            });

            if (image is null) return false;
            context.DrawImage(image, Bounds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override void DisposeGpu()
    {
        _glHost?.Dispose();
        _glHost = null;
    }

    protected override void OnGpuInteropInvalidatedCore(GpuInteropInvalidatedEventArgs e)
    {
        _resolved = false;
    }
}
