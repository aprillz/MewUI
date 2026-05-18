using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal interface IMewVGOffscreenSurfaceProvider : IDisposable
{
    MewVGGLOffscreenSurface AcquireSurface();
    void ReturnSurface(MewVGGLOffscreenSurface surface);
    void QueueTargetDisposal(OpenGLPixelRenderSurface target);
    void ReleasePendingTargetsUnderCurrentContext();
    void QueueImageDisposal(MewVGImage image);

    /// <summary>
    /// Drains pending NVG image-id deletions belonging to <paramref name="vg"/>. Call from
    /// that NVG's <c>EndFrame</c> on the thread that owns the NVG. This is the per-NVG
    /// safe-drain entry point — preferred over <see cref="ReleasePendingImages"/>, which
    /// fans out to every NVG and is therefore unsafe when other threads are mid-frame.
    /// </summary>
    int ReleasePendingImagesForVg(NanoVG vg);

    /// <summary>
    /// Drains every NVG's pending bucket regardless of owning thread. Use only at shutdown
    /// (provider <c>Dispose</c>) where all NVG instances are guaranteed to be idle.
    /// </summary>
    int ReleasePendingImages();

    /// <summary>Tracks render-session nesting. Pending FBO target disposals only drain
    /// when the outermost session ends, since nested sessions (e.g. filter source
    /// layers) may have wrapped scratch FBO textures via CreateImageFromHandle into the
    /// outer NVG's deferred draw queue — those textures must outlive the outer flush.</summary>
    void EnterSession();
    /// <summary>Returns true if this was the outermost session (caller should drain).</summary>
    bool ExitSession();
}

internal sealed class MewVGGLOffscreenSurface
{
    internal MewVGGLOffscreenSurface(nint nativeContext, NanoVGGL vg, MewVGTextCache textCache)
    {
        NativeContext = nativeContext;
        Vg = vg;
        TextCache = textCache;
    }

    internal nint NativeContext { get; }
    internal NanoVGGL Vg { get; }
    internal MewVGTextCache TextCache { get; }
}

internal sealed class MewVGGLOffscreenSurfaceProvider : IMewVGOffscreenSurfaceProvider
{
    private readonly Func<nint> _getCurrentContext;
    private readonly object _lock = new();
    private readonly Dictionary<nint, SurfacePool> _poolsByContext = new();
    // List, not Queue, because the drain filters by each target's CreationContext —
    // a target queued by the worker thread (FBO created on the worker HGLRC) must
    // not be dequeued and released by the UI thread (running under the window
    // HGLRC), since FBOs aren't shared. Walking the list lets us pick out only the
    // entries whose owning context matches the current one.
    private readonly List<OpenGLPixelRenderSurface> _pendingTargetDisposal = new();
    // Per-NVG queue: each NVG drains only its own bucket from its EndFrame on the thread
    // that owns it. NanoVG instance state (image table, draw queue, transform stack) is not
    // thread-safe — calling vg.DeleteImage on a NVG that's mid-frame on another thread
    // corrupts the image table and surfaces as wrong-texture binding on the next flush.
    // The previous flat queue let UI's EndFrame drain images whose NVG was the worker's,
    // racing the worker mid-frame.
    private readonly Dictionary<NanoVG, Queue<(MewVGImage Image, NVGimageFlags Flags)>> _pendingImageDisposal = new();
    private int _sessionDepth;
    private bool _disposed;

    public void EnterSession()
    {
        lock (_lock)
        {
            _sessionDepth++;
        }
    }

    public bool ExitSession()
    {
        lock (_lock)
        {
            if (_sessionDepth > 0)
            {
                _sessionDepth--;
            }
            return _sessionDepth == 0;
        }
    }

    public MewVGGLOffscreenSurfaceProvider(Func<nint> getCurrentContext)
    {
        _getCurrentContext = getCurrentContext ?? throw new ArgumentNullException(nameof(getCurrentContext));
    }

    private sealed class SurfacePool
    {
        public readonly Stack<MewVGGLOffscreenSurface> Available = new();
    }

    public MewVGGLOffscreenSurface AcquireSurface()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        nint nativeContext = _getCurrentContext();
        if (nativeContext == 0)
        {
            throw new InvalidOperationException(
                "Offscreen rendering requires a current OpenGL context on the calling thread.");
        }

        lock (_lock)
        {
            if (!_poolsByContext.TryGetValue(nativeContext, out var pool))
            {
                pool = new SurfacePool();
                _poolsByContext[nativeContext] = pool;
            }

            if (pool.Available.Count > 0)
            {
                return pool.Available.Pop();
            }
        }

