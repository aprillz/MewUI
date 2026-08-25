namespace Aprillz.MewUI.Rendering;

public sealed class RenderResourceCache : IRenderResourceCache, IDisposable
{
    private const long DEFAULT_PERSISTENT_BUDGET_BYTES = 256L * 1024 * 1024;
    private const int DEFAULT_PERSISTENT_MAX_COUNT = 512;
    private const int NORMAL_MAINTENANCE_EVICTION_LIMIT = 32;
    private const long PERSISTENT_IDLE_EVICT_MS = 5_000;
    // Scratch pool ceilings. The count ceiling is sized ABOVE the working set of a maximized
    // icon grid (a QHD window shows ~2,300 tiles): evicting below the active working set makes
    // every presenter rebind destroy and recreate GPU surfaces in bulk, which costs far more
    // than the retention it saves. The byte budget only has to cover that same working set,
    // which is small because grid tiles are small; a window-sized surface pooled during a
    // resize is what the budget is there to throw away, since every resize step asks for a
    // size the pool has never seen (measured on a 600-step sweep: 129 hits against 383 misses,
    // identical step times at 320 MB and at 16 MB, 344 MB less retained).
    private const long SCRATCH_BUDGET_BYTES = 16L * 1024 * 1024;
    private const int SCRATCH_MAX_COUNT = 512;
    private const long SCRATCH_MAX_EXTRA_BYTES = 8L * 1024 * 1024;
    private const int SCRATCH_MAX_OVERSIZE_CANDIDATES = 32;

    // A pooled surface untouched this long is dead weight (the view that used it is gone), so a
    // periodic sweep disposes it. This is what returns memory after scrolling stops, without
    // ever evicting the surfaces an active grid is cycling through.
    private const long SCRATCH_IDLE_EVICT_MS = 5_000;
    private const long SCRATCH_SWEEP_INTERVAL_MS = 1_000;
    private long _scratchLastSweepTicks;

    private readonly object _gate = new();
    private readonly Dictionary<RenderCacheKey, CachedRenderResource> _entries = new();
    private readonly List<PendingRelease> _pendingReleases = new();
    private readonly long _persistentBudgetBytes;
    private readonly long _persistentLowWatermarkBytes;
    private readonly int _persistentMaxCount;
    private readonly Func<long> _tickProvider;
    private long _persistentBytes;
    private long _persistentUseSequence;
    private long _atlasActiveBytes;

    // Scratch buckets are lists: several surfaces can share one key. Rent pops the warm end.
    private readonly Dictionary<ScratchSurfaceKey, List<PooledScratchSurface>> _scratchBuckets = new();
    private long _scratchBytes;
    private long _scratchReturnSequence;
    private bool _disposed;

    public RenderResourceCache()
        : this(DEFAULT_PERSISTENT_BUDGET_BYTES, DEFAULT_PERSISTENT_MAX_COUNT)
    {
    }

    internal RenderResourceCache(
        long persistentBudgetBytes,
        int persistentMaxCount,
        Func<long>? tickProvider = null)
    {
        _persistentBudgetBytes = Math.Max(1, persistentBudgetBytes);
        _persistentLowWatermarkBytes = Math.Max(1, _persistentBudgetBytes * 4 / 5);
        _persistentMaxCount = Math.Max(1, persistentMaxCount);
        _tickProvider = tickProvider ?? (() => Environment.TickCount64);
    }

