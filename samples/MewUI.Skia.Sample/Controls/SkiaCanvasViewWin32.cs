using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Skia.Sample.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Sample.Controls;

/// <summary>
/// Windows Skia host. Picks one of two GPU bridges on first render:
/// <list type="bullet">
///   <item><b>GL → MewVG GL</b> when the active backend is MewVG.Win32. Skia renders into a
///         GL FBO whose color texture is sampled by MewVG via
///         <see cref="IExternalRasterSource"/> — same GL context, no readback.</item>
///   <item><b>GL → WGL_NV_DX_interop → D3D11 → D2D</b> for Direct2D only when
///         <c>MEWUI_SKIA_D2D_WGL</c> opts into the experimental bridge. Otherwise Direct2D
///         uses the base CPU path.</item>
/// </list>
/// Any other Windows backend (GDI) demotes to the base CPU path.
/// </summary>
public sealed class SkiaCanvasViewWin32 : SkiaCanvasView
{
    private const string EnableDirect2DWglInteropEnv = "MEWUI_SKIA_D2D_WGL";
    private const string MewVGWin32BackendName = "MewVG.Win32";
    private const string Direct2DBackendName = "Direct2D";

    private enum GpuKind
    { Undetermined, GL, Wgl }

    private GpuKind _gpu = GpuKind.Undetermined;
    private SkiaGLSurfaceHost? _glHost;
    private SkiaWglInteropHost? _wglHost;

    // Per-render-phase timing accumulators (logged every ~1 s) so we can see how much of
    // OnRender's cost lives outside SkiaGLSurfaceHost.Paint — specifically the MewVG sample
    // (context.DrawImage) which is where any cross-adapter / DWM compositor cost would show.
    private long _glEnsureTicks, _glPaintTicks, _glDrawTicks;
    private int _glFrames;
    private long _glLogDeadlineTicks;

    public override string PathDescription => _gpu switch
    {
        GpuKind.GL => "GPU zero-copy (Skia GL → MewVG GL)",
        GpuKind.Wgl => "Experimental GPU zero-copy (Skia GL → WGL_NV_DX_interop → D3D11/D2D)",
        _ => IsGpuPath ? "Pending" : "CPU upload (Skia → byte[] → backend)"
    };

    protected override bool TryRenderGpu(IGraphicsContext context, int width, int height)
    {
        if (_gpu == GpuKind.Undetermined && !ResolveGpu())
        {
            return false;
        }

        return _gpu switch
        {
            GpuKind.GL => TryRenderGL(context, width, height),
            GpuKind.Wgl => TryRenderWgl(context, width, height),
            _ => false
        };
    }

    private bool ResolveGpu()
    {
        var factory = GetGraphicsFactory();
        string backend = factory.Backend;

        if (backend.Equals(MewVGWin32BackendName, StringComparison.OrdinalIgnoreCase))
        {
            _glHost = new SkiaGLSurfaceHost(factory);
            _gpu = GpuKind.GL;
            System.Diagnostics.Debug.WriteLine($"[SkiaCanvasViewWin32] Resolved GPU path = GL (factory.Backend={backend})");
            return true;
        }

        if (IsDirect2DWglInteropEnabled() &&
            backend.Equals(Direct2DBackendName, StringComparison.OrdinalIgnoreCase) &&
            factory is Direct2DGraphicsFactory d2dFactory)
        {
            _wglHost = new SkiaWglInteropHost(d2dFactory);
            _gpu = GpuKind.Wgl;
            System.Diagnostics.Debug.WriteLine($"[SkiaCanvasViewWin32] Resolved GPU path = WGL (factory.Backend={backend})");
            return true;
        }

        return false;
    }

