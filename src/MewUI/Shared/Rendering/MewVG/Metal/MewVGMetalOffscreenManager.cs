using Aprillz.MewVG;
using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Provides a pool of <see cref="NanoVGMetal"/> instances bound to a shared
/// <c>MTLDevice</c>, so offscreen passes (filter / pattern tile / cached view
/// bitmap cache) can render into a pixel surface's MTLTexture without
/// disturbing the window's own NVG state.
/// </summary>
/// <remarks>
/// <b>Pool semantics</b>: nested offscreen passes (e.g. a cached bitmap
/// cache renders the document, which in turn invokes a Pattern brush that
/// needs its own offscreen tile) require independent NVG instances — calling
/// <c>nvgBeginFrame</c> on the inner pass would otherwise reset the outer
/// pass's transform / scissor / draw queue, since NanoVG holds only one
/// "frame" worth of state per instance. Each level of nesting acquires a fresh
/// surface and returns it when its render finishes. Returned instances are
/// reused, so the pool grows only to the maximum nesting depth observed.
/// <para/>
/// MTLTextures created on any pool instance interoperate with the window's
/// NVG because they share the same MTLDevice.
/// </remarks>
internal sealed class MewVGMetalOffscreenSurface
{
    internal MewVGMetalOffscreenSurface(nint device, nint commandQueue, NanoVGMetal vg, MewVGMetalTextCache textCache)
    {
        Device = device;
        CommandQueue = commandQueue;
        Vg = vg;
        TextCache = textCache;
    }

    internal nint Device { get; }
    internal nint CommandQueue { get; }
    internal NanoVGMetal Vg { get; }
    internal MewVGMetalTextCache TextCache { get; }
}