    public bool TryGet(RenderCacheKey key, out IRenderCacheEntry entry)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            DrainCompletedReleases_NoLock();
            if (_entries.TryGetValue(key, out var cached))
            {
                if (cached.SafeToDisposeAfter is null || cached.SafeToDisposeAfter.IsCompleted)
                {
                    cached.LastUseSequence = ++_persistentUseSequence;
                    entry = cached.Acquire(this);
                    return true;
                }
            }
        }

        entry = null!;
        return false;
    }

    public IRenderCacheEntry Add(
        RenderCacheKey key,
        IRenderSurface surface,
        IImage image,
        IRenderOperation? safeToDisposeAfter = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(image);

        lock (_gate)
        {
            ThrowIfDisposed();
            DrainCompletedReleases_NoLock();

            if (_entries.Remove(key, out var existing))
            {
                RetireEntry_NoLock(existing);
            }

            var resource = AddNoLock(key, surface, image, safeToDisposeAfter);
            EvictPersistentToBudgetNoLock(NORMAL_MAINTENANCE_EVICTION_LIMIT, resource);
            return resource.Acquire(this);
        }
    }

    public bool TryAdd(
        RenderCacheKey key,
        IRenderSurface surface,
        IImage image,
        out IRenderCacheEntry entry,
        IRenderOperation? safeToDisposeAfter = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(image);

        lock (_gate)
        {
            ThrowIfDisposed();
            DrainCompletedReleases_NoLock();

            if (_entries.Remove(key, out var existing))
            {
                RetireEntry_NoLock(existing);
            }

            long incomingBytes = RenderResourceMetrics.ScratchBytes(surface.PixelWidth, surface.PixelHeight);
            if (incomingBytes > _persistentBudgetBytes)
            {
                entry = null!;
                return false;
            }

            EvictPersistentForAdmissionNoLock(incomingBytes, incomingCount: 1);
            if (BudgetedNativeBytesNoLock() + incomingBytes > _persistentBudgetBytes
                || _entries.Count >= _persistentMaxCount)
            {
                entry = null!;
                return false;
            }

            var resource = AddNoLock(key, surface, image, safeToDisposeAfter);
            entry = resource.Acquire(this);
            return true;
        }
    }

    public void Release(RenderCacheKey key)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_entries.Remove(key, out var existing))
            {
                RetireEntry_NoLock(existing);
            }
        }
    }

    public void ReleaseLater(IDisposable resource, IRenderOperation safeAfter)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(safeAfter);

        lock (_gate)
        {
            if (_disposed)
            {
                try
                {
                    safeAfter.Wait();
                }
                finally
                {
                    resource.Dispose();
                    safeAfter.Dispose();
                }
                return;
            }

            if (safeAfter.IsCompleted)
            {
                resource.Dispose();
                safeAfter.Dispose();
            }
            else
            {
                long bytes = EstimateResourceBytes(resource);
                _pendingReleases.Add(new PendingRelease(resource, safeAfter, bytes));
                RenderResourceMetrics.PendingReleaseAdded(bytes);
            }
        }
    }

    public void Trim(RenderCacheTrimReason reason)
    {
        lock (_gate)
        {
            if (_disposed) return;
            int limit = reason is RenderCacheTrimReason.MemoryPressure or RenderCacheTrimReason.DeviceLost or RenderCacheTrimReason.Manual
                ? int.MaxValue
                : NORMAL_MAINTENANCE_EVICTION_LIMIT;
            int evicted = 0;
            foreach (var entry in _entries.Values.OrderBy(static entry => entry.LastUseSequence).ToArray())
            {
                if (evicted >= limit || (reason == RenderCacheTrimReason.CapacityExceeded && IsPersistentWithinBudgetNoLock()))
                {
                    break;
                }
                if (entry.LeaseCount != 0
                    && reason is not RenderCacheTrimReason.DeviceLost and not RenderCacheTrimReason.Manual)
                {
                    continue;
                }
                _entries.Remove(entry.Key);
                RetireEntry_NoLock(entry);
                evicted++;
            }
            if (reason is RenderCacheTrimReason.MemoryPressure or RenderCacheTrimReason.DeviceLost or RenderCacheTrimReason.Manual)
            {
                ClearScratchPoolNoLock();
            }
            DrainCompletedReleases_NoLock();
        }
    }

    public void Maintain(RenderCacheMaintenanceMode mode = RenderCacheMaintenanceMode.Frame)
    {
        lock (_gate)
        {
            if (_disposed) return;
            DrainCompletedReleases_NoLock();
            int limit = mode is RenderCacheMaintenanceMode.MemoryPressure
                or RenderCacheMaintenanceMode.DeviceLost
                or RenderCacheMaintenanceMode.Shutdown
                ? int.MaxValue
                : NORMAL_MAINTENANCE_EVICTION_LIMIT;
            int remaining = limit;
            remaining -= SweepIdleScratchNoLock(remaining);
            if (mode is RenderCacheMaintenanceMode.DeviceLost or RenderCacheMaintenanceMode.Shutdown)
            {
                foreach (var entry in _entries.Values.ToArray())
                {
                    _entries.Remove(entry.Key);
                    RetireEntry_NoLock(entry);
                }
            }
            else
            {
                remaining -= EvictIdlePersistentNoLock(_tickProvider(), remaining);
                EvictPersistentToBudgetNoLock(remaining);
            }
            if (mode is RenderCacheMaintenanceMode.MemoryPressure or RenderCacheMaintenanceMode.DeviceLost or RenderCacheMaintenanceMode.Shutdown)
            {
                ClearScratchPoolNoLock();
            }
        }
    }

    internal bool TryReserveAtlasPage(long bytes)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            bytes = Math.Max(0, bytes);
            EvictPersistentForAdmissionNoLock(bytes);
            if (_persistentBytes + _scratchBytes + _atlasActiveBytes + bytes > _persistentBudgetBytes)
            {
                return false;
            }
            _atlasActiveBytes += bytes;
            return true;
        }
    }

    internal void ReleaseAtlasPageReservation(long bytes)
    {
        lock (_gate)
        {
            _atlasActiveBytes = Math.Max(0, _atlasActiveBytes - Math.Max(0, bytes));
        }
    }

    internal RenderResourceCacheStatistics GetStatistics()
    {
        lock (_gate)
        {
            int activePersistent = 0;
            foreach (var entry in _entries.Values)
            {
                if (entry.LeaseCount != 0)
                {
                    activePersistent++;
                }
            }
            return new RenderResourceCacheStatistics(
                _entries.Count,
                activePersistent,
                _persistentBytes,
                CountScratchNoLock(),
                _scratchBytes,
                _pendingReleases.Count,
                _atlasActiveBytes);
        }
    }

    internal RenderCacheKey[] SnapshotPersistentKeys()
    {
        lock (_gate)
        {
            return _entries.Keys.ToArray();
        }
    }

    public IRenderSurface? RentScratchSurface(ScratchSurfaceKey key)
        => RentScratchSurface(key, exactSizeOnly: false);

    public IRenderSurface? RentScratchSurface(ScratchSurfaceKey key, bool exactSizeOnly)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            SweepIdleScratchNoLock(NORMAL_MAINTENANCE_EVICTION_LIMIT);
            if (TryRentFromBucketNoLock(key, out var exact))
            {
                return exact;
            }
            if (exactSizeOnly)
            {
                return null;
            }

            long requestedArea = (long)key.PixelWidth * key.PixelHeight;
            long requestedBytes = RenderResourceMetrics.ScratchBytes(key.PixelWidth, key.PixelHeight);
            ScratchSurfaceKey selectedKey = default;
            int selectedIndex = -1;
            long newestUse = long.MinValue;
            int candidates = 0;
            foreach (var pair in _scratchBuckets)
            {
                var candidateKey = pair.Key;
                if (candidateKey.DpiScale != key.DpiScale
                    || candidateKey.HasAlpha != key.HasAlpha
                    || candidateKey.ResourceClass != key.ResourceClass
                    || candidateKey.PixelWidth < key.PixelWidth
                    || candidateKey.PixelHeight < key.PixelHeight)
                {
                    continue;
                }

                long area = (long)candidateKey.PixelWidth * candidateKey.PixelHeight;
                long bytes = RenderResourceMetrics.ScratchBytes(candidateKey.PixelWidth, candidateKey.PixelHeight);
                if ((area > requestedArea && area - requestedArea > requestedArea)
                    || bytes - requestedBytes > SCRATCH_MAX_EXTRA_BYTES)
                {
                    continue;
                }

                var bucket = pair.Value;
                for (int i = bucket.Count - 1; i >= 0 && candidates < SCRATCH_MAX_OVERSIZE_CANDIDATES; i--)
                {
                    var pooled = bucket[i];
                    if (pooled.Surface is IReusableScratchSurface reusable && !reusable.CanRenderFromCurrentThread)
                    {
                        continue;
                    }
                    candidates++;
                    if (pooled.Sequence > newestUse)
                    {
                        newestUse = pooled.Sequence;
                        selectedKey = candidateKey;
                        selectedIndex = i;
                    }
                }
                if (candidates >= SCRATCH_MAX_OVERSIZE_CANDIDATES)
                {
                    break;
                }
            }

            return selectedIndex >= 0
                ? RemoveScratchAtNoLock(selectedKey, selectedIndex)
                : null;
        }
    }

    private bool TryRentFromBucketNoLock(ScratchSurfaceKey key, out IRenderSurface surface)
    {
        if (_scratchBuckets.TryGetValue(key, out var bucket))
        {
            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                var pooled = bucket[i];
                if (pooled.Surface is IReusableScratchSurface reusable && !reusable.CanRenderFromCurrentThread)
                {
                    continue;
                }
                surface = RemoveScratchAtNoLock(key, i);
                return true;
            }
        }
        surface = null!;
        return false;
    }

    private IRenderSurface RemoveScratchAtNoLock(ScratchSurfaceKey key, int index)
    {
        var bucket = _scratchBuckets[key];
        var pooled = bucket[index];
        bucket.RemoveAt(index);
        _scratchBytes -= pooled.Bytes;
        RenderResourceMetrics.ScratchUnpooled(pooled.Bytes, disposed: false);
        if (bucket.Count == 0)
        {
            _scratchBuckets.Remove(key);
        }
        return pooled.Surface;
    }

    public void ReturnScratchSurface(ScratchSurfaceKey key, IRenderSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        lock (_gate)
        {
            if (_disposed)
            {
                surface.Dispose();
                RenderResourceMetrics.ScratchDisposedOutsidePool();
                return;
            }

            if (!_scratchBuckets.TryGetValue(key, out var bucket))
            {
                bucket = new List<PooledScratchSurface>();
                _scratchBuckets[key] = bucket;
            }

            long bytes = RenderResourceMetrics.ScratchBytes(key.PixelWidth, key.PixelHeight);
            bucket.Add(new PooledScratchSurface(
                surface,
                bytes,
                _tickProvider(),
                ++_scratchReturnSequence));
            _scratchBytes += bytes;
            RenderResourceMetrics.ScratchPooled(bytes);

            SweepIdleScratchNoLock(NORMAL_MAINTENANCE_EVICTION_LIMIT);
            EvictScratchToBudgetNoLock();
            EvictPersistentToBudgetNoLock(NORMAL_MAINTENANCE_EVICTION_LIMIT);
        }
    }

    /// <summary>Disposes pooled surfaces that have sat unused past the idle window, at most once per sweep interval.</summary>
    private int SweepIdleScratchNoLock(int limit)
    {
        long now = _tickProvider();
        if (limit <= 0 || now - _scratchLastSweepTicks < SCRATCH_SWEEP_INTERVAL_MS)
        {
            return 0;
        }
        _scratchLastSweepTicks = now;
        int evicted = 0;

        List<ScratchSurfaceKey>? emptied = null;
        foreach (var pair in _scratchBuckets)
        {
            var bucket = pair.Value;
            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                if (evicted >= limit)
                {
                    break;
                }
                if (now - bucket[i].ReturnedAt < SCRATCH_IDLE_EVICT_MS)
                {
                    continue;
                }
                _scratchBytes -= bucket[i].Bytes;
                RenderResourceMetrics.ScratchUnpooled(bucket[i].Bytes, disposed: true);
                bucket[i].Surface.Dispose();
                bucket.RemoveAt(i);
                evicted++;
            }
            if (bucket.Count == 0)
            {
                (emptied ??= new List<ScratchSurfaceKey>()).Add(pair.Key);
            }
        }

        if (emptied != null)
        {
            foreach (var key in emptied)
            {
                _scratchBuckets.Remove(key);
            }
        }
        return evicted;
    }

    /// <summary>Evicts least-recently-used pooled surfaces until within budget, keeping at least one.</summary>
    private void EvictScratchToBudgetNoLock()
    {
        while ((_scratchBytes > SCRATCH_BUDGET_BYTES || CountScratchNoLock() > SCRATCH_MAX_COUNT) && CountScratchNoLock() > 1)
        {
            ScratchSurfaceKey oldestKey = default;
            int oldestIndex = -1;
            long oldestTimestamp = long.MaxValue;
            foreach (var pair in _scratchBuckets)
            {
                var bucket = pair.Value;
                for (int i = 0; i < bucket.Count; i++)
                {
                    if (bucket[i].Sequence < oldestTimestamp)
                    {
                        oldestTimestamp = bucket[i].Sequence;
                        oldestKey = pair.Key;
                        oldestIndex = i;
                    }
                }
            }

            if (oldestIndex < 0)
            {
                return;
            }

            var victimBucket = _scratchBuckets[oldestKey];
            var victim = victimBucket[oldestIndex];
            victimBucket.RemoveAt(oldestIndex);
            _scratchBytes -= victim.Bytes;
            RenderResourceMetrics.ScratchUnpooled(victim.Bytes, disposed: true);
            if (victimBucket.Count == 0)
            {
                _scratchBuckets.Remove(oldestKey);
            }

            victim.Surface.Dispose();
        }
    }

    private int CountScratchNoLock()
    {
        int count = 0;
        foreach (var bucket in _scratchBuckets.Values)
        {
            count += bucket.Count;
        }
        return count;
    }

    private void ClearScratchPoolNoLock()
    {
        foreach (var bucket in _scratchBuckets.Values)
        {
            foreach (var pooled in bucket)
            {
                pooled.Surface.Dispose();
                RenderResourceMetrics.ScratchUnpooled(pooled.Bytes, disposed: true);
            }
        }
        _scratchBuckets.Clear();
        _scratchBytes = 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var entry in _entries.Values)
            {
                entry.Retire();
                if (entry.LeaseCount == 0)
                {
                    WaitAndDisposeEntry(entry);
                }
            }

            _entries.Clear();
            _persistentBytes = 0;
            ClearScratchPoolNoLock();

            foreach (var pending in _pendingReleases)
            {
                try
                {
                    pending.Operation.Wait();
                }
                finally
                {
                    RenderResourceMetrics.PendingReleaseRemoved(pending.Bytes);
                    pending.Resource.Dispose();
                    if (pending.DisposeOperationSeparately)
                    {
                        pending.Operation.Dispose();
                    }
                }
            }

            _pendingReleases.Clear();
        }
    }

    private void RetireEntry_NoLock(CachedRenderResource entry)
    {
        _persistentBytes -= entry.AccountedBytes;
        entry.Retire();
        if (entry.LeaseCount != 0)
        {
            return;
        }
        QueueOrDisposeEntryNoLock(entry);
    }

    private void ReturnLease(CachedRenderResource entry)
    {
        lock (_gate)
        {
            if (!entry.ReleaseLease())
            {
                return;
            }
            entry.IdleSince = _tickProvider();
            if (entry.IsRetired)
            {
                if (_disposed)
                {
                    WaitAndDisposeEntry(entry);
                }
                else
                {
                    QueueOrDisposeEntryNoLock(entry);
                }
            }
        }
    }

    private static void WaitAndDisposeEntry(CachedRenderResource entry)
    {
        try
        {
            entry.SafeToDisposeAfter?.Wait();
        }
        finally
        {
            entry.Dispose();
        }
    }

    private void QueueOrDisposeEntryNoLock(CachedRenderResource entry)
    {
        if (entry.SafeToDisposeAfter is { } operation && !operation.IsCompleted)
        {
            _pendingReleases.Add(new PendingRelease(entry, operation, entry.AccountedBytes, DisposeOperationSeparately: false));
            RenderResourceMetrics.PendingReleaseAdded(entry.AccountedBytes);
            return;
        }

        entry.Dispose();
    }

    private bool IsPersistentWithinBudgetNoLock()
        => _persistentBytes + _scratchBytes + _atlasActiveBytes <= _persistentBudgetBytes
            && _entries.Count <= _persistentMaxCount;

    private long BudgetedNativeBytesNoLock() => _persistentBytes + _scratchBytes + _atlasActiveBytes;

    private void EvictPersistentForAdmissionNoLock(long incomingBytes, int incomingCount = 0)
    {
        while (BudgetedNativeBytesNoLock() + incomingBytes > _persistentBudgetBytes
            || _entries.Count + incomingCount > _persistentMaxCount)
        {
            var victim = FindOldestReusablePersistentNoLock();
            if (victim == null)
            {
                return;
            }
            _entries.Remove(victim.Key);
            RetireEntry_NoLock(victim);
        }
    }

    private CachedRenderResource AddNoLock(
        RenderCacheKey key,
        IRenderSurface surface,
        IImage image,
        IRenderOperation? safeToDisposeAfter)
    {
        var resource = new CachedRenderResource(
            key,
            surface,
            image,
            safeToDisposeAfter,
            ++_persistentUseSequence);
        _entries.Add(key, resource);
        _persistentBytes += resource.AccountedBytes;
        return resource;
    }

    private int EvictIdlePersistentNoLock(long now, int limit)
    {
        int evicted = 0;
        while (evicted < limit)
        {
            CachedRenderResource? victim = null;
            foreach (var candidate in _entries.Values)
            {
                if (candidate.LeaseCount != 0
                    || now - candidate.IdleSince < PERSISTENT_IDLE_EVICT_MS)
                {
                    continue;
                }
                if (victim == null || candidate.LastUseSequence < victim.LastUseSequence)
                {
                    victim = candidate;
                }
            }
            if (victim == null)
            {
                return evicted;
            }
            _entries.Remove(victim.Key);
            RetireEntry_NoLock(victim);
            evicted++;
        }
        return evicted;
    }

    private CachedRenderResource? FindOldestReusablePersistentNoLock()
    {
        CachedRenderResource? victim = null;
        foreach (var candidate in _entries.Values)
        {
            if (candidate.LeaseCount != 0)
            {
                continue;
            }
            if (victim == null || candidate.LastUseSequence < victim.LastUseSequence)
            {
                victim = candidate;
            }
        }
        return victim;
    }

    private int EvictPersistentToBudgetNoLock(int limit, CachedRenderResource? protectedEntry = null)
    {
        if (IsPersistentWithinBudgetNoLock())
        {
            return 0;
        }

        int evicted = 0;
        while (evicted < limit
            && (_persistentBytes + _scratchBytes + _atlasActiveBytes > _persistentLowWatermarkBytes
                || _entries.Count > _persistentMaxCount))
        {
            var victim = FindOldestReusablePersistentNoLock();
            if (ReferenceEquals(victim, protectedEntry))
            {
                victim = _entries.Values
                    .Where(candidate => !ReferenceEquals(candidate, protectedEntry) && candidate.LeaseCount == 0)
                    .MinBy(static candidate => candidate.LastUseSequence);
            }
            if (victim == null)
            {
                break;
            }
            _entries.Remove(victim.Key);
            RetireEntry_NoLock(victim);
            evicted++;
        }
        return evicted;
    }

    private void DrainCompletedReleases_NoLock()
    {
        for (int i = _pendingReleases.Count - 1; i >= 0; i--)
        {
            var pending = _pendingReleases[i];
            if (!pending.Operation.IsCompleted)
            {
                continue;
            }

            _pendingReleases.RemoveAt(i);
            RenderResourceMetrics.PendingReleaseRemoved(pending.Bytes);
            pending.Resource.Dispose();
            if (pending.DisposeOperationSeparately)
            {
                pending.Operation.Dispose();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static long EstimateResourceBytes(IDisposable resource)
        => resource switch
        {
            CachedRenderResource entry => entry.AccountedBytes,
            IRenderTarget target => RenderResourceMetrics.ScratchBytes(target.PixelWidth, target.PixelHeight),
            IImage image => RenderResourceMetrics.ScratchBytes(image.PixelWidth, image.PixelHeight),
            _ => 0,
        };

    private sealed class CachedRenderResource : IDisposable
    {
        private bool _disposed;
        private int _leaseCount;

        public CachedRenderResource(
            RenderCacheKey key,
            IRenderSurface surface,
            IImage image,
            IRenderOperation? safeToDisposeAfter,
            long lastUseSequence)
        {
            Key = key;
            Surface = surface;
            Image = image;
            SafeToDisposeAfter = safeToDisposeAfter;
            LastUseSequence = lastUseSequence;
            AccountedBytes = RenderResourceMetrics.ScratchBytes(surface.PixelWidth, surface.PixelHeight);
            RenderResourceMetrics.PersistentResourceAdded(AccountedBytes);
        }

        public RenderCacheKey Key { get; }

        public IRenderSurface Surface { get; }

        public IImage Image { get; }

        public IRenderOperation? SafeToDisposeAfter { get; }

        public long AccountedBytes { get; }

        public int LeaseCount => _leaseCount;

        public bool IsRetired { get; private set; }

        public long LastUseSequence { get; set; }

        public long IdleSince { get; set; }

        public IRenderCacheEntry Acquire(RenderResourceCache owner)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _leaseCount++;
            IdleSince = 0;
            return new RenderCacheLease(owner, this);
        }

        public bool ReleaseLease()
        {
            if (_leaseCount == 0)
            {
                return false;
            }
            _leaseCount--;
            return _leaseCount == 0;
        }

        public void Retire() => IsRetired = true;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RenderResourceMetrics.PersistentResourceRemoved(AccountedBytes);
            Image.Dispose();
            Surface.Dispose();
            SafeToDisposeAfter?.Dispose();
        }
    }

    private sealed class RenderCacheLease : IRenderCacheEntry
    {
        private RenderResourceCache? _owner;
        private CachedRenderResource? _resource;

        public RenderCacheLease(RenderResourceCache owner, CachedRenderResource resource)
        {
            _owner = owner;
            _resource = resource;
        }

        public RenderCacheKey Key => GetResource().Key;
        public IRenderSurface Surface => GetResource().Surface;
        public IImage Image => GetResource().Image;
        public IRenderOperation? SafeToDisposeAfter => GetResource().SafeToDisposeAfter;

        public void Dispose()
        {
            var resource = Interlocked.Exchange(ref _resource, null);
            var owner = Interlocked.Exchange(ref _owner, null);
            if (resource != null && owner != null)
            {
                owner.ReturnLease(resource);
            }
        }

        private CachedRenderResource GetResource() => Volatile.Read(ref _resource)
            ?? throw new ObjectDisposedException(nameof(RenderCacheLease));
    }

    private readonly record struct PendingRelease(
        IDisposable Resource,
        IRenderOperation Operation,
        long Bytes,
        bool DisposeOperationSeparately = true);

    private readonly record struct PooledScratchSurface(
        IRenderSurface Surface,
        long Bytes,
        long ReturnedAt,
        long Sequence);
}

internal readonly record struct RenderResourceCacheStatistics(
    int PersistentCount,
    int ActivePersistentCount,
    long PersistentBytes,
    int PooledScratchCount,
    long PooledScratchBytes,
    int PendingReleaseCount,
    long AtlasActiveBytes);
