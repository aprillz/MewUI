namespace Aprillz.MewUI.Resources;

internal interface IDecodedPixelCacheOwner
{
    bool TryEvictDecodedPixels(DecodedPixelOwner owner);
}

/// <summary>
/// Process-wide budget for rehydratable decoded variants. Entries only hold weak references to
/// their source and pixel owner, so diagnostics and eviction never extend application lifetimes.
/// </summary>
internal sealed class DecodedPixelCache
{
    internal const long DefaultBudgetBytes = 128L * 1024 * 1024;

    public static DecodedPixelCache Shared { get; } = new(DefaultBudgetBytes);

    private readonly object _gate = new();
    private readonly long _budgetBytes;
    private readonly bool _scheduleMaintenance;
    private readonly List<Entry> _entries = [];
    private long _residentBytes;
    private long _sequence;
    private int _maintenanceScheduled;

    internal DecodedPixelCache(long budgetBytes, bool scheduleMaintenance = true)
    {
        _budgetBytes = Math.Max(1, budgetBytes);
        _scheduleMaintenance = scheduleMaintenance;
    }

    public void Register(IDecodedPixelCacheOwner source, DecodedPixelOwner owner)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            RemoveMatchingNoLock(source, owner);
            _entries.Add(new Entry(source, owner, owner.AccountedBytes, ++_sequence));
            _residentBytes += owner.AccountedBytes;
            CleanupDeadNoLock();
        }

        if (_scheduleMaintenance)
        {
            ScheduleMaintenance();
        }
    }

    public void Touch(DecodedPixelOwner owner)
    {
        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                if (entry.Owner.TryGetTarget(out var current) && ReferenceEquals(current, owner))
                {
                    entry.LastUseSequence = ++_sequence;
                    return;
                }
            }
        }
    }

    public void Unregister(DecodedPixelOwner? owner)
    {
        if (owner == null)
        {
            return;
        }

        lock (_gate)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Owner.TryGetTarget(out var current) && ReferenceEquals(current, owner))
                {
                    RemoveAtNoLock(i);
                }
            }
        }
    }

    public void Maintain()
    {
        while (true)
        {
            Entry[] candidates;
            lock (_gate)
            {
                CleanupDeadNoLock();
                if (_residentBytes <= _budgetBytes || _entries.Count <= 1)
                {
                    return;
                }
                candidates = _entries.OrderBy(static entry => entry.LastUseSequence).ToArray();
            }

            bool evicted = false;
            foreach (var candidate in candidates)
            {
                lock (_gate)
                {
                    if (_residentBytes <= _budgetBytes)
                    {
                        return;
                    }
                    int index = _entries.IndexOf(candidate);
                    if (index < 0)
                    {
                        continue;
                    }
                    RemoveAtNoLock(index);
                }

                if (!candidate.Source.TryGetTarget(out var source)
                    || !candidate.Owner.TryGetTarget(out var owner)
                    || source.TryEvictDecodedPixels(owner))
                {
                    evicted = true;
                    continue;
                }

                lock (_gate)
                {
                    candidate.LastUseSequence = ++_sequence;
                    _entries.Add(candidate);
                    _residentBytes += candidate.Bytes;
                }
            }

            if (!evicted)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Attempts to release every rehydratable decoded variant without disturbing variants that
    /// are pinned by a native realization. Pinned entries remain registered and can be retried at
    /// the next safe trim boundary.
    /// </summary>
    public void Trim()
    {
        Entry[] candidates;
        lock (_gate)
        {
            CleanupDeadNoLock();
            candidates = _entries.OrderBy(static entry => entry.LastUseSequence).ToArray();
        }

        foreach (var candidate in candidates)
        {
            lock (_gate)
            {
                int index = _entries.IndexOf(candidate);
                if (index < 0)
                {
                    continue;
                }
                RemoveAtNoLock(index);
            }

            if (!candidate.Source.TryGetTarget(out var source)
                || !candidate.Owner.TryGetTarget(out var owner)
                || source.TryEvictDecodedPixels(owner))
            {
                continue;
            }

            lock (_gate)
            {
                candidate.LastUseSequence = ++_sequence;
                _entries.Add(candidate);
                _residentBytes += candidate.Bytes;
            }
        }
    }

    internal (int Count, long Bytes) GetStatistics()
    {
        lock (_gate)
        {
            CleanupDeadNoLock();
            return (_entries.Count, _residentBytes);
        }
    }

    private void ScheduleMaintenance()
    {
        if (Interlocked.Exchange(ref _maintenanceScheduled, 1) != 0)
        {
            return;
        }

        ThreadPool.UnsafeQueueUserWorkItem(static cache =>
        {
            try
            {
                cache.Maintain();
            }
            finally
            {
                Volatile.Write(ref cache._maintenanceScheduled, 0);
            }
        }, this, preferLocal: false);
    }

    private void RemoveMatchingNoLock(IDecodedPixelCacheOwner source, DecodedPixelOwner owner)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            bool sameSource = _entries[i].Source.TryGetTarget(out var currentSource)
                && ReferenceEquals(currentSource, source);
            bool sameOwner = _entries[i].Owner.TryGetTarget(out var currentOwner)
                && ReferenceEquals(currentOwner, owner);
            if (sameSource || sameOwner)
            {
                RemoveAtNoLock(i);
            }
        }
    }

    private void CleanupDeadNoLock()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (!_entries[i].Source.TryGetTarget(out _) || !_entries[i].Owner.TryGetTarget(out _))
            {
                RemoveAtNoLock(i);
            }
        }
    }

    private void RemoveAtNoLock(int index)
    {
        _residentBytes -= _entries[index].Bytes;
        _entries.RemoveAt(index);
    }

    private sealed class Entry(
        IDecodedPixelCacheOwner source,
        DecodedPixelOwner owner,
        long bytes,
        long lastUseSequence)
    {
        public WeakReference<IDecodedPixelCacheOwner> Source { get; } = new(source);
        public WeakReference<DecodedPixelOwner> Owner { get; } = new(owner);
        public long Bytes { get; } = bytes;
        public long LastUseSequence { get; set; } = lastUseSequence;
    }
}
