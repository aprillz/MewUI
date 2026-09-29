using Aprillz.MewVG;
using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Provides a pool of <see cref="MewVGMetal"/> instances bound to a shared
/// <c>MTLDevice</c>, so offscreen passes (filter / pattern tile / cached view
/// bitmap cache) can render into a pixel surface's MTLTexture without
/// disturbing the window's own NVG state.
/// </summary>
/// <remarks>
/// <b>Pool semantics</b>: nested offscreen passes (e.g. a cached bitmap
/// cache renders the document, which in turn invokes a Pattern brush that
/// needs its own offscreen tile) require independent NVG instances - calling
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
    internal MewVGMetalOffscreenSurface(nint device, nint commandQueue, MewVGMetal vg, MewVGMetalTextCache textCache)
    {
        Device = device;
        CommandQueue = commandQueue;
        Vg = vg;
        TextCache = textCache;
    }

    internal nint Device { get; }
    internal nint CommandQueue { get; }
    internal MewVGMetal Vg { get; }
    internal MewVGMetalTextCache TextCache { get; }

    // Command buffers this instance's renderer submitted that may still run on the GPU, oldest first.
    private readonly Queue<nint> _framesInFlight = new();

    // Below the renderer's buffer count, so a frame never refills a vertex buffer the GPU still reads.
    private const int MAX_FRAMES_IN_FLIGHT = MewVGMetal.BufferCount - 1;

    private static readonly nint _selRetain = ObjCRuntime.RegisterSelector("retain");
    private static readonly nint _selRelease = ObjCRuntime.RegisterSelector("release");
    private static readonly nint _selWaitUntilCompleted = ObjCRuntime.RegisterSelector("waitUntilCompleted");

    /// <summary>Records a frame submitted by <see cref="Vg"/>, first waiting for the oldest ones beyond what its buffers allow.</summary>
    internal void TrackFrame(nint commandBuffer)
    {
        ObjCRuntime.SendMessageNoReturn(commandBuffer, _selRetain);
        _framesInFlight.Enqueue(commandBuffer);
        while (_framesInFlight.Count > MAX_FRAMES_IN_FLIGHT)
        {
            WaitForOldestFrame();
        }
    }

    /// <summary>Waits until every frame submitted by <see cref="Vg"/> has finished on the GPU.</summary>
    internal void WaitForFrames()
    {
        while (_framesInFlight.Count > 0)
        {
            WaitForOldestFrame();
        }
    }

    private void WaitForOldestFrame()
    {
        nint oldest = _framesInFlight.Dequeue();
        ObjCRuntime.SendMessageNoReturn(oldest, _selWaitUntilCompleted);
        ObjCRuntime.SendMessageNoReturn(oldest, _selRelease);
    }
}

