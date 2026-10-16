using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux.Framebuffer;
using Aprillz.MewUI.Rendering.FreeType;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGFramebufferGraphicsFactory : IPersistentFrameGraphicsFactory
{
    public const string BackendIdentifier = "MewVG.Framebuffer";

    private readonly object _deviceLock = new();
    private readonly object _workerActivationLock = new();
    private FramebufferEglDevice? _device;
    private bool _drawsInPlace;
    private bool _windowKeepsFrame;

    internal static nint GetCurrentGLContextStatic() => LibEgl.eglGetCurrentContext();

    private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider =
        new MewVGGLOffscreenSurfaceProvider(GetCurrentGLContextStatic);

    private readonly PboFenceUploaderPool _pboPool = new(GetCurrentGLContextStatic);

    public string Backend => BackendIdentifier;

    public IDisposable AcquireConcurrentRenderUnit() => MewVGNoOpRenderScope.Instance;

    /// <summary>
    /// The EGL device of the screen surface. A DRM/KMS surface brings its GBM device; a CPU surface renders
    /// on a surfaceless display. Created with the first window and kept for the process.
    /// </summary>
    private FramebufferEglDevice EnsureDevice(IWindowSurface surface)
    {
        lock (_deviceLock)
        {
            if (_device != null)
            {
                return _device;
            }

            _device = surface switch
            {
                IDrmKmsWindowSurface drm => FramebufferEglDevice.CreateForGbm(drm.GbmDevice, drm.GbmFormat),
                ICpuFramebufferWindowSurface => FramebufferEglDevice.CreateSurfaceless(),
                _ => throw new ArgumentException("MewVG (framebuffer) requires a framebuffer platform window surface.", nameof(surface)),
            };

            Console.Error.WriteLine($"[MewUI.Framebuffer] renderer: {_device.Description}");
            return _device;
        }
    }

    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        (family, string? path, string[] listed) = ResolveFamilyCandidate(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size));
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px) { ListedFamilies = listed }
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
    }

    private partial IFont CreateFontCore(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        (family, string? path, string[] listed) = ResolveFamilyCandidate(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero));
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px) { ListedFamilies = listed }
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
    }

    /// <summary>
    /// Picks the first installed family from a comma-separated list to draw with, and the installed families
    /// after it to supply what it lacks, as the X11 backend does.
    /// </summary>
    private static (string Family, string? Path, string[] Listed) ResolveFamilyCandidate(string family, FontWeight weight, bool italic)
    {
        if (!FontFamilyList.IsList(family))
        {
            return (family, LinuxFontResolver.ResolveFontPath(family, weight, italic), []);
        }

        string[] candidates = FontFamilyList.Split(family);
        var installed = candidates
            .Select(candidate => (Family: candidate, Path: LinuxFontResolver.ResolveInstalledFontPath(candidate, weight, italic)))
            .Where(candidate => candidate.Path != null)
            .ToArray();
        if (installed.Length > 0)
        {
            return (installed[0].Family, installed[0].Path, [.. installed[1..].Select(candidate => candidate.Family)]);
        }

        string first = candidates.Length > 0 ? candidates[0] : family;
        return (first, LinuxFontResolver.ResolveFontPath(first, weight, italic), []);
    }

    private partial IDisposable CreateWindowResources(IWindowSurface surface)
    {
        var device = EnsureDevice(surface);
        var resources = MewVGFramebufferWindowResources.Create(device, surface);
        _drawsInPlace = resources.DrawsInPlace;
        _windowKeepsFrame = resources.DrawsInPlace || resources.TracksBufferAge;
        return resources;
    }

    private partial IGraphicsContext CreateContextCore(WindowRenderTarget target, IDisposable resources)
        => ((MewVGFramebufferWindowResources)resources).GetOrCreateContext(_offscreenProvider, RaiseGpuInteropInvalidated);

    private partial ITextBackendMeasurementContext CreateMeasurementContextCore(uint dpi)
        => new OpenGLMeasurementContext(dpi);

    partial void TryCreatePixelSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha, ref bool handled, ref IRenderSurface? renderTarget)
    {
        if (handled)
        {
            return;
        }

        renderTarget = new OpenGLPixelRenderSurface(
            pixelWidth,
            pixelHeight,
            dpiScale,
            _offscreenProvider.QueueTargetDisposal,
            GetCurrentGLContextStatic,
            hasAlpha);
        handled = true;
    }

    partial void TryGetImageDisposeHandler(ref Action<MewVGImage>? handler)
        => handler ??= _offscreenProvider.QueueImageDisposal;

    partial void TryCreateImageFilterExecutor(ref Filters.IImageFilterExecutor? executor)
    {
        // The GPU filter shaders are GLSL 1.40; on GLES the CPU executor runs instead.
        if (_device?.IsGles != true)
        {
            executor ??= new OpenGLImageFilterExecutor();
        }
    }

    partial void TryCreateContextForTarget(IRenderTarget target, ref bool handled, ref IGraphicsContext? context)
    {
        if (handled || target is not OpenGLPixelRenderSurface pixelSurface)
        {
            return;
        }

        context = MewVGFramebufferGraphicsContext.CreateForOffscreen(
            _offscreenProvider.AcquireSurface(),
            _offscreenProvider,
            pixelSurface);
        handled = true;
    }

    partial void DisposePlatformResources()
    {
        _offscreenProvider.Dispose();
        lock (_deviceLock)
        {
            _device?.Dispose();
            _device = null;
        }
    }

    private partial IDisposable AcquireBackgroundRenderScopeCore()
    {
        // A context already current on this thread (the UI thread inside a frame) is left alone.
        if (GetCurrentGLContextStatic() != 0)
        {
            return MewVGNoOpRenderScope.Instance;
        }

        var device = _device;
        if (device == null)
        {
            // No window yet, so no display to render on; the caller skips its offscreen work.
            return MewVGNoOpRenderScope.Instance;
        }

        // The worker context can be current on one thread at a time.
        Monitor.Enter(_workerActivationLock);
        try
        {
            if (!device.MakeWorkerCurrent())
            {
                Monitor.Exit(_workerActivationLock);
                return MewVGNoOpRenderScope.Instance;
            }

            MewVGGLBootstrapFramebuffer.EnsureInitialized(device.IsGles);
        }
        catch
        {
            Monitor.Exit(_workerActivationLock);
            throw;
        }

        return new WorkerContextScope(_workerActivationLock, device);
    }

    bool IPersistentFrameGraphicsFactory.IsPersistentFrameRenderingVerified => true;

    // The CPU copy path keeps its frame in its own buffer and repaints it in place. A swap-chain buffer holds an
    // older frame, which buffer age lets the window context bring up to date before the changed areas are copied.
    bool IPersistentFrameGraphicsFactory.WindowTargetKeepsPresentedFrame => _windowKeepsFrame;

    bool IPersistentFrameGraphicsFactory.DrawsWindowFramesInPlace => _drawsInPlace;

    IDisposable IPersistentFrameGraphicsFactory.AcquirePersistentFrameRenderScope()
    {
        // The window context draws the frame surface as well as the window, so the copy that follows needs no
        // GPU wait and no context switch. It stays current: the window frame that comes next makes it current
        // anyway and releases it at its end.
        if (GetCurrentGLContextStatic() == 0)
        {
            foreach (var resources in _windows.Values)
            {
                if (resources is MewVGFramebufferWindowResources window)
                {
                    window.MakeCurrent();
                    return MewVGNoOpRenderScope.Instance;
                }
            }
        }

        return AcquireBackgroundRenderScopeCore();
    }

    partial void TryCreateAsyncUploadImage(IPixelBufferSource source, ref IImage? image)
    {
        if (!PboFenceUploader.IsSupported)
        {
            return;
        }

        try
        {
            var uploader = _pboPool.Rent(source);
            image = new MewVGExternalRasterImage(new PooledPboTexture(uploader, _pboPool), ownsSource: true);
        }
        catch
        {
            // Async upload is an optimization; the synchronous path takes over.
        }
    }

    private sealed class WorkerContextScope : IDisposable
    {
        private readonly object _activationLock;
        private readonly FramebufferEglDevice _device;
        private bool _disposed;

        public WorkerContextScope(object activationLock, FramebufferEglDevice device)
        {
            _activationLock = activationLock;
            _device = device;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                // The window context samples what the worker drew, so its commands must have finished.
                GL.Finish();
                _device.ReleaseCurrent();
            }
            finally
            {
                Monitor.Exit(_activationLock);
            }
        }
    }
}
