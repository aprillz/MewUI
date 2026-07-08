using System.Runtime.CompilerServices;

namespace Aprillz.MewUI;

/// <summary>
/// Source of a property value, ordered by priority (higher = wins).
/// </summary>
internal enum ValueSource : byte
{
    Default = 0,
    Inherited = 1,
    Style = 2,
    Trigger = 3,
    Local = 4,
}

/// <summary>
/// Wrapper allocated only when a property is being animated.
/// Preserves the base value so it can be restored when the animation completes.
/// </summary>
internal sealed class AnimatedEntry
{
    public required object BaseValue;
    public required object AnimatedValue;
    public ValueSource BaseSource;
}

/// <summary>
/// Per-instance storage for <see cref="MewProperty{T}"/> values.
/// Each entry stores a single value and its <see cref="ValueSource"/>.
/// Animation is handled via an <see cref="AnimatedEntry"/> wrapper (allocated only when animating).
/// </summary>
internal sealed class PropertyValueStore
{
    private const int SPARSE_CAPACITY = 8;
    private const int MAX_JUSTIFIED_DENSE_ID = 256;
    
    // Small ints and the two most common doubles (Opacity/scale endpoints) are cached to avoid
    // a fresh box on every SetLocal/SetTarget/SetInherited call in the hot style/trigger path.
    private const int CACHED_INT_MIN = -1;
    private const int CACHED_INT_MAX = 8;

    private static readonly object _boxedTrue = true;
    private static readonly object _boxedFalse = false;

    private static readonly object[] _boxedInts = CreateBoxedInts();
    private static readonly object _boxedDoubleZero = 0.0;
    private static readonly object _boxedDoubleOne = 1.0;

    private static object[] CreateBoxedInts()
    {
        var boxedInts = new object[CACHED_INT_MAX - CACHED_INT_MIN + 1];
        for (int value = CACHED_INT_MIN; value <= CACHED_INT_MAX; value++)
            boxedInts[value - CACHED_INT_MIN] = value;
        return boxedInts;
    }

    private readonly WeakReference<IPropertyOwner> _ownerRef;
    private readonly Type _ownerType;
    private Entry[]? _entries;

    // Property ids are global and sequential across the whole app (framework + user-registered),
    // so a dense Entry[] sized by the highest touched id can waste a lot of space when an element
    // only ever touches a couple of late-registered properties. Below SparseCapacity distinct
    // properties we use a small linear-scan array keyed by property id (below); once an element
    // genuinely touches many properties we promote to the dense array above (as long as the id
    // range is small enough to make that worthwhile), which keeps GetValue/SetValue O(1) for the
    // common, heavily-styled controls that this store exists to serve.
    private SparseEntry[]? _sparseEntries;
    private int _sparseCount;

    internal Type OwnerType => _ownerType;

    /// <summary>
    /// Callback invoked when a snap overrides a property that has an animated value.
    /// The animation system registers this to stop the running clock for that property.
    /// </summary>
    internal Action<int>? StopAnimationCallback;

    /// <summary>
    /// Callback invoked when <see cref="Clear"/> is called, so the animation system can stop all clocks.
    /// </summary>
    internal Action? StopAllAnimationsCallback;

    public PropertyValueStore(IPropertyOwner owner)
    {
        _ownerRef = new WeakReference<IPropertyOwner>(owner);
        _ownerType = owner.GetType();
    }

    /// <summary>
    /// Gets the current effective value of a property.
    /// Resolution: Local > Animated > Trigger > Style > Inherited > Default.
    /// </summary>
    public T GetValue<T>(MewProperty<T> property)
    {
        ref var entry = ref GetEntry(property.Id);

        if (entry.Source == ValueSource.Default)
            return property.GetDefaultForType(_ownerType);

        if (entry.Value is AnimatedEntry animated)
            return (T)animated.AnimatedValue;

        return (T)entry.Value!;
    }

