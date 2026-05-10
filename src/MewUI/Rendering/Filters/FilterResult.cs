namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// Output of <see cref="IImageFilterExecutor.Execute"/>. Wraps a backend's intermediate image
/// with explicit ownership semantics so callers can <c>using</c>-dispose without worrying about
/// whether the result aliases a borrowed source layer or a scratch RT from the pool.
/// </summary>
/// <remarks>
/// Three concrete shapes:
/// <list type="bullet">
/// <item><see cref="BorrowedFilterResult"/> — points at a layer the caller owns. <see cref="Dispose"/> is no-op.</item>
/// <item><see cref="ScratchFilterResult"/> — backed by an <see cref="IBitmapRenderTarget"/> rented
/// from the executor's scratch pool; <see cref="Dispose"/> returns it.</item>
/// <item>Backend-specific subclasses — wrap native handles (<c>ID2D1Bitmap</c>, NVG image id),
/// optionally with lazy CPU readback for cross-backend chain-of-responsibility fallback.</item>
/// </list>
/// All subclasses must support both <see cref="AsImage"/> (for backend-native draw operations)
/// and <see cref="ReadPixels"/> (for CPU executor handoff). The latter may trigger a GPU→CPU
/// readback, which is the unavoidable cost of mixing GPU and CPU nodes in one graph.
/// </remarks>
public abstract class FilterResult : IDisposable
{
    public abstract int PixelWidth { get; }
    public abstract int PixelHeight { get; }

    /// <summary>
    /// Bounds of this image in the executor's source coordinate space. Initially equals
    /// <see cref="IImageFilterContext.SourceBounds"/>; nodes that translate or extend the
    /// region (Offset, Blur halo) update this so downstream nodes know the spatial layout.
    /// </summary>
    public abstract Rect Bounds { get; }

    /// <summary>
    /// Backend-native image handle for use in draw operations
    /// (<see cref="IGraphicsContext.DrawImage(IImage, Rect)"/> etc.).
    /// </summary>
    public abstract IImage AsImage();

    /// <summary>
    /// CPU-side pixel access (BGRA32). For GPU-backed results, the first call may incur a
    /// GPU→CPU readback. Returned span has stride <paramref name="strideBytes"/> and length
    /// <c>PixelHeight × strideBytes</c>. The buffer is owned by the result and remains valid
    /// until <see cref="Dispose"/>.
    /// </summary>
    public abstract ReadOnlySpan<byte> ReadPixels(out int strideBytes);

    /// <summary>
    /// <see langword="true"/> when the underlying pixels are in premultiplied alpha
    /// (<see cref="IBitmapRenderTarget.IsPremultiplied"/>). Mirrored on the result so CPU
    /// fallback paths know how to interpret <see cref="ReadPixels"/> bytes.
    /// </summary>
    public abstract bool IsPremultiplied { get; }

    /// <summary>
    /// The underlying render surface if this result is backed by one (Borrowed/Scratch).
    /// Returns <see langword="null"/> for backend-specific results that wrap native handles only.
    /// Used by GPU executors to access backend-specific resources (e.g. OpenGL FBO/texture).
    /// </summary>
    public abstract IRenderSurface? UnderlyingSurface { get; }

    /// <summary>
    /// Bitmap view of <see cref="UnderlyingSurface"/> when the result is backed by a bitmap-capable
    /// surface. CPU fallback paths use this while filter internals migrate to surface capabilities.
    /// </summary>
    public abstract IBitmapRenderTarget? UnderlyingTarget { get; }

    public abstract void Dispose();
}

/// <summary>
/// A <see cref="FilterResult"/> that aliases an externally-owned image (typically the
/// executor's source layer). <see cref="Dispose"/> is intentionally a no-op — the caller
/// must not release the underlying resource through this wrapper.
/// </summary>
public sealed class BorrowedFilterResult : FilterResult
{
    private readonly IImage _image;
    private readonly IBitmapRenderTarget? _pixelSource;
    private readonly IRenderSurface? _surface;

    public BorrowedFilterResult(IImage image, Rect bounds, IRenderSurface? surface = null, IBitmapRenderTarget? pixelSource = null)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _surface = surface ?? pixelSource;
        _pixelSource = pixelSource;
        Bounds = bounds;
        PixelWidth = image.PixelWidth;
        PixelHeight = image.PixelHeight;
    }

    public override int PixelWidth { get; }
    public override int PixelHeight { get; }
    public override Rect Bounds { get; }
    public override bool IsPremultiplied => _pixelSource?.IsPremultiplied ?? false;
    public override IRenderSurface? UnderlyingSurface => _surface;
    public override IBitmapRenderTarget? UnderlyingTarget => _pixelSource;

    public override IImage AsImage() => _image;

    public override ReadOnlySpan<byte> ReadPixels(out int strideBytes)
    {
        if (_pixelSource is null)
        {
            strideBytes = 0;
            return ReadOnlySpan<byte>.Empty;
        }

        strideBytes = _pixelSource.StrideBytes;
        return _pixelSource.GetPixelSpan();
    }

    public override void Dispose() { }
}

/// <summary>
/// A <see cref="FilterResult"/> backed by a scratch <see cref="IBitmapRenderTarget"/> rented
/// from a pool. <see cref="Dispose"/> returns the target via the supplied release callback.
/// </summary>
public sealed class ScratchFilterResult : FilterResult, IPixelTargetAccess
{
    IBitmapRenderTarget IPixelTargetAccess.Target => _target;

    private readonly IRenderSurface _surface;
    private readonly IBitmapRenderTarget _target;
    private readonly IImage _image;
    private readonly Action<IBitmapRenderTarget>? _release;
    private readonly ScratchRenderTargetLease? _lease;
    private readonly Action<ScratchRenderTargetLease>? _releaseLease;
    private bool _disposed;

    public ScratchFilterResult(IBitmapRenderTarget target, IImage image, Rect bounds,
        Action<IBitmapRenderTarget>? release)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _surface = target;
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _release = release;
        Bounds = bounds;
    }

    public ScratchFilterResult(ScratchRenderTargetLease lease, IImage image, Rect bounds,
        Action<ScratchRenderTargetLease>? release)
    {
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _surface = lease.Surface;
        _target = lease.BitmapTarget;
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _releaseLease = release;
        Bounds = bounds;
    }

    public override int PixelWidth => _target.PixelWidth;
    public override int PixelHeight => _target.PixelHeight;
    public override Rect Bounds { get; }
    public override bool IsPremultiplied => _target.IsPremultiplied;
    public override IRenderSurface? UnderlyingSurface => _surface;
    public override IBitmapRenderTarget? UnderlyingTarget => _target;

    public override IImage AsImage() => _image;

    public override ReadOnlySpan<byte> ReadPixels(out int strideBytes)
    {
        strideBytes = _target.StrideBytes;
        return _target.GetPixelSpan();
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_lease is not null)
        {
            _releaseLease?.Invoke(_lease);
        }
        else
        {
            _release?.Invoke(_target);
        }
    }

    /// <summary>Transfers ownership of the underlying target + image to the caller. After
    /// Detach, <see cref="Dispose"/> is a no-op (the pool release is suppressed). Caller
    /// must dispose the returned surface/image when done. Used by result-caching paths that
    /// want to keep the scratch RT alive across frames without copying its pixels.</summary>
    public (IRenderSurface Surface, IBitmapRenderTarget Target, IImage Image)? Detach()
    {
        if (_disposed) return null;
        if (_lease is null) return null;
        _disposed = true;
        return (_lease.Surface, _target, _image);
    }
}
