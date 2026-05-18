using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Sample.Rendering;

/// <summary>
/// Hosts Skia GR GL rendering on top of a backend-owned offscreen render target.
/// </summary>
/// <remarks>
/// <para>
/// The backend's <see cref="IRenderDevice.CreateSurface(RenderSurfaceDescriptor)"/> already
/// allocates the GL FBO + color texture + (optional) stencil renderbuffer, manages the GL
/// context that owns those handles, and handles deferred disposal — see
/// <c>OpenGLPixelRenderSurface</c>. This host borrows that GL texture id via the
/// backend-agnostic <see cref="IGpuTextureSource"/> (using its native handle as a GL texture
/// id; the caller has already gated this path to GL-backed factories), wraps it as a Skia
/// <see cref="GRBackendTexture"/> so Skia can render into it, and exposes the same surface
/// as an <see cref="IImage"/> for MewVG to sample without any cross-resource copy. The
/// per-backend <c>IGLTextureSource</c> interface lives in each backend's own assembly, which
/// would make this sample ambiguous to compile against both — <see cref="IGpuTextureSource"/>
/// in core gives us the same handle without the duplicate-type problem.
/// </para>
/// <para>
/// Lifecycle: <see cref="EnsureSurface"/> creates/recreates the surface + Skia GR
/// wrappers only when dimensions change; <see cref="Paint"/> runs every frame and brackets
/// the user delegate in an <see cref="IExternalWritableGpuSurface.BeginExternalWrite"/>
/// scope. The backend keeps the FBO and texture alive — we don't call any
/// <c>glGen*</c>/<c>glDelete*</c> ourselves.
/// </para>
/// </remarks>
internal sealed class SkiaGLSurfaceHost : IDisposable
{
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_RGBA8 = 0x8058;

    private readonly IGraphicsFactory _factory;

    private IExternalWritableGpuSurface? _surface;
    private IImage? _image;

    private GRGlInterface? _glInterface;
    private GRContext? _grContext;
    private GRBackendTexture? _backendTexture;
    private SKSurface? _skSurface;
    private GpuResourceAffinity? _writeAffinity;

    private int _pixelWidth;
    private int _pixelHeight;
    private bool _disposed;
    private bool _surfaceInvalidated;

    // --- Per-frame timing accumulators (logged every ~1 s) ---
    private long _accBegin, _accReset, _accPaint, _accFlush, _accEnd;
    private int _accFrames;
    private long _accLogDeadlineTicks;
    private int _lastLoggedSwapInterval = int.MinValue;

    public SkiaGLSurfaceHost(IGraphicsFactory factory)
    {
        _factory = factory;
    }

    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;

    public bool SurfaceInvalidated => _surfaceInvalidated;

    /// <summary>
    /// Ensures the backend offscreen surface, Skia <see cref="GRContext"/>, and the
    /// <see cref="SKSurface"/> wrapping the backend's GL texture match the requested
    /// dimensions. No-op when called repeatedly with the same size.
    /// </summary>
    public bool EnsureSurface(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return false;
        }

        if (_skSurface != null && pixelWidth == _pixelWidth && pixelHeight == _pixelHeight)
        {
            return true;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[SkiaGLSurfaceHost] (re)creating surface {pixelWidth}x{pixelHeight} (prev={_pixelWidth}x{_pixelHeight}) t={DateTime.Now:HH:mm:ss.fff}");

        ReleaseSurfaceResources();
        _surfaceInvalidated = false;

        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;

        try
        {
            var surface = _factory.CreateSurface(RenderSurfaceDescriptor.ExternalGpuWritable(
                pixelWidth, pixelHeight, dpiScale: 1.0, hasAlpha: true,
                debugName: "SkiaGLSurfaceHost"));
            _surface = surface as IExternalWritableGpuSurface
                ?? throw new InvalidOperationException(
                    "Backend offscreen surface does not support external GPU writes.");

            using (var writeScope = _surface.BeginExternalWrite())
            {
                CaptureWriteAffinity(writeScope);

                EnsureGrContext();

                uint textureId = (uint)writeScope.NativeHandle;
                if (textureId == 0)
                {
                    throw new InvalidOperationException(
                        "Backend offscreen surface did not expose a GL texture handle.");
                }

                _backendTexture = new GRBackendTexture(
                    pixelWidth,
                    pixelHeight,
                    mipmapped: false,
                    new GRGlTextureInfo(GL_TEXTURE_2D, textureId, GL_RGBA8));

                _skSurface = SKSurface.Create(
                    _grContext!,
                    _backendTexture,
                    // OpenGL FBO color attachments are bottom-up — IGpuTextureSource.YFlipped
                    // is true on the backend side, and MewVG flips at sample time, so Skia
                    // also paints bottom-left so the two conventions cancel out.
                    GRSurfaceOrigin.BottomLeft,
                    SKColorType.Rgba8888);

                if (_skSurface == null)
                {
                    throw new InvalidOperationException(
                        "SKSurface.Create failed wrapping the backend GL texture.");
                }

                // One-shot GL identity dump per surface lifetime. If the renderer string says
                // "GDI Generic" or "Microsoft Basic Render Driver", we're on the OS software
                // fallback and the whole zero-copy story is moot.
                GLDiagnostics.LogContextIdentity($"SkiaGLSurfaceHost.EnsureSurface {pixelWidth}x{pixelHeight}");
            }

            _image = _factory.CreateImageView(_surface);
            return _image != null;
        }
        catch
        {
            ReleaseSurfaceResources();
            throw;
        }
    }