internal sealed class MewVGMetalOffscreenSurfaceProvider : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<nint, OffscreenPool> _poolsByDevice = new();
    // Per-NVG queue: see GL provider for the rationale. NVG instance state isn't
    // thread-safe — calling vg.DeleteImage from a thread that doesn't own the NVG
    // (or while the NVG is mid-frame elsewhere) corrupts the image table.
    private readonly Dictionary<NanoVG, Queue<(MewVGImage Image, NVGimageFlags Flags)>> _pendingImageDisposal = new();
    private nint _defaultDevice;
    private nint _filterCommandQueue;
    private bool _disposed;

    public MewVGMetalOffscreenSurfaceProvider() { }

    private sealed class OffscreenPool
    {
        public readonly Stack<MewVGMetalOffscreenSurface> Available = new();
    }

    /// <summary>
    /// Borrows an offscreen NVG instance bound to the given
    /// <paramref name="device"/>, or to the system-default <c>MTLDevice</c>
    /// when <paramref name="device"/> is 0. The returned instance has unique
    /// transform / scissor / draw-queue state for this offscreen pass — safe
    /// to use even when an outer pass is mid-frame on a different borrowed
    /// instance. Caller MUST <see cref="ReturnSurface"/> the same instance when
    /// finished, typically in the offscreen graphics context's Dispose.
    /// </summary>
    internal MewVGMetalOffscreenSurface AcquireSurface(nint device = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        nint key = device != 0 ? device : EnsureDefaultDevice();

        lock (_lock)
        {
            if (!_poolsByDevice.TryGetValue(key, out var pool))
            {
                pool = new OffscreenPool();
                _poolsByDevice[key] = pool;
            }

            if (pool.Available.Count > 0)
            {
                return pool.Available.Pop();
            }
        }

        // Create outside the lock — NanoVGMetal ctor compiles shaders and is
        // expensive; serialising it across all threads is unnecessary. The
        // race only causes a tiny amount of over-allocation: two callers may
        // both find the pool empty and each create a fresh instance, with the
        // second one returned to the pool on first Return.
        nint queue = ObjCRuntime.SendMessage(key, ObjCRuntime.RegisterSelector("newCommandQueue"));
        if (queue == 0)
        {
            throw new InvalidOperationException("Failed to create offscreen MTLCommandQueue.");
        }

        var vg = new NanoVGMetal(key, NVGcreateFlags.Antialias)
        {
            PixelFormat = MTLPixelFormat.BGRA8Unorm,
            StencilFormat = MTLPixelFormat.Depth32Float_Stencil8,
            SampleCount = 0
        };

        var textCache = new MewVGMetalTextCache(vg);
        return new MewVGMetalOffscreenSurface(key, queue, vg, textCache);
    }

    /// <summary>
    /// Returns a borrowed offscreen instance to the pool for reuse.
    /// </summary>
    internal void ReturnSurface(MewVGMetalOffscreenSurface surface)
    {
        if (surface is null)
        {
            return;
        }

        if (_disposed)
        {
            DisposeSurface(surface);
            return;
        }

        nint device = surface.Device;
        if (device == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (!_poolsByDevice.TryGetValue(device, out var pool))
            {
                pool = new OffscreenPool();
                _poolsByDevice[device] = pool;
            }

            pool.Available.Push(surface);
        }
    }

    private nint EnsureDefaultDevice()
    {
        if (_defaultDevice != 0)
        {
            return _defaultDevice;
        }

        _defaultDevice = MetalDevice.CreateSystemDefaultDevice();
        if (_defaultDevice == 0)
        {
            throw new PlatformNotSupportedException(
                "MewVG offscreen rendering requires a system-default MTLDevice.");
        }

        return _defaultDevice;
    }

    /// <summary>
    /// Returns the system-default <c>MTLDevice</c>, allocating it on the first call.
    /// Used by <see cref="MetalImageFilterExecutor"/> to drive standalone command-buffer
    /// passes (MPS blur etc.) that aren't tied to a specific window or offscreen surface.
    /// Returns 0 on failure.
    /// </summary>
    internal nint TryGetDefaultDevice()
    {
        if (_disposed) return 0;
        try
        {
            return EnsureDefaultDevice();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Returns a long-lived <c>MTLCommandQueue</c> on the system-default device, dedicated to
    /// filter passes. Reused across calls so MPS / compute kernels don't pay queue-allocation
    /// cost per blur. Returns 0 if the device isn't available. Owned by the provider — do not
    /// release.
    /// </summary>
    internal nint TryGetFilterCommandQueue()
    {
        if (_disposed) return 0;
        if (_filterCommandQueue != 0) return _filterCommandQueue;

        nint device = TryGetDefaultDevice();
        if (device == 0) return 0;

        lock (_lock)
        {
            if (_filterCommandQueue != 0) return _filterCommandQueue;
            nint queue = ObjCRuntime.SendMessage(device, ObjCRuntime.RegisterSelector("newCommandQueue"));
            if (queue == 0) return 0;
            _filterCommandQueue = queue;
            return queue;
        }
    }

    /// <summary>
    /// Queues a <see cref="MewVGImage"/> for deferred disposal. Splits the image's NVG
    /// entries into per-NVG buckets — each NVG drains its own bucket from its own
    /// EndFrame on the thread that owns it. Without this split, the window NVG's drain
    /// could call <c>vg.DeleteImage</c> on a worker NVG that's mid-frame elsewhere,
    /// corrupting the image table.
    /// </summary>
    internal void QueueImageDisposal(MewVGImage image)
    {
        if (image is null) return;
        if (_disposed)
        {
            image.ReleaseImagesImmediate();
            return;
        }

        var entries = image.SnapshotPendingEntries();
        if (entries.Count == 0)
        {
            // No NVG image-ids — nothing to defer; release inline so the post-release
            // callback (e.g. scratch surface pool return) still fires.
            image.ReleaseImagesImmediate();
            return;
        }

        lock (_lock)
        {
            foreach (var (vg, flags) in entries)
            {
                if (!_pendingImageDisposal.TryGetValue(vg, out var queue))
                {
                    queue = new Queue<(MewVGImage, NVGimageFlags)>();
                    _pendingImageDisposal[vg] = queue;
                }
                queue.Enqueue((image, flags));
            }
        }
    }

    /// <summary>
    /// Drains pending NVG image-id deletions belonging to <paramref name="vg"/>. Call
    /// from that NVG's <c>EndFrame</c> on the thread that owns it. Per-NVG drain is
    /// the safe entry point; <see cref="ReleasePendingImages"/> is for shutdown only.
    /// </summary>
    internal int ReleasePendingImagesForVg(NanoVG vg)
    {
        if (vg is null) return 0;

        int count = 0;
        while (true)
        {
            (MewVGImage Image, NVGimageFlags Flags) entry;
            lock (_lock)
            {
                if (!_pendingImageDisposal.TryGetValue(vg, out var queue) || queue.Count == 0)
                {
                    return count;
                }
                entry = queue.Dequeue();
            }
            entry.Image.ReleasePendingEntry(vg, entry.Flags);
            count++;
        }
    }

    /// <summary>Drains every NVG's bucket — shutdown only, when all NVGs are idle.</summary>
    internal int ReleasePendingImages()
    {
        int count = 0;
        List<(MewVGImage Image, NanoVG Vg, NVGimageFlags Flags)> all = new();
        lock (_lock)
        {
            foreach (var (vg, queue) in _pendingImageDisposal)
            {
                while (queue.Count > 0)
                {
                    var entry = queue.Dequeue();
                    all.Add((entry.Image, vg, entry.Flags));
                }
            }
            _pendingImageDisposal.Clear();
        }
        foreach (var (image, vg, flags) in all)
        {
            image.ReleasePendingEntry(vg, flags);
            count++;
        }
        return count;
    }

    public void Dispose()
    {
        List<MewVGMetalOffscreenSurface> surfaces = new();
        List<(MewVGImage Image, NanoVG Vg, NVGimageFlags Flags)> imageEntries = new();
        nint defaultDevice;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            defaultDevice = _defaultDevice;
            _defaultDevice = 0;

            foreach (var pool in _poolsByDevice.Values)
            {
                while (pool.Available.Count > 0)
                {
                    surfaces.Add(pool.Available.Pop());
                }
            }
            _poolsByDevice.Clear();

            foreach (var (vg, queue) in _pendingImageDisposal)
            {
                while (queue.Count > 0)
                {
                    var entry = queue.Dequeue();
                    imageEntries.Add((entry.Image, vg, entry.Flags));
                }
            }
            _pendingImageDisposal.Clear();
        }

        foreach (var (image, vg, flags) in imageEntries)
        {
            image.ReleasePendingEntry(vg, flags);
        }

        foreach (var item in surfaces)
        {
            DisposeSurface(item);
        }

        if (_filterCommandQueue != 0)
        {
            ObjCRuntime.Release(_filterCommandQueue);
            _filterCommandQueue = 0;
        }

        if (defaultDevice != 0)
        {
            ObjCRuntime.Release(defaultDevice);
        }
    }

    private static void DisposeSurface(MewVGMetalOffscreenSurface surface)
    {
        surface.TextCache.Dispose();
        if (surface.Vg is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (surface.CommandQueue != 0)
        {
            ObjCRuntime.Release(surface.CommandQueue);
        }
    }
}