    /// <summary>
    /// Gets the current value as <see cref="object"/>. Used by non-generic resolution paths.
    /// </summary>
    public object GetBoxedValue(MewProperty property)
    {
        ref var entry = ref GetEntry(property.Id);

        if (entry.Source == ValueSource.Default)
            return property.GetBoxedDefaultForType(_ownerType);

        if (entry.Value is AnimatedEntry animated)
            return animated.AnimatedValue;

        return entry.Value!;
    }

    /// <summary>
    /// Sets the local (user-defined) value. Highest priority in resolution.
    /// </summary>
    public void SetLocal(MewProperty<bool> property, bool value)
    {
        ref var entry = ref GetEntry(property.Id);
        if (entry.Source == ValueSource.Local && entry.Value is bool existing && existing == value)
            return;

        SetValue(property, Box(value), ValueSource.Local);
    }

    /// <summary>
    /// Sets the local (user-defined) value. Highest priority in resolution.
    /// </summary>
    public void SetLocal<T>(MewProperty<T> property, T value)
    {
        // Fast path: skip boxing when the value hasn't changed.
        ref var entry = ref GetEntry(property.Id);
        if (entry.Source == ValueSource.Local && entry.Value is T existing &&
            EqualityComparer<T>.Default.Equals(existing, value))
            return;

        SetValue(property, BoxCached(value), ValueSource.Local);
    }

    /// <summary>
    /// Sets a style base setter value.
    /// </summary>
    public void SetStyle(MewProperty property, object value)
    {
        SetValue(property, value, ValueSource.Style);
    }

    /// <summary>
    /// Sets a trigger setter value. Overrides style values.
    /// </summary>
    public void SetTrigger(MewProperty property, object value)
    {
        SetValue(property, value, ValueSource.Trigger);
    }

    /// <summary>
    /// Sets a property value with the given source.
    /// If the current source has higher priority, the call is ignored.
    /// Stops any running animation on this property.
    /// </summary>
    public void SetValue(MewProperty property, object? value, ValueSource source)
    {
        ref var entry = ref EnsureEntry(property.Id);

        // Don't overwrite a higher-priority source (e.g. Local beats Trigger)
        if (source < entry.Source && entry.Source != ValueSource.Default)
            return;

        // Apply coerce callback (skipped for null: coercion callbacks assume a non-null value)
        if (value != null && property.CoerceCallback != null && _ownerRef.TryGetTarget(out var coerceOwner))
        {
            value = property.CoerceCallback(coerceOwner, value);
        }

        // No change - skip to avoid infinite invalidation loops
        if (entry.Source == source && entry.Value is not AnimatedEntry && Equals(entry.Value, value))
            return;

        var oldValue = CaptureEffective(ref entry, property);

        // Stop any running animation
        if (entry.Value is AnimatedEntry)
        {
            StopAnimationCallback?.Invoke(property.Id);
        }

        entry.Value = value;
        entry.Source = source;
        NotifyChanged(property, oldValue, value);
    }

    /// <summary>
    /// Clears the value if it was set by the given source,
    /// allowing lower-priority values to take effect.
    /// Called when a trigger no longer matches.
    /// </summary>
    public void ClearSource(int propertyId, ValueSource source)
    {
        ref var entry = ref GetEntry(propertyId);
        if (entry.Source != source)
            return;

        var property = MewPropertyRegistry.GetProperty(propertyId);

        // Stop animation if running
        if (entry.Value is AnimatedEntry)
        {
            StopAnimationCallback?.Invoke(propertyId);
        }

        var oldValue = CaptureEffective(ref entry, property);
        entry.Value = null;
        entry.Source = ValueSource.Default;

        if (property != null)
        {
            var newValue = property.GetBoxedDefaultForType(_ownerType);
            NotifyChanged(property, oldValue, newValue);
        }
    }

    /// <summary>
    /// Backward-compatible SetTarget - maps to Trigger source.
    /// Used by existing code (PropertyForward, MewObjectPropertyBinding, etc.)
    /// </summary>
    public void SetTarget(MewProperty property, object? value)
    {
        SetValue(property, value, ValueSource.Trigger);
    }