internal sealed class MewVGMetalOffscreenSurfaceProvider : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<nint, OffscreenPool> _poolsByDevice = new();
    // Per-NVG queue: see GL provider for the rationale. NVG instance state isn't
    // thread-safe - calling vg.DeleteImage from a thread that doesn't own the NVG
    // (or while the NVG is mid-frame elsewhere) corrupts the image table.
    private readonly Dictionary<MewVGContext, Queue<(MewVGImage Image, MewVGImageFlags Flags)>> _pendingImageDisposal = new();
    // NVGs disposed with their window: an entry queued for one afterwards has no NVG left to drain it.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<MewVGContext, object> _retiredVgs = new();
    private nint _defaultDevice;
    // One queue per device, shared by every offscreen NVG instance and by the filter passes.
    // Same-queue command buffers execute in commit order, so a pass that samples what the
    // previous pass rendered needs no completion wait.
    private readonly Dictionary<nint, nint> _sharedQueuesByDevice = new();
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
    /// transform / scissor / draw-queue state for this offscreen pass - safe
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

        // Create outside the lock - MewVGMetal ctor compiles shaders and is
        // expensive; serialising it across all threads is unnecessary. The
        // race only causes a tiny amount of over-allocation: two callers may
        // both find the pool empty and each create a fresh instance, with the
        // second one returned to the pool on first Return.
        nint queue = TryGetSharedCommandQueue(key);
        if (queue == 0)
        {
            throw new InvalidOperationException("Failed to create offscreen MTLCommandQueue.");
        }

        var vg = new MewVGMetal(key)
        {
            PixelFormat = MTLPixelFormat.BGRA8Unorm
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
    /// cost per blur. Returns 0 if the device isn't available. Owned by the provider - do not
    /// release.
    /// </summary>
    internal nint TryGetFilterCommandQueue() => TryGetSharedCommandQueue(0);

    /// <summary>The device's shared offscreen/filter queue, created on first use. Owned by the
    /// provider - do not release.</summary>
    internal nint TryGetSharedCommandQueue(nint device)
    {
        if (_disposed) return 0;

        nint key = device != 0 ? device : TryGetDefaultDevice();
        if (key == 0) return 0;

        lock (_lock)
        {
            if (_sharedQueuesByDevice.TryGetValue(key, out nint existing)) return existing;
            nint queue = ObjCRuntime.SendMessage(key, ObjCRuntime.RegisterSelector("newCommandQueue"));
            if (queue == 0) return 0;
            _sharedQueuesByDevice[key] = queue;
            return queue;
        }
    }

    /// <summary>
    /// Queues a <see cref="MewVGImage"/> for deferred disposal. Splits the image's NVG
    /// entries into per-NVG buckets - each NVG drains its own bucket from its own
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
            // No NVG image-ids - nothing to defer; release inline so the post-release
            // callback (e.g. scratch surface pool return) still fires.
            image.ReleaseImagesImmediate();
            return;
        }

        List<(MewVGContext Vg, MewVGImageFlags Flags)>? retired = null;
        lock (_lock)
        {
            foreach (var (vg, flags) in entries)
            {
                if (_retiredVgs.TryGetValue(vg, out _))
                {
                    (retired ??= []).Add((vg, flags));
                    continue;
                }

                if (!_pendingImageDisposal.TryGetValue(vg, out var queue))
                {
                    queue = new Queue<(MewVGImage, MewVGImageFlags)>();
                    _pendingImageDisposal[vg] = queue;
                }
                queue.Enqueue((image, flags));
            }
        }

        if (retired != null)
        {
            foreach (var (vg, flags) in retired)
            {
                image.ReleaseEntryOfDisposedVg(vg, flags);
            }
        }
    }

    /// <summary>
    /// Drains what is queued for <paramref name="vg"/> and retires it, so nothing queued for it
    /// afterwards waits on a drain that never comes. Call before disposing it, on its owning thread.
    /// </summary>
    internal void RetireVg(MewVGContext vg)
    {
        ReleasePendingImagesForVg(vg);
        lock (_lock)
        {
            _pendingImageDisposal.Remove(vg);
            _retiredVgs.AddOrUpdate(vg, vg);
        }
    }

    /// <summary>
    /// Drains pending NVG image-id deletions belonging to <paramref name="vg"/>. Call
    /// from that NVG's <c>EndFrame</c> on the thread that owns it. Per-NVG drain is
    /// the safe entry point; <see cref="ReleasePendingImages"/> is for shutdown only.
    /// </summary>
    internal int ReleasePendingImagesForVg(MewVGContext vg)
    {
        if (vg is null) return 0;

        int count = 0;
        while (true)
        {
            (MewVGImage Image, MewVGImageFlags Flags) entry;
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

    /// <summary>Drains every NVG's bucket - shutdown only, when all NVGs are idle.</summary>
    internal int ReleasePendingImages()
    {
        int count = 0;
        List<(MewVGImage Image, MewVGContext Vg, MewVGImageFlags Flags)> all = new();
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
        List<(MewVGImage Image, MewVGContext Vg, MewVGImageFlags Flags)> imageEntries = new();
        List<nint> sharedQueues = new();
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

            sharedQueues.AddRange(_sharedQueuesByDevice.Values);
            _sharedQueuesByDevice.Clear();

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

        foreach (nint queue in sharedQueues)
        {
            ObjCRuntime.Release(queue);
        }

        if (defaultDevice != 0)
        {
            ObjCRuntime.Release(defaultDevice);
        }
    }

    private static void DisposeSurface(MewVGMetalOffscreenSurface surface)
    {
        surface.WaitForFrames();
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