        var vg = new NanoVGGL(NVGcreateFlags.Antialias);
        var textCache = new MewVGTextCache(vg);
        return new MewVGGLOffscreenSurface(nativeContext, vg, textCache);
    }

    public void ReturnSurface(MewVGGLOffscreenSurface surface)
    {
        if (_disposed)
        {
            DisposeSurface(surface);
            return;
        }

        if (surface.NativeContext == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (!_poolsByContext.TryGetValue(surface.NativeContext, out var pool))
            {
                pool = new SurfacePool();
                _poolsByContext[surface.NativeContext] = pool;
            }

            pool.Available.Push(surface);
        }
    }

    public void QueueTargetDisposal(OpenGLPixelRenderSurface target)
    {
        if (target is null)
        {
            return;
        }

        if (_disposed)
        {
            if (_getCurrentContext() != 0)
            {
                target.ReleaseGLResources();
            }
            return;
        }

        lock (_lock)
        {
            _pendingTargetDisposal.Add(target);
        }
    }

    public void ReleasePendingTargetsUnderCurrentContext()
    {
        nint current = _getCurrentContext();
        if (current == 0)
        {
            return;
        }

        while (true)
        {
            OpenGLPixelRenderSurface? target = null;
            lock (_lock)
            {
                // Find the first pending target whose creation context matches the
                // currently-active GL context. Targets created on a different context
                // (worker FBOs vs. UI window FBOs) stay queued until that context's
                // own drain runs. CreationContext == 0 means the target's FBO never
                // initialized; safe to release under any context (no-op).
                for (int i = 0; i < _pendingTargetDisposal.Count; i++)
                {
                    var pending = _pendingTargetDisposal[i];
                    nint owner = pending.CreationContext;
                    if (owner == 0 || owner == current)
                    {
                        target = pending;
                        _pendingTargetDisposal.RemoveAt(i);
                        break;
                    }
                }
                if (target is null)
                {
                    return;
                }
            }

            target.ReleaseGLResources();
        }
    }

    public void QueueImageDisposal(MewVGImage image)
    {
        if (image is null)
        {
            return;
        }

        if (_disposed)
        {
            image.ReleaseImagesImmediate();
            return;
        }

        var entries = image.SnapshotPendingEntries();
        if (entries.Count == 0)
        {
            // No NVG image-ids attached — nothing to defer; release inline so the
            // post-release callback (e.g. scratch surface pool return) still fires.
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

    public int ReleasePendingImagesForVg(NanoVG vg)
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

            // Per-entry release: deletes only this (vg, flags) image-id from the image's
            // dict. When the dict empties, the image fires its FinalizeRelease (retain
            // release + post-release callback). Safe to call vg.DeleteImage here because
            // we're inside vg's own EndFrame on its owning thread.
            entry.Image.ReleasePendingEntry(vg, entry.Flags);
            count++;
        }
    }

    public int ReleasePendingImages()
    {
        // Shutdown / fallback path — drain every NVG's bucket. Caller must guarantee no
        // NVG is mid-frame (e.g. provider Dispose at process teardown).
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
        List<MewVGGLOffscreenSurface> surfaces = new();
        List<(MewVGImage Image, NanoVG Vg, NVGimageFlags Flags)> imageEntries = new();
        List<OpenGLPixelRenderSurface> targets = new();

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var pool in _poolsByContext.Values)
            {
                while (pool.Available.Count > 0)
                {
                    surfaces.Add(pool.Available.Pop());
                }
            }
            _poolsByContext.Clear();

            foreach (var (vg, queue) in _pendingImageDisposal)
            {
                while (queue.Count > 0)
                {
                    var entry = queue.Dequeue();
                    imageEntries.Add((entry.Image, vg, entry.Flags));
                }
            }
            _pendingImageDisposal.Clear();

            targets.AddRange(_pendingTargetDisposal);
            _pendingTargetDisposal.Clear();
        }

        foreach (var (image, vg, flags) in imageEntries)
        {
            image.ReleasePendingEntry(vg, flags);
        }

        foreach (var surface in surfaces)
        {
            DisposeSurface(surface);
        }

        nint currentContext = _getCurrentContext();
        foreach (var target in targets)
        {
            // Best-effort release: skip targets whose creation context is non-zero
            // and doesn't match the current — releasing under the wrong context is
            // a silent no-op anyway and would leak. The factory's full Dispose path
            // sees this only when the host process is tearing down, so the leak is
            // bounded.
            nint owner = target.CreationContext;
            if (currentContext == 0)
            {
                continue;
            }
            if (owner == 0 || owner == currentContext)
            {
                target.ReleaseGLResources();
            }
        }
    }

    private static void DisposeSurface(MewVGGLOffscreenSurface surface)
    {
        surface.TextCache.Dispose();
        if (surface.Vg is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