    /// <summary>
    /// Typed SetTarget convenience.
    /// </summary>
    public void SetTarget<T>(MewProperty<T> property, T value)
    {
        SetTarget(property, BoxCached(value));
    }

    /// <summary>
    /// Caches an inherited property value. Does not fire change notifications
    /// because the effective value hasn't actually changed - it was already
    /// resolved via parent chain traversal; we're just caching the result.
    /// </summary>
    internal void SetInherited<T>(MewProperty<T> property, T value)
    {
        ref var entry = ref EnsureEntry(property.Id);
        // Don't overwrite a higher-priority source
        if (entry.Source > ValueSource.Inherited)
            return;
        entry.Value = BoxCached(value);
        entry.Source = ValueSource.Inherited;
    }

    /// <summary>
    /// Boxed counterpart of <see cref="SetInherited{T}"/> used by the non-generic inherited-change
    /// propagation path. Caches the resolved inherited value without firing notifications.
    /// </summary>
    internal void SetInheritedBoxed(MewProperty property, object? value)
    {
        ref var entry = ref EnsureEntry(property.Id);
        if (entry.Source > ValueSource.Inherited)
            return;
        entry.Value = value;
        entry.Source = ValueSource.Inherited;
    }

    /// <summary>
    /// Clears a cached inherited value, allowing the next GetValue call
    /// to re-resolve via the parent chain.
    /// </summary>
    internal void ClearInherited(int propertyId)
    {
        ref var entry = ref GetEntry(propertyId);
        if (entry.Source != ValueSource.Inherited)
            return;
        entry.Value = null;
        entry.Source = ValueSource.Default;
    }

