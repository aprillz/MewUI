using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewUI.Video.Sample.Decoding;
using Aprillz.MewUI.Video.Sample.Diagnostics;
using Aprillz.MewUI.Video.Sample.Playback;

namespace Aprillz.MewUI.Video.Sample.Controls;

public sealed class VideoView : FrameworkElement
{
    private VideoPlayback? _playback;
    private IImage? _image;
    private string _presentationPathText = "idle";
    private readonly Dictionary<nint, CachedGlInteropEntry> _glInteropCache = [];
    // Live WGL_NV_DX_interop wrapper for hardware-decoded frames on GL Win32. Lifetime
    // is tied to the IImage we hand to MewVG — recreated when the frame's underlying
    // D3D11 texture changes. Null when the path isn't taken (software-decoded frames,
    // non-GL-Win32 backend, driver missing the extension, OS != Windows).
    private WglDxInteropTexture? _interopTexture;
    private CachedGlInteropEntry? _activeGLInteropEntry;
    private IGpuInteropInvalidationSource? _gpuInteropInvalidationSource;
    private bool _interopProbeFailed;   // sticky flag — don't retry per frame after a failed probe
    private VideoFrame? _lastUploadedFrame;
    private long _lastGeneration = -1;
    private bool _firstPresentedFrameLogged;
    private long _lastGpuInteropInvalidationTicks;

    public VideoPlayback? Playback
    {
        get => _playback;
        set
        {
            if (ReferenceEquals(_playback, value))
            {
                return;
            }

            ReleaseLastFrame();
            if (_playback is not null)
            {
                _playback.FrameReady -= OnPlaybackFrameReady;
            }

            _playback = value;
            if (_playback is not null)
            {
                _playback.FrameReady += OnPlaybackFrameReady;
                _presentationPathText = "waiting for first frame";
            }
            else
            {
                _presentationPathText = "idle";
            }

            _lastGeneration = value?.Generation ?? -1;
            InvalidateVisual();
        }
    }

    public string PresentationPathText => _presentationPathText;

    public event EventHandler<GpuInteropInvalidatedEventArgs>? GpuInteropInvalidated;

    /// <summary>
    /// Diagnostic counter — increments each time <see cref="OnRender"/> runs. Externally
    /// readable so the host (Program.cs) can periodically log "OnRender invocations per
    /// second" alongside the platform render-loop fps to distinguish "render loop ticks
    /// without VideoView redraw" (cheap stats overlay update) from "render loop ticks
    /// with full visual-tree redraw" (expensive — 4K texture sample per tick). Reset
    /// externally via <see cref="ResetOnRenderCallCount"/>.
    /// </summary>
    public int OnRenderCallCount => _onRenderCallCount;
    private int _onRenderCallCount;

    /// <summary>Resets the <see cref="OnRenderCallCount"/> counter (sampling boundary).</summary>
    public void ResetOnRenderCallCount() => _onRenderCallCount = 0;

    protected override Size MeasureContent(Size availableSize) => new(640, 360);

    protected override void OnRender(IGraphicsContext context)
    {
        _onRenderCallCount++;
        base.OnRender(context);

        context.FillRectangle(Bounds, Color.Black);

        var playback = Playback;
        if (playback is null)
        {
            return;
        }

        if (_lastGeneration != playback.Generation)
        {
            ReleaseLastFrame();
            _lastGeneration = playback.Generation;
        }

        var frame = playback.PullCurrent();
        if (frame is not null)
        {
            PresentFrame(frame);
            if (!_firstPresentedFrameLogged)
            {
                _firstPresentedFrameLogged = true;
                SampleLog.Write($"First frame presented. size={frame.Width}x{frame.Height}, pts={frame.Pts}");
            }
        }

        if (_image is not null && _lastUploadedFrame is not null)
        {
            context.DrawImage(_image, FitAspect(Bounds, _lastUploadedFrame.Width, _lastUploadedFrame.Height));
        }

        // No unconditional InvalidateVisual here: re-invalidating from inside OnRender
        // turns the render loop into a CPU spin that re-samples the same texture
        // ~5x per displayed frame (e.g. ~285 fps render for a 60 fps source). The
        // playback's FrameReady event drives invalidation at the actual decode rate
        // — see VideoPlayback.RunDecodeLoop, which now fires FrameReady on every
        // queued frame regardless of play state.
    }

