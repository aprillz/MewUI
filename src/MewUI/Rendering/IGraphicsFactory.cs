using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Factory interface for creating graphics resources.
/// Allows different graphics backends to be plugged in.
/// </summary>
public interface IGraphicsFactory : IRenderDevice, IDisposable
{
    /// <summary>Gets the retained text layout engine associated with this backend factory.</summary>
    ITextEngine TextEngine => TextServices.GetEngine(this);

    /// <summary>
    /// Backend name matching the package suffix (<c>Aprillz.MewUI.Backend.&lt;Backend&gt;</c>).
    /// Do not branch on this value.
    /// </summary>
    string Backend { get; }

    /// <summary>
    /// Whether presenting a frame is paced by the display's refresh. A backend that reports false leaves
    /// <see cref="RenderLoopSettings.VSyncEnabled"/> with nothing to act on, so the render loop caps the
    /// frame rate itself instead of spinning.
    /// </summary>
    bool SupportsVSync => true;

    /// <summary>
    /// Creates a font resource.
    /// </summary>
    IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false);

    /// <summary>
    /// Creates a font resource for a specific DPI.
    /// Font size is specified in DIPs (1/96 inch).
    /// </summary>
    IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false);

    /// <summary>
    /// Creates a graphics context for the specified render target.
    /// The returned context is not yet started; call
    /// <see cref="IGraphicsContext.BeginFrame"/> before drawing.
    /// </summary>
    /// <param name="target">The render target to draw to.</param>
    /// <returns>A new graphics context.</returns>
    IGraphicsContext CreateContext(IRenderTarget target);

    /// <summary>
    /// Creates a measurement-only graphics context for text measurement.
    /// </summary>
    /// <summary>
    /// Creates an executor for evaluating <see cref="ImageFilter"/> graphs. The default
    /// returns a CPU reference implementation; backends override to return GPU-accelerated
    /// executors that internally chain CPU fallback for unsupported nodes.
    /// </summary>
    IImageFilterExecutor CreateImageFilterExecutor() => new CpuImageFilterExecutor();

    /// <summary>
    /// Serializes worker-thread offline render units against UI window frames. Backends
    /// with share-listed GPU contexts override this to acquire the same mutex their UI
    /// <c>BeginFrame</c> / <c>EndFrame</c> path holds, so filter offline renders and UI
    /// renders don't overlap (overlap races on some drivers produce intermittent black
    /// filter regions). Backends with thread-safe APIs return a no-op disposable.
    /// </summary>
    IDisposable AcquireConcurrentRenderUnit() => NoOpScope.Instance;

    /// <summary>
    /// Reserves any per-thread state needed to perform rendering on the calling thread.
    /// Required for backends with thread-affine state (context-current style APIs).
    /// Backends with thread-safe APIs return a no-op disposable.
    /// <para/>
    /// Use this from worker threads that call <see cref="IRenderDevice.CreateSurface"/> /
    /// <see cref="IRenderDevice.CreateContext(IRenderSurface)"/>:
    /// <code>
    /// await Task.Run(() =&gt; {
    ///     using var _ = factory.AcquireBackgroundRenderScope();
    ///     var surface = factory.CreateSurface(...);
    ///     using var ctx = factory.CreateContext(surface);
    ///     // draw...
    /// });
    /// </code>
    /// Backends with a shared session/context that offscreen and UI-frame rendering would
    /// otherwise contend over route background surfaces to a separate per-thread context here.
    /// </summary>
    IDisposable AcquireBackgroundRenderScope() => NoOpScope.Instance;

    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();

        public void Dispose() { }
    }
}

/// <summary>Encoded-image helpers kept outside the backend dispatch contract for NativeAOT pay-for-play.</summary>
public static class GraphicsFactoryImageExtensions
{
    /// <summary>Creates an image from a file path.</summary>
    public static IImage CreateImageFromFile(this IGraphicsFactory factory, string path)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return ImageSource.FromFile(path).CreateImage(factory);
    }

    /// <summary>Creates an image from encoded bytes.</summary>
    public static IImage CreateImageFromBytes(this IGraphicsFactory factory, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return ImageSource.FromBytes(data).CreateImage(factory);
    }
}

/// <summary>Optional custom encoded-image capability used after built-in decoding fails.</summary>
public interface IEncodedImageFactory
{
    /// <summary>Creates an image from an encoded payload understood by the custom factory.</summary>
    IImage CreateImageFromBytes(byte[] data);
}

/// <summary>
/// Optional capability for factories that must release per-window resources when a window is destroyed.
/// </summary>
public interface IWindowResourceReleaser
{
    void ReleaseWindowResources(nint windowHandle);
}
