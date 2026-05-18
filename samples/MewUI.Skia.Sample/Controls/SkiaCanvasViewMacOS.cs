using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Skia.Sample.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Sample.Controls;

/// <summary>
/// macOS Skia host. Skia GR Metal shares the system <c>MTLDevice</c> with MewVG's Metal
/// backend so the produced <c>MTLTexture</c> is sample-able by MewVG without a cross-resource
/// copy. Demotes to the base CPU path on any other backend.
/// </summary>
public sealed class SkiaCanvasViewMacOS : SkiaCanvasView
{
    private const string MewVGMacOSBackendName = "MewVG.MacOS";

    private SkiaMetalSurfaceHost? _metalHost;
    private bool _resolved;

    public override string PathDescription => _metalHost is not null
        ? "GPU zero-copy (Skia Metal → MewVG Metal)"
        : (IsGpuPath ? "Pending" : "CPU upload (Skia → byte[] → backend)");

    protected override bool TryRenderGpu(IGraphicsContext context, int width, int height)
    {
        if (!_resolved)
        {
            _resolved = true;
            var factory = GetGraphicsFactory();
            if (factory.Backend.Equals(MewVGMacOSBackendName, StringComparison.OrdinalIgnoreCase))
            {
                _metalHost = new SkiaMetalSurfaceHost(factory);
            }
        }

        if (_metalHost is null) return false;

        try
        {
            if (!_metalHost.EnsureSurface(width, height)) return false;

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var image = _metalHost.Paint(surface =>
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
        _metalHost?.Dispose();
        _metalHost = null;
    }

    protected override void OnGpuInteropInvalidatedCore(GpuInteropInvalidatedEventArgs e)
    {
        _resolved = false;
    }
}