    /// <summary>
    /// Clears all cached inherited values. Called when the element's parent changes
    /// so stale cached values from the old parent chain are discarded.
    /// </summary>
    internal void ClearAllInherited()
    {
        if (_entries != null)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Source == ValueSource.Inherited)
                {
                    _entries[i].Value = null;
                    _entries[i].Source = ValueSource.Default;
                }
            }
            return;
        }

        if (_sparseEntries == null) return;
        for (int i = 0; i < _sparseCount; i++)
        {
            if (_sparseEntries[i].Entry.Source == ValueSource.Inherited)
            {
                _sparseEntries[i].Entry.Value = null;
                _sparseEntries[i].Entry.Source = ValueSource.Default;
            }
        }
    }

    /// <summary>
    /// Returns true if this store has any non-default value for the property.
    /// Used by inheritance resolution to determine when to stop walking the parent chain.
    /// </summary>
    public bool HasOwnValue(int propertyId)
    {
        ref var entry = ref GetEntry(propertyId);
        return entry.Source != ValueSource.Default;
    }

    /// <summary>
    /// Gets the source of the current value for a property.
    /// </summary>
    internal ValueSource GetSource(int propertyId)
    {
        ref var entry = ref GetEntry(propertyId);
        return entry.Source;
    }

    /// <summary>
    /// Returns true if any value (style, trigger, or local) has been set.
    /// </summary>
    internal bool HasTargetValue(int propertyId)
    {
        ref var entry = ref GetEntry(propertyId);
        return entry.Source != ValueSource.Default;
    }

    /// <summary>
    /// Gets the current visual value (animated if running, otherwise the base value).
    /// Used by the animation system to capture the "from" value.
    /// </summary>
    internal object? GetCurrentVisualValue(int propertyId)
    {
        ref var entry = ref GetEntry(propertyId);
        if (entry.Value is AnimatedEntry animated)
            return animated.AnimatedValue;
        return entry.Value;
    }

    /// <summary>
    /// Sets the underlying target value without stopping animations or notifying.
    /// Used by <see cref="Animation.PropertyAnimator"/> when starting a new animation.
    /// </summary>
    internal void SetTargetDirect(MewProperty property, object value, ValueSource? source = null)
    {
        ref var entry = ref EnsureEntry(property.Id);
        if (entry.Value is AnimatedEntry animated)
        {
            animated.BaseValue = value;
            if (source.HasValue)
            {
                animated.BaseSource = source.Value;
                // Keep entry.Source aligned with the base source so priority checks
                // during the animation see the post-animation source. Without this,
                // a Style-source restoration animation leaves entry.Source at the
                // prior Trigger value, and a later SetStyle is rejected as lower
                // priority - pinning the stale animated target.
                entry.Source = source.Value;
            }
        }
        else
        {
            entry.Value = value;
            if (source.HasValue)
                entry.Source = source.Value;
        }
    }

    /// <summary>
    /// Sets the animated (interpolated) value for a property.
    /// Wraps the current value in an <see cref="AnimatedEntry"/> if not already wrapped.
    /// Called by the animation system on each frame tick.
    /// </summary>
    internal void SetAnimatedValue(int propertyId, object value)
    {
        ref var entry = ref EnsureEntry(propertyId);
        var property = MewPropertyRegistry.GetProperty(propertyId);
        var oldValue = property != null ? CaptureEffective(ref entry, property) : null;

        if (entry.Value is AnimatedEntry animated)
        {
            animated.AnimatedValue = value;
        }
        else
        {
            entry.Value = new AnimatedEntry
            {
                BaseValue = entry.Value!,
                AnimatedValue = value,
                BaseSource = entry.Source,
            };
        }

        if (property != null)
            NotifyChanged(property, oldValue, value);
    }

    /// <summary>
    /// Clears the animated value, restoring the base value.
    /// Called by the animation system when an animation completes.
    /// </summary>
    internal void ClearAnimatedValue(int propertyId)
    {
        ref var entry = ref GetEntry(propertyId);

        if (entry.Value is not AnimatedEntry animated)
            return;

        var property = MewPropertyRegistry.GetProperty(propertyId);
        var oldValue = property != null ? (object?)animated.AnimatedValue : null;

        entry.Value = animated.BaseValue;
        entry.Source = animated.BaseSource;

        if (property != null)
            NotifyChanged(property, oldValue, entry.Value);
    }

    /// <summary>
    /// Clears the local value for a property, allowing style/trigger/inherited to take effect.
    /// </summary>
    public void ClearLocal(MewProperty property)
    {
        ClearSource(property.Id, ValueSource.Local);
    }

    /// <summary>
    /// Clears all stored values, stops animations, and releases references.
    /// </summary>
    public void Clear()
    {
        StopAllAnimationsCallback?.Invoke();

        if (_entries != null)
            Array.Clear(_entries);

        if (_sparseEntries != null)
        {
            Array.Clear(_sparseEntries, 0, _sparseCount);
            _sparseCount = 0;
        }
    }

    private void NotifyChanged(MewProperty property, object? oldValue, object? newValue)
    {
        if (_ownerRef.TryGetTarget(out var owner))
            owner.OnPropertyChanged(property, oldValue, newValue);
    }

    private static object Box(bool value) => value ? _boxedTrue : _boxedFalse;

    /// <summary>
    /// Boxes a value, reusing a cached box for small ints and 0.0/1.0 doubles instead of
    /// allocating. <c>typeof(T)</c> comparisons are resolved per generic instantiation by the
    /// JIT, so the branch not matching T costs nothing; <see cref="Unsafe.As{TFrom, TTo}"/> reads
    /// the value without boxing it first just to inspect it.
    /// </summary>
    private static object BoxCached<T>(T value)
    {
        if (typeof(T) == typeof(int))
        {
            int intValue = Unsafe.As<T, int>(ref value);
            if (intValue >= CACHED_INT_MIN && intValue <= CACHED_INT_MAX)
                return _boxedInts[intValue - CACHED_INT_MIN];
        }
        else if (typeof(T) == typeof(double))
        {
            double doubleValue = Unsafe.As<T, double>(ref value);
            if (doubleValue == 0.0)
                return _boxedDoubleZero;
            if (doubleValue == 1.0)
                return _boxedDoubleOne;
        }

        return value!;
    }

    /// <summary>
    /// Captures the current effective value before a mutation.
    /// </summary>
    private object? CaptureEffective(ref Entry entry, MewProperty? property)
    {
        if (property?.ChangedWithValuesCallback == null)
            return null;

        if (entry.Value is AnimatedEntry animated)
            return animated.AnimatedValue;

        if (entry.Source != ValueSource.Default)
            return entry.Value;

        return property.GetBoxedDefaultForType(_ownerType);
    }

    private ref Entry GetEntry(int id)
    {
        if (_entries != null)
        {
            if (id < _entries.Length)
                return ref _entries[id];
            return ref Entry.Empty;
        }

        if (_sparseEntries != null)
        {
            for (int i = 0; i < _sparseCount; i++)
            {
                if (_sparseEntries[i].PropertyId == id)
                    return ref _sparseEntries[i].Entry;
            }
        }

        return ref Entry.Empty;
    }

    private ref Entry EnsureEntry(int id)
    {
        if (_entries != null)
        {
            if (id >= _entries.Length)
                Array.Resize(ref _entries, Math.Max(id + 1, _entries.Length * 2));

            return ref _entries[id];
        }

        _sparseEntries ??= new SparseEntry[SPARSE_CAPACITY];

        for (int i = 0; i < _sparseCount; i++)
        {
            if (_sparseEntries[i].PropertyId == id)
                return ref _sparseEntries[i].Entry;
        }

        if (_sparseCount < _sparseEntries.Length)
        {
            _sparseEntries[_sparseCount].PropertyId = id;
            return ref _sparseEntries[_sparseCount++].Entry;
        }

        int maxTouchedId = id;
        for (int i = 0; i < _sparseCount; i++)
            maxTouchedId = Math.Max(maxTouchedId, _sparseEntries[i].PropertyId);

        if (maxTouchedId < MAX_JUSTIFIED_DENSE_ID)
        {
            // Promote: the id range is small enough that a dense array pays for itself.
            var dense = new Entry[Math.Max(maxTouchedId + 1, SPARSE_CAPACITY)];
            for (int i = 0; i < _sparseCount; i++)
                dense[_sparseEntries[i].PropertyId] = _sparseEntries[i].Entry;

            _entries = dense;
            _sparseEntries = null;
            _sparseCount = 0;

            if (id >= _entries.Length)
                Array.Resize(ref _entries, Math.Max(id + 1, _entries.Length * 2));

            return ref _entries[id];
        }

        // Id range too spread out to justify a dense array of that size; keep growing sparse
        // instead so a single high-numbered property never forces a giant allocation.
        Array.Resize(ref _sparseEntries, _sparseEntries.Length * 2);
        _sparseEntries[_sparseCount].PropertyId = id;
        return ref _sparseEntries[_sparseCount++].Entry;
    }

    private struct Entry
    {
        public static Entry Empty;

        public object? Value;       // plain value, or AnimatedEntry when animating
        public ValueSource Source;  // who set this value
    }

    private struct SparseEntry
    {
        public int PropertyId;
        public Entry Entry;
    }
}

/// <summary>
/// Stores MewProperty references indexed by property Id for fast lookup.
/// </summary>
internal static class MewPropertyRegistry
{
    private static MewProperty?[] _properties = new MewProperty?[16];
    private static readonly object _lock = new();

    /// <summary>
    /// Registers a property reference. Called during <see cref="MewProperty{T}"/> construction.
    /// </summary>
    internal static void Register(MewProperty property)
    {
        lock (_lock)
        {
            int id = property.Id;
            if (id >= _properties.Length)
            {
                int newSize = Math.Max(id + 1, _properties.Length * 2);
                Array.Resize(ref _properties, newSize);
            }
            _properties[id] = property;
        }
    }

    internal static MewProperty? GetProperty(int id)
    {
        return id < _properties.Length ? _properties[id] : null;
    }
}