    /// <summary>
    /// Brackets the user's painter in an external write scope, runs the
    /// Skia paint into the wrapped surface, flushes Skia + GR, and returns the MewUI
    /// <see cref="IImage"/> aliased to the same texture for <c>context.DrawImage(...)</c>.
    /// </summary>
    public IImage? Paint(Action<SKSurface> painter)
    {

        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(painter);

        if (_surface == null || _skSurface == null || _image == null || _grContext == null)
        {
            return null;
        }

        bool diagnostics = DiagLog.Enabled;
        long tStart = diagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        long tBegin = 0, tReset = 0, tPaint = 0, tFlush = 0, tEnd = 0;

        using var writeScope = _surface.BeginExternalWrite();
        if (diagnostics) tBegin = System.Diagnostics.Stopwatch.GetTimestamp();
        if (HasWriteAffinityChanged(writeScope))
        {
            ReleaseSurfaceResources();
            _surfaceInvalidated = true;
            return null;
        }

        // Skia tracks GL state from its previous frame; MewVG may have changed shader /
        // texture / blend / scissor bindings since then. ResetContext forces Skia to
        // re-bind everything it cares about — without it we get stale-binding artifacts
        // on the first draw of each frame.
        _grContext.ResetContext(GRBackendState.All);
        if (diagnostics) tReset = System.Diagnostics.Stopwatch.GetTimestamp();

        painter(_skSurface);
        if (diagnostics) tPaint = System.Diagnostics.Stopwatch.GetTimestamp();

        _skSurface.Flush();
        _grContext.Flush();
        writeScope.Flush();
        _surface.MarkExternalContentChanged();
        if (diagnostics)
        {
            tFlush = System.Diagnostics.Stopwatch.GetTimestamp();
            tEnd = System.Diagnostics.Stopwatch.GetTimestamp();

            AccumulatePaintTimings(tBegin - tStart, tReset - tBegin, tPaint - tReset, tFlush - tPaint, tEnd - tFlush);
        }

        return _image;
    }

    /// <summary>
    /// Accumulates per-phase timings across frames and flushes a single summary line every
    /// ~1 s, so the log shows the average cost of each Paint phase without spamming once
    /// per frame. Also tracks the GL swap interval each cycle — when it changes (e.g. after
    /// a monitor move) the new value is included so we can correlate CPU swings with vsync
    /// state transitions.
    /// </summary>
    private void AccumulatePaintTimings(long begin, long reset, long paint, long flush, long end)
    {
        if (!DiagLog.Enabled)
        {
            return;
        }

        _accBegin += begin;
        _accReset += reset;
        _accPaint += paint;
        _accFlush += flush;
        _accEnd += end;
        _accFrames++;

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_accLogDeadlineTicks == 0)
        {
            _accLogDeadlineTicks = now + System.Diagnostics.Stopwatch.Frequency;
            return;
        }
        if (now < _accLogDeadlineTicks) return;

        int? swap = GLDiagnostics.GetSwapInterval();
        bool swapChanged = swap.HasValue && swap.Value != _lastLoggedSwapInterval;
        if (swap.HasValue) _lastLoggedSwapInterval = swap.Value;

        double tickToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        int frames = Math.Max(1, _accFrames);
        double mBegin = (double)_accBegin / frames * tickToMs;
        double mReset = (double)_accReset / frames * tickToMs;
        double mPaint = (double)_accPaint / frames * tickToMs;
        double mFlush = (double)_accFlush / frames * tickToMs;
        double mEnd = (double)_accEnd / frames * tickToMs;
        double mTotal = mBegin + mReset + mPaint + mFlush + mEnd;

        System.Diagnostics.Debug.WriteLine(
            $"[SkiaGLSurfaceHost.Paint] frames={_accFrames} avgMs total={mTotal:F3} " +
            $"begin={mBegin:F3} reset={mReset:F3} paint={mPaint:F3} flush={mFlush:F3} end={mEnd:F3} " +
            $"swapInterval={(swap?.ToString() ?? "n/a")}{(swapChanged ? " (changed)" : "")}");

        _accBegin = _accReset = _accPaint = _accFlush = _accEnd = 0;
        _accFrames = 0;
        _accLogDeadlineTicks = now + System.Diagnostics.Stopwatch.Frequency;
    }

    private void EnsureGrContext()
    {
        if (_grContext != null) return;

        _glInterface = GRGlInterface.Create()
            ?? throw new InvalidOperationException(
                "GRGlInterface.Create returned null — no GL context current on this thread.");
        _grContext = GRContext.CreateGl(_glInterface)
            ?? throw new InvalidOperationException("GRContext.CreateGl returned null for the current GL context.");
    }

    private void CaptureWriteAffinity(IExternalGpuWriteScope scope)
        => _writeAffinity = (scope as IGpuResourceAffinityProvider)?.Affinity;

    private bool HasWriteAffinityChanged(IExternalGpuWriteScope scope)
    {
        var current = (scope as IGpuResourceAffinityProvider)?.Affinity;
        return _writeAffinity is { } previous
            && current is { } next
            && previous != next;
    }

    private void ReleaseSurfaceResources()
    {
        _skSurface?.Dispose();
        _skSurface = null;

        _backendTexture?.Dispose();
        _backendTexture = null;

        _image?.Dispose();
        _image = null;

        _surface?.Dispose();
        _surface = null;
        _writeAffinity = null;

        _pixelWidth = 0;
        _pixelHeight = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseSurfaceResources();

        _grContext?.Dispose();
        _grContext = null;
        _glInterface?.Dispose();
        _glInterface = null;
    }
}