    protected override void OnDispose()
    {
        UnsubscribeGpuInteropInvalidation();
        ReleaseLastFrame();
        if (_playback is not null)
        {
            _playback.FrameReady -= OnPlaybackFrameReady;
        }
        _image?.Dispose();
        _image = null;
        base.OnDispose();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        UnsubscribeGpuInteropInvalidation();

        if (newRoot is Window window && window.GraphicsFactory is IGpuInteropInvalidationSource source)
        {
            _gpuInteropInvalidationSource = source;
            source.GpuInteropInvalidated += OnGpuInteropInvalidated;
        }
    }

    private void UnsubscribeGpuInteropInvalidation()
    {
        if (_gpuInteropInvalidationSource is null)
        {
            return;
        }

        _gpuInteropInvalidationSource.GpuInteropInvalidated -= OnGpuInteropInvalidated;
        _gpuInteropInvalidationSource = null;
    }

    private void OnGpuInteropInvalidated(object? sender, GpuInteropInvalidatedEventArgs e)
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher is not null && !dispatcher.IsOnUIThread)
        {
            dispatcher.BeginInvoke(() => OnGpuInteropInvalidated(sender, e));
            return;
        }

        if (e.RenderTargetHandle != 0
            && FindVisualRoot() is Window window
            && window.Handle != e.RenderTargetHandle)
        {
            return;
        }

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        bool mayReleaseCurrentFrame = _lastGpuInteropInvalidationTicks == 0
            || now - _lastGpuInteropInvalidationTicks >= System.Diagnostics.Stopwatch.Frequency;
        _lastGpuInteropInvalidationTicks = now;

        if (!mayReleaseCurrentFrame)
        {
            return;
        }

        SampleLog.Write(
            $"VideoView GPU interop invalidated: reason={e.Reason}, renderTarget=0x{e.RenderTargetHandle:X}, renderTargetDeviceChanged={e.RenderTargetDeviceChanged}, displayChanged={e.DisplayChanged}, externalResourceMismatch={e.ExternalResourceMismatch}");

        _interopProbeFailed = false;
        ReleaseLastFrame();
        UpdatePresentationPath("gpu interop invalidated");

        GpuInteropInvalidated?.Invoke(this, e);
        InvalidateVisual();
    }

    private void PresentFrame(VideoFrame frame)
    {
        if (ReferenceEquals(frame, _lastUploadedFrame))
        {
            return;
        }

        var previousFrame = _lastUploadedFrame;
        var previousImage = _image;
        var previousInterop = _interopTexture;
        var previousGlInteropEntry = _activeGLInteropEntry;

        var factory = GetGraphicsFactory();
        IImage? image = TryCreateZeroCopyImage(factory, frame);
        if (image is null)
        {
            if (!frame.HasCpuPixels)
            {
                UpdatePresentationPath("cpu pending (requesting readback fallback)");
                _playback?.EnableCpuPresentationFallback();
                _playback?.Recycle(frame);
                return;
            }

            // CPU-readback fallback: BgraData → MewVGImage / Direct2DImage upload.
            // Hits when the frame is software-decoded, the backend isn't GL-Win32-with-
            // WGL_NV_DX_interop, or registration failed (FFmpeg may output texture arrays
            // that the basic GL_TEXTURE_2D wrap path can't address — a known limitation
            // we accept while the zero-copy path matures).
            image = factory.CreateImageView(frame);
            _interopTexture = null;
            _activeGLInteropEntry = null;

            // Distinguish the two CPU-upload variants in the stats overlay so toggling
            // the PBO path is visible. The factory returns MewVGExternalRasterImage when
            // it routed through PboFenceUploader, and MewVGImage otherwise. The concrete
            // types are internal so we match by type name string.
            string uploadKind = image?.GetType().Name == "MewVGExternalRasterImage"
                ? "cpu upload + async PBO+fence"
                : "cpu upload (sync)";
            UpdatePresentationPath($"{uploadKind} ({factory.Backend})");
        }
        _image = image;
        _lastUploadedFrame = frame;

        if (previousGlInteropEntry is null)
        {
            previousImage?.Dispose();
            previousInterop?.Dispose();
        }

        if (previousFrame is not null)
        {
            _playback?.Recycle(previousFrame);
        }
    }

    /// <summary>
    /// Attempts the zero-copy GPU path for a hardware-decoded frame. Returns null when
    /// any precondition fails (caller falls back to CPU upload). On success, the
    /// returned IImage shares the D3D11 texture via WGL_NV_DX_interop — sampling from
    /// NVG happens directly against the decoder output, no readback.
    /// </summary>
    /// <remarks>
    /// Path eligibility is gated by OS (Windows-only — macOS/X11 don't have D3D11),
    /// frame metadata (must be hardware-decoded with a D3D11 texture and device), and
    /// driver capability (WGL_NV_DX_interop must load on the active GL context). If
    /// the backend doesn't support external sample image views (D2D, GDI),
    /// it throws <see cref="NotSupportedException"/> which we treat as "use CPU path".
    /// </remarks>
    private IImage? TryCreateZeroCopyImage(IGraphicsFactory factory, VideoFrame frame)
    {
        return frame.GpuResource switch
        {
            VideoToolboxGpuResource { Texture: var vtTexture } => TryCreateVideoToolboxImage(factory, vtTexture),
            D3D11GpuResource d3d11 => TryCreateD3D11Image(factory, frame, d3d11),
            VaapiGpuResource vaapi => TryCreateVaapiImage(factory, frame, vaapi),
            _ => null,
        };
    }

    /// <summary>
    /// Linux VAAPI zero-copy attempt: export the VA surface as a DRM PRIME dma_buf
    /// and import it as a GL texture via EGLImage. Requires Mesa-style GLX/EGL
    /// state sharing — NVIDIA proprietary fails here and falls back to CPU upload.
    /// </summary>
    /// <remarks>
    /// Wrapper is constructed per frame. Caching by VASurfaceID would amortize the
    /// dma_buf import cost across frames (FFmpeg rotates a small surface pool), but
    /// is left as a follow-up — first cut focuses on functional correctness.
    /// </remarks>
    private IImage? TryCreateVaapiImage(IGraphicsFactory factory, VideoFrame frame, VaapiGpuResource vaapi)
    {
        if (!OperatingSystem.IsLinux() || vaapi.VaDisplay == 0 || vaapi.VaSurfaceId == 0)
        {
            return null;
        }

        try
        {
            var texture = new VaapiDmaBufTexture(vaapi.VaDisplay, vaapi.VaSurfaceId, frame.Width, frame.Height);
            var image = factory.CreateImageView(texture);
            UpdatePresentationPath("gpu zero-copy (vaapi dma_buf → egl → gl)");
            if (!_firstPresentedFrameLogged)
            {
                SampleLog.Write($"Zero-copy GPU path active: VAAPI surface {vaapi.VaSurfaceId} exported as dma_buf, bound as GL texture {(uint)texture.Planes[0].NativeHandle}.");
            }
            return image;
        }
        catch (Exception ex)
        {
            SampleLog.Write($"VAAPI dma_buf import failed ({ex.Message}); using CPU upload path.");
            return null;
        }
    }

    private IImage? TryCreateVideoToolboxImage(IGraphicsFactory factory, VideoToolboxFrameTexture vtTexture)
    {
        try
        {
            var image = factory.CreateImageView(vtTexture);
            UpdatePresentationPath("gpu zero-copy (videotoolbox iosurface → metal)");
            if (!_firstPresentedFrameLogged)
            {
                SampleLog.Write($"Zero-copy GPU path active: VideoToolbox CVPixelBuffer wrapped as MTLTexture 0x{vtTexture.MtlTexture:X}.");
            }
            return image;
        }
        catch (Exception ex)
        {
            SampleLog.Write($"VideoToolbox zero-copy wrap failed ({ex.Message}); using CPU upload path.");
            return null;
        }
    }

    private IImage? TryCreateD3D11Image(IGraphicsFactory factory, VideoFrame frame, D3D11GpuResource d3d11)
    {
        if (!OperatingSystem.IsWindows() || d3d11.TextureHandle == 0 || d3d11.DeviceHandle == 0)
        {
            return null;
        }

        if (factory is Direct2DGraphicsFactory d2dFactory)
        {
            try
            {
                var image = d2dFactory.CreateImageView(d3d11);
                UpdatePresentationPath("gpu zero-copy (direct2d dxgi)");
                if (!_firstPresentedFrameLogged)
                {
                    SampleLog.Write($"Zero-copy GPU path active: Direct2D shared bitmap wrapping D3D11 texture 0x{d3d11.TextureHandle:X}.");
                }
                return image;
            }
            catch (Exception ex)
            {
                SampleLog.Write(
                    $"Direct2D external raster path failed ({ex.Message}); using CPU upload path. frameDevice=0x{d3d11.DeviceHandle:X}, factoryDevice=0x{d2dFactory.NativeD3D11Device:X}, texture=0x{d3d11.TextureHandle:X}");
            }

            return null;
        }

        if (_interopProbeFailed) return null;

        if (!WglDxInteropTexture.IsAvailable)
        {
            SampleLog.Write("WGL_NV_DX_interop unavailable on this driver — using CPU readback path.");
            _interopProbeFailed = true;
            return null;
        }

        string? incompatReason = D3D11Native.ValidateForInterop(d3d11.TextureHandle);
        if (incompatReason != null)
        {
            SampleLog.Write($"D3D11 texture not interop-compatible ({incompatReason}); using CPU readback path.");
            _interopProbeFailed = true;
            return null;
        }

        try
        {
            var entry = GetOrCreateCachedGlInteropEntry(factory, d3d11, frame.Width, frame.Height);
            _interopTexture = entry.InteropTexture;
            _activeGLInteropEntry = entry;
            UpdatePresentationPath("gpu zero-copy (wgl dx interop)");
            if (!_firstPresentedFrameLogged)
            {
                SampleLog.Write($"Zero-copy GPU path active: WGL_NV_DX_interop wrapping D3D11 texture 0x{d3d11.TextureHandle:X}.");
            }
            return entry.Image;
        }
        catch (Exception ex)
        {
            SampleLog.Write($"WGL_NV_DX_interop registration failed ({ex.Message}); switching to CPU readback path.");
            _activeGLInteropEntry = null;
            _interopProbeFailed = true;
            return null;
        }
    }

    private void ReleaseLastFrame()
    {
        if (_activeGLInteropEntry is null)
        {
            _image?.Dispose();
            _interopTexture?.Dispose();
        }

        _image = null;
        _interopTexture = null;
        _activeGLInteropEntry = null;
        DisposeGlInteropCache();

        if (_lastUploadedFrame is null)
        {
            return;
        }

        _playback?.Recycle(_lastUploadedFrame);
        _lastUploadedFrame = null;
    }

    private void UpdatePresentationPath(string nextPath)
    {
        if (string.Equals(_presentationPathText, nextPath, StringComparison.Ordinal))
        {
            return;
        }

        _presentationPathText = nextPath;
        SampleLog.Write($"Presentation path: {nextPath}");
    }

    private CachedGlInteropEntry GetOrCreateCachedGlInteropEntry(IGraphicsFactory factory, D3D11GpuResource d3d11, int width, int height)
    {
        if (_glInteropCache.TryGetValue(d3d11.TextureHandle, out var cached))
        {
            return cached;
        }

        var interopTexture = new WglDxInteropTexture(d3d11.DeviceHandle, d3d11.TextureHandle, width, height);
        var image = factory.CreateImageView(interopTexture);
        cached = new CachedGlInteropEntry(interopTexture, image);
        _glInteropCache.Add(d3d11.TextureHandle, cached);
        return cached;
    }

    private void DisposeGlInteropCache()
    {
        foreach (var entry in _glInteropCache.Values)
        {
            entry.Dispose();
        }

        _glInteropCache.Clear();
    }

    private sealed class CachedGlInteropEntry(WglDxInteropTexture interopTexture, IImage image) : IDisposable
    {
        private bool _disposed;

        public WglDxInteropTexture InteropTexture { get; } = interopTexture;

        public IImage Image { get; } = image;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Image.Dispose();
            InteropTexture.Dispose();
        }
    }

    private void OnPlaybackFrameReady()
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher is null || dispatcher.IsOnUIThread)
        {
            InvalidateVisual();
            return;
        }

        dispatcher.BeginInvoke(InvalidateVisual);
    }

    private static Rect FitAspect(Rect bounds, int width, int height)
    {
        if (bounds.IsEmpty || width <= 0 || height <= 0)
        {
            return Rect.Empty;
        }

        double sourceAspect = width / (double)height;
        double targetAspect = bounds.Width / Math.Max(1.0, bounds.Height);

        if (sourceAspect > targetAspect)
        {
            double renderHeight = bounds.Width / sourceAspect;
            double y = bounds.Y + (bounds.Height - renderHeight) * 0.5;
            return new Rect(bounds.X, y, bounds.Width, renderHeight);
        }

        double renderWidth = bounds.Height * sourceAspect;
        double x = bounds.X + (bounds.Width - renderWidth) * 0.5;
        return new Rect(x, bounds.Y, renderWidth, bounds.Height);
    }
}