    private static bool IsDirect2DWglInteropEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnableDirect2DWglInteropEnv);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryRenderGL(IGraphicsContext context, int width, int height)
    {
        if (_glHost is null) return false;

        try
        {
            if (!DiagLog.Enabled)
            {
                if (!_glHost.EnsureSurface(width, height)) return false;

                var fastInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                var fastImage = _glHost.Paint(surface =>
                {
                    var canvas = surface.Canvas;
                    canvas.Clear(SKColors.Transparent);
                    InvokePaint(canvas, fastInfo);
                });

                if (fastImage is null)
                {
                    if (_glHost.SurfaceInvalidated && _glHost.EnsureSurface(width, height))
                    {
                        fastImage = _glHost.Paint(surface =>
                        {
                            var canvas = surface.Canvas;
                            canvas.Clear(SKColors.Transparent);
                            InvokePaint(canvas, fastInfo);
                        });
                    }

                    if (fastImage is null)
                    {
                        return false;
                    }
                }

                context.DrawImage(fastImage, Bounds);
                return true;
            }

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!_glHost.EnsureSurface(width, height)) return false;
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var image = _glHost.Paint(surface =>
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                InvokePaint(canvas, info);
            });
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();

            if (image is null)
            {
                if (_glHost.SurfaceInvalidated && _glHost.EnsureSurface(width, height))
                {
                    image = _glHost.Paint(surface =>
                    {
                        var canvas = surface.Canvas;
                        canvas.Clear(SKColors.Transparent);
                        InvokePaint(canvas, info);
                    });
                }

                if (image is null)
                {
                    return false;
                }
            }

            context.DrawImage(image, Bounds);
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();

            AccumulateGLTimings(t1 - t0, t2 - t1, t3 - t2);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Accumulates per-phase render timings and emits a single averaged line every ~1 s.
    /// <c>ensure</c> is the EnsureSurface call (~0 ms after first frame), <c>paint</c> is
    /// SkiaGLSurfaceHost.Paint (Skia GR draw + flush), and <c>draw</c> is
    /// <c>context.DrawImage</c> — MewVG sampling our IImage into the window backbuffer.
    /// The <c>draw</c> column is the one that reveals cross-adapter / DWM compositor cost.
    /// </summary>
    private void AccumulateGLTimings(long ensure, long paint, long draw)
    {
        if (!DiagLog.Enabled)
        {
            return;
        }

        _glEnsureTicks += ensure;
        _glPaintTicks += paint;
        _glDrawTicks += draw;
        _glFrames++;

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_glLogDeadlineTicks == 0)
        {
            _glLogDeadlineTicks = now + System.Diagnostics.Stopwatch.Frequency;
            return;
        }
        if (now < _glLogDeadlineTicks) return;

        double tickToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        int frames = Math.Max(1, _glFrames);
        double mEnsure = _glEnsureTicks / (double)frames * tickToMs;
        double mPaint = _glPaintTicks / (double)frames * tickToMs;
        double mDraw = _glDrawTicks / (double)frames * tickToMs;
        double mTotal = mEnsure + mPaint + mDraw;

        System.Diagnostics.Debug.WriteLine(
            $"[SkiaCanvasViewWin32.TryRenderGL] frames={_glFrames} avgMs total={mTotal:F3} " +
            $"ensure={mEnsure:F3} paint={mPaint:F3} draw={mDraw:F3}");

        _glEnsureTicks = _glPaintTicks = _glDrawTicks = 0;
        _glFrames = 0;
        _glLogDeadlineTicks = now + System.Diagnostics.Stopwatch.Frequency;
    }

    private bool TryRenderWgl(IGraphicsContext context, int width, int height)
    {
        if (_wglHost is null) return false;

        try
        {
            if (!_wglHost.EnsureSurface(width, height))
            {
                System.Diagnostics.Debug.WriteLine("[SkiaCanvasViewWin32] WGL EnsureSurface returned false — falling back to CPU.");
                return false;
            }

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var image = _wglHost.Paint(surface =>
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                InvokePaint(canvas, info);
            });

            if (image is null) return false;
            context.DrawImage(image, Bounds);
            return true;
        }
        catch (Exception ex)
        {
            // Surface the failure so we don't silently degrade to CPU. The WGL_NV_DX_interop
            // path requires the extension to be exposed on the GL driver and the D3D11 texture
            // to be registrable (some AMD drivers return success on register and then AV on
            // first lock). Logging the exception is the only useful diagnostic.
            System.Diagnostics.Debug.WriteLine($"[SkiaCanvasViewWin32] WGL D2D interop path failed: {ex}");
            Console.Error.WriteLine($"[SkiaCanvasViewWin32] WGL D2D interop path failed: {ex.Message}");
            return false;
        }
    }

    protected override void DisposeGpu()
    {
        _glHost?.Dispose();
        _glHost = null;
        _wglHost?.Dispose();
        _wglHost = null;
    }

    protected override void OnGpuInteropInvalidatedCore(GpuInteropInvalidatedEventArgs e)
    {
        _gpu = GpuKind.Undetermined;
        _glEnsureTicks = _glPaintTicks = _glDrawTicks = 0;
        _glFrames = 0;
        _glLogDeadlineTicks = 0;
    }
}
