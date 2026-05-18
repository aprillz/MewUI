using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Per-dimension pool of <see cref="PboFenceUploader"/> instances. Reuses the GL
/// texture + PBO ring across many short-lived <see cref="IPixelBufferSource"/>
/// objects (e.g. video frames recycled per decoded frame), avoiding the alloc/free
/// churn that would otherwise dominate the PBO upload path.
/// </summary>
/// <remarks>
/// Without pooling, each video frame allocates a new 4K BGRA texture (33 MB) plus
/// two PBOs (33 MB each) and frees them next frame — driver-side allocator overhead
/// then exceeds the saved sync upload cost, making PBO upload measurably worse than
/// the plain <c>glTexSubImage2D</c> path. Pool keeps the same set of uploaders alive
/// across frames; <see cref="PboFenceUploader.Rebind"/> swaps the source pointer and
/// triggers a fresh upload.
/// <para/>
/// Pool key is the (PixelWidth, PixelHeight) tuple — sources of the same dimensions
/// share a bucket. Per-bucket cap (<see cref="MaxPerBucket"/>) bounds memory under
/// "many one-shot" workloads; overflow uploaders are disposed immediately on return.
/// </remarks>
internal sealed class PboFenceUploaderPool : IDisposable
{
    private readonly Dictionary<(int Width, int Height), Stack<PboFenceUploader>> _buckets = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Maximum retained uploaders per (width, height) bucket.</summary>
    public int MaxPerBucket { get; init; } = 4;

    /// <summary>
    /// Take an uploader matching <paramref name="source"/>'s dimensions from the pool,
    /// rebound to the new source. Returns a freshly-constructed uploader if the bucket
    /// is empty.
    /// </summary>
    public PboFenceUploader Rent(IPixelBufferSource source)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PboFenceUploaderPool));

        var key = (source.PixelWidth, source.PixelHeight);
        lock (_lock)
        {
            if (_buckets.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                var uploader = stack.Pop();
                uploader.Rebind(source);
                return uploader;
            }
        }

        return new PboFenceUploader(source);
    }

    /// <summary>
    /// Return an uploader to the pool for reuse. If the bucket is at capacity, the
    /// uploader is disposed immediately.
    /// </summary>
    public void Return(PboFenceUploader uploader)
    {
        if (uploader is null) return;

        if (_disposed)
        {
            uploader.Dispose();
            return;
        }

        var key = (uploader.PixelWidth, uploader.PixelHeight);
        lock (_lock)
        {
            if (!_buckets.TryGetValue(key, out var stack))
            {
                stack = new Stack<PboFenceUploader>();
                _buckets[key] = stack;
            }

            if (stack.Count >= MaxPerBucket)
            {
                uploader.Dispose();
                return;
            }

            stack.Push(uploader);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            foreach (var stack in _buckets.Values)
            {
                while (stack.Count > 0)
                {
                    stack.Pop().Dispose();
                }
            }
            _buckets.Clear();
        }
    }
}

/// <summary>
/// Thin <see cref="IExternalRasterSource"/> wrapper that returns its inner
/// <see cref="PboFenceUploader"/> to a pool on <see cref="Dispose"/> instead of
/// destroying the underlying GL resources. Used by the factory so that an
/// external raster image transparently routes the disposal to the pool.
/// </summary>
internal sealed class PooledPboTexture : IExternalRasterSource
{
    private PboFenceUploader? _inner;
    private readonly PboFenceUploaderPool _pool;

    public PooledPboTexture(PboFenceUploader inner, PboFenceUploaderPool pool)
    {
        _inner = inner;
        _pool = pool;
    }

    public int PixelWidth => _inner?.PixelWidth ?? 0;
    public int PixelHeight => _inner?.PixelHeight ?? 0;
    public int Version => _inner?.Version ?? 0;
    public RenderPixelFormat Format => _inner?.Format ?? RenderPixelFormat.Bgra8888;
    public BitmapAlphaMode AlphaMode => _inner?.AlphaMode ?? BitmapAlphaMode.Ignore;
    public bool YFlipped => _inner?.YFlipped ?? false;
    public SurfaceCapabilities Capabilities => _inner?.Capabilities ?? SurfaceCapabilities.None;
    public IReadOnlyList<ExternalRasterPlane> Planes => _inner?.Planes ?? Array.Empty<ExternalRasterPlane>();

    public IExternalRasterLease Acquire()
        => _inner?.Acquire() ?? EmptyRasterLease.Instance;

    public void Dispose()
    {
        if (_inner is null) return;
        _pool.Return(_inner);
        _inner = null;
    }

    private sealed class EmptyRasterLease : IExternalRasterLease
    {
        public static readonly EmptyRasterLease Instance = new();
        public int PixelWidth => 0;
        public int PixelHeight => 0;
        public bool YFlipped => false;
        public nint NativeHandle => 0;
        public nint NativeAlternateHandle => 0;
        public void Dispose() { }
    }
}
