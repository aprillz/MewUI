using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewUI.Text;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGWin32GraphicsFactory
{
    public const string BackendIdentifier = "MewVG.Browser";
    public string Backend => BackendIdentifier;
    public IDisposable AcquireConcurrentRenderUnit() => MewVGNoOpRenderScope.Instance;

    // The browser exposes one WebGL2 context, so every surface shares a single share group.
    internal static nint GetCurrentGLContextStatic() => 1;

    private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider =
        new MewVGGLOffscreenSurfaceProvider(GetCurrentGLContextStatic);

    private partial IFont CreateFontCore(
        string family,
        double size,
        FontWeight weight,
        bool italic,
        bool underline,
        bool strikethrough)
        => new BrowserFont(family, size, weight, italic, underline, strikethrough);

    private partial IFont CreateFontCore(
        string family,
        double size,
        uint dpi,
        FontWeight weight,
        bool italic,
        bool underline,
        bool strikethrough)
        => new BrowserFont(family, size, weight, italic, underline, strikethrough);

    private partial IDisposable CreateWindowResources(IWindowSurface surface)
        => BrowserWindowResources.Create();

    private partial IGraphicsContext CreateContextCore(WindowRenderTarget target, IDisposable resources)
        => ((BrowserWindowResources)resources).GetOrCreateContext();

    private partial ITextBackendMeasurementContext CreateMeasurementContextCore(uint dpi)
        => new BrowserMeasurementContext(dpi);

    private partial IDisposable AcquireBackgroundRenderScopeCore()
        => MewVGNoOpRenderScope.Instance;

    partial void TryCreatePixelSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha, ref bool handled, ref IRenderSurface? renderTarget)
    {
        if (handled) return;
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

    partial void TryCreateContextForTarget(IRenderTarget target, ref bool handled, ref IGraphicsContext? context)
    {
        if (handled || target is not OpenGLPixelRenderSurface pixelSurface) return;
        context = MewVGWin32GraphicsContext.CreateForOffscreen(
            _offscreenProvider.AcquireSurface(),
            _offscreenProvider,
            pixelSurface);
        handled = true;
    }

    private sealed class BrowserMeasurementContext(uint dpi) : MeasureGraphicsContextBase
    {
        public override double DpiScale { get; } = Math.Max(1, dpi) / 96.0;

        public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
            => Measure(text, font, double.PositiveInfinity);

        public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
            => Measure(text, font, maxWidth);

        private static Size Measure(ReadOnlySpan<char> text, IFont font, double maxWidth)
            => BrowserTextMeasure.Measure(text, font, maxWidth);
    }
}

internal sealed class BrowserWindowResources : IDisposable, IMewVGWindowCacheMaintenance
{
    private static bool _initialized;
    private static NanoVGGL? _sharedVg;
    private static readonly object _gate = new();
    private MewVGWin32GraphicsContext? _context;
    private bool _disposed;

    private BrowserTextCache? _textCache;

    private BrowserWindowResources(NanoVGGL vg) => Vg = vg;

    internal NanoVGGL Vg { get; }

    // Resizing recreates the graphics context, so the cache lives with the window resources
    // instead; otherwise every resize frame would re-rasterize the whole screen's text.
    internal BrowserTextCache TextCache => _textCache ??= new BrowserTextCache(Vg);

    internal static BrowserWindowResources Create() => new BrowserWindowResources(SharedVg);

    /// <summary>
    /// The one renderer for the canvas context. Offscreen surfaces render through the same
    /// instance by rebinding the framebuffer; a second instance would duplicate every shader,
    /// buffer and mask target on the shared GL context.
    /// </summary>
    internal static NanoVGGL SharedVg
    {
        get
        {
            lock (_gate)
            {
                if (!_initialized)
                {
                    BrowserNative.PreserveFunctionPointerSignatures();
                    int result = BrowserNative.InitializeContext("#canvas");
                    if (result != 0)
                    {
                        throw new InvalidOperationException($"WebGL2 context creation failed (EMSCRIPTEN_RESULT {result}).");
                    }

                    NanoVGGL.Initialize(BrowserNative.GetProcAddress, NanoVGGLProfile.Gles3);
                    Native.OpenGLExt.EnsureInitialized();
                    _initialized = true;
                    Console.WriteLine("MewUI MewVG WebGL2 initialized.");
                }

                return _sharedVg ??= new NanoVGGL();
            }
        }
    }

    internal MewVGWin32GraphicsContext GetOrCreateContext()
        => _context ??= new MewVGWin32GraphicsContext(this);

    internal void InvalidateContext(MewVGWin32GraphicsContext context)
    {
        if (ReferenceEquals(_context, context))
        {
            _context = null;
        }
    }

    public void TrimCaches() { }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _context?.Dispose();
        _context = null;
        _textCache?.Dispose();
        _textCache = null;
    }
}
