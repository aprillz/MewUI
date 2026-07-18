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
/// Every source (Local, Trigger, Style, Inherited) is preserved simultaneously, so clearing a
/// higher-priority source reveals the preserved lower one without the caller re-deriving it.
/// The common single-source case stays inline: <c>Value</c>/<c>Source</c> hold the effective base
/// value and a <see cref="SlotSet"/> is allocated only when two or more sources coexist.
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
    private SparseEntry[]? _sparseEntries;
    private int _sparseCount;

    internal Type OwnerType => _ownerType;

    internal Action<int>? StopAnimationCallback;
    internal Action? StopAllAnimationsCallback;

    public PropertyValueStore(IPropertyOwner owner)
    {
        _ownerRef = new WeakReference<IPropertyOwner>(owner);
        _ownerType = owner.GetType();
    }

    /// <summary>
    /// Gets the current effective value of a property.
    /// Resolution: Animated over the highest set source (Local > Trigger > Style > Inherited > Default).
    /// </summary>
    public T GetValue<T>(MewProperty<T> property)
    {
        var entry = GetEntry(property.Id);

        if (entry.Value is AnimatedEntry animated)
            return (T)animated.AnimatedValue;

        if (entry.Source == ValueSource.Default)
            return property.GetDefaultForType(_ownerType);

        return (T)entry.Value!;
    }

    /// <summary>
    /// Gets the current value as <see cref="object"/>. Used by non-generic resolution paths.
    /// </summary>
    public object GetBoxedValue(MewProperty property)
    {
        var entry = GetEntry(property.Id);

        if (entry.Value is AnimatedEntry animated)
            return animated.AnimatedValue;

        if (entry.Source == ValueSource.Default)
            return property.GetBoxedDefaultForType(_ownerType);

        return entry.Value!;
    }

    /// <summary>
    /// Sets the local (user-defined) value. Highest priority in resolution.
    /// </summary>
    public void SetLocal(MewProperty<bool> property, bool value)
    {
        // Effective Local already equals value: a no-op regardless of any shadowed lower slots
        // (they are unaffected). entry.Value is not a bool while animating, so that path is not
        // short-circuited here.
        var entry = GetEntry(property.Id);
        if (entry.Source == ValueSource.Local && entry.Value is bool existing && existing == value)
            return;

        SetValue(property, Box(value), ValueSource.Local);
    }

    /// <summary>
    /// Sets the local (user-defined) value. Highest priority in resolution.
    /// </summary>
    public void SetLocal<T>(MewProperty<T> property, T value)
    {
        // Fast path: skip boxing when the effective Local value already equals value. Shadowed
        // lower slots are unaffected, so this holds whether or not a shadow exists.
        var entry = GetEntry(property.Id);
        if (entry.Source == ValueSource.Local &&
            entry.Value is T existing &&
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
    /// Sets a property value with the given source. The value is stored in that source's slot;
    /// lower-priority slots are preserved so a later clear reveals them.
    /// Stops any running animation on this property.
    /// </summary>
    public void SetValue(MewProperty property, object? value, ValueSource source)
    {
        ref var entry = ref EnsureEntry(property.Id);

        // Apply coerce callback (skipped for null: coercion callbacks assume a non-null value)
        if (value != null && property.CoerceCallback != null && _ownerRef.TryGetTarget(out var coerceOwner))
        {
            value = property.CoerceCallback(coerceOwner, value);
        }

        object? oldEffective = CaptureEffective(ref entry, property);

        // No change - skip to avoid infinite invalidation loops (only when the effective source and
        // value already match and nothing is animating).
        if (entry.Value is not AnimatedEntry && entry.Shadow == null &&
            entry.Source == source && Equals(entry.Value, value))
            return;

        // Pre-commit veto: rejecting before the entry is written keeps the store and the
        // side effects of changed callbacks consistent. Unlike coerce this also runs for null.
        if (property.ValidateCallback != null && _ownerRef.TryGetTarget(out var validateOwner))
        {
            property.ValidateCallback(validateOwner, value);
        }

        // A direct set stops any running animation and drops the animation overlay.
        if (entry.Value is AnimatedEntry)
        {
            StopAnimationCallback?.Invoke(property.Id);
            UnwrapAnimation(ref entry);
        }

        SetSlotValue(ref entry, source, value);

        object? newEffective = ComputeEffective(ref entry, property);
        if (!Equals(oldEffective, newEffective))
        {
            NotifyChanged(property, oldEffective, newEffective);
        }
    }

    /// <summary>
    /// Clears the value in the given source's slot, letting the next-highest preserved slot
    /// (or the default) take effect. Called when a trigger no longer matches.
    /// </summary>
    public void ClearSource(int propertyId, ValueSource source)
    {
        var snapshot = GetEntry(propertyId);
        if (!HasSlot(snapshot, source))
            return;

        ref var entry = ref EnsureEntry(propertyId);
        var property = MewPropertyRegistry.GetProperty(propertyId);

        // Clearing a source also stops an animation whose base is being altered, matching the
        // legacy behavior where clearing dropped the effective value.
        if (entry.Value is AnimatedEntry)
        {
            StopAnimationCallback?.Invoke(propertyId);
            UnwrapAnimation(ref entry);
        }

        object? oldEffective = property != null ? CaptureEffective(ref entry, property) : null;

        ClearSlotValue(ref entry, source);

        if (property != null)
        {
            object? newEffective = ComputeEffective(ref entry, property);
            if (!Equals(oldEffective, newEffective))
            {
                NotifyChanged(property, oldEffective, newEffective);
            }
        }
    }

    /// <summary>
    /// Backward-compatible SetTarget - maps to Trigger source.
    /// </summary>
    public void SetTarget(MewProperty property, object? value)
    {
        SetValue(property, value, ValueSource.Trigger);
    }

    /// <summary>
    /// Sets a pre-boxed value at the local tier. Used by the property-forward path so
    /// bindings sit above styles/triggers, consistent with the typed <see cref="SetLocal{T}"/>.
    /// </summary>
    public void SetLocal(MewProperty property, object? value)
    {
        SetValue(property, value, ValueSource.Local);
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
        // Inherited is a re-resolvable cache, not a preserved slot: skip it (and any shadow
        // allocation) when a higher source already wins, since clearing that source re-resolves
        // inheritance from the parent chain anyway.
        if (BaseSource(entry) > ValueSource.Inherited)
            return;
        SetSlotValue(ref entry, ValueSource.Inherited, BoxCached(value));
    }

    /// <summary>
    /// Boxed counterpart of <see cref="SetInherited{T}"/> used by the non-generic inherited-change
    /// propagation path. Caches the resolved inherited value without firing notifications.
    /// </summary>
    internal void SetInheritedBoxed(MewProperty property, object? value)
    {
        ref var entry = ref EnsureEntry(property.Id);
        if (BaseSource(entry) > ValueSource.Inherited)
            return;
        SetSlotValue(ref entry, ValueSource.Inherited, value);
    }

    /// <summary>
    /// Clears a cached inherited value, allowing the next GetValue call
    /// to re-resolve via the parent chain.
    /// </summary>
    internal void ClearInherited(int propertyId)
    {
        var snapshot = GetEntry(propertyId);
        if (!HasSlot(snapshot, ValueSource.Inherited))
            return;

        ref var entry = ref EnsureEntry(propertyId);
        ClearSlotValue(ref entry, ValueSource.Inherited);
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
                if (HasSlot(_entries[i], ValueSource.Inherited))
                    ClearSlotValue(ref _entries[i], ValueSource.Inherited);
            }
            return;
        }

        if (_sparseEntries == null) return;
        for (int i = 0; i < _sparseCount; i++)
        {
            if (HasSlot(_sparseEntries[i].Entry, ValueSource.Inherited))
                ClearSlotValue(ref _sparseEntries[i].Entry, ValueSource.Inherited);
        }
    }

    /// <summary>
    /// Appends the ids of properties currently holding a cached inherited value.
    /// Used by reparent propagation to diff them against the new context chain.
    /// </summary>
    internal void GetInheritedPropertyIds(List<int> result)
    {
        if (_entries != null)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (HasSlot(_entries[i], ValueSource.Inherited))
                    result.Add(i);
            }
            return;
        }

        if (_sparseEntries == null) return;
        for (int i = 0; i < _sparseCount; i++)
        {
            if (HasSlot(_sparseEntries[i].Entry, ValueSource.Inherited))
                result.Add(_sparseEntries[i].PropertyId);
        }
    }

    /// <summary>
    /// Returns true if this store has any non-default value for the property.
    /// </summary>
    public bool HasOwnValue(int propertyId)
    {
        var entry = GetEntry(propertyId);
        return entry.Value is AnimatedEntry || entry.Source != ValueSource.Default;
    }

    /// <summary>
    /// Gets the source of the current effective (base) value for a property.
    /// </summary>
    internal ValueSource GetSource(int propertyId)
    {
        return GetEntry(propertyId).Source;
    }

    /// <summary>
    /// Returns true if any value (style, trigger, or local) has been set.
    /// </summary>
    internal bool HasTargetValue(int propertyId)
    {
        var entry = GetEntry(propertyId);
        return entry.Value is AnimatedEntry || entry.Source != ValueSource.Default;
    }

    /// <summary>
    /// Gets the current visual value (animated if running, otherwise the base value).
    /// Used by the animation system to capture the "from" value.
    /// </summary>
    internal object? GetCurrentVisualValue(int propertyId)
    {
        var entry = GetEntry(propertyId);
        if (entry.Value is AnimatedEntry animated)
            return animated.AnimatedValue;
        return entry.Value;
    }

    /// <summary>
    /// Sets the underlying base value without stopping animations or notifying.
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
                // Update the base slot while the animation overlay is temporarily lifted, then
                // restore the overlay over the recomputed base.
                UnwrapAnimation(ref entry);
                SetSlotValue(ref entry, source.Value, value);
                RewrapAnimation(ref entry, animated);
            }
        }
        else
        {
            SetSlotValue(ref entry, source ?? entry.Source, value);
        }
    }

    /// <summary>
    /// Sets the animated (interpolated) value for a property.
    /// Wraps the current base value in an <see cref="AnimatedEntry"/> if not already wrapped.
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
    /// </summary>
    internal void ClearAnimatedValue(int propertyId)
    {
        var snapshot = GetEntry(propertyId);

        if (snapshot.Value is not AnimatedEntry animated)
            return;

        ref var entry = ref EnsureEntry(propertyId);

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

    /// <summary>
    /// Computes the effective value after a mutation (for change notification comparison).
    /// </summary>
    private object? ComputeEffective(ref Entry entry, MewProperty property)
    {
        if (entry.Value is AnimatedEntry animated)
            return animated.AnimatedValue;

        if (entry.Source == ValueSource.Default)
            return property.GetBoxedDefaultForType(_ownerType);

        return entry.Value;
    }

    // ----- multi-slot base storage -----
    //
    // A slot being "set" is tracked independently of its value, so an explicit null (e.g. a local
    // null override that must hide a style value) is a real slot value, not an absent one. In the
    // inline form Source != Default marks the single set slot; the shadow set carries its own mask.

    // True if the given source currently has a value slot set.
    private static bool HasSlot(in Entry entry, ValueSource source)
    {
        if (entry.Shadow != null)
            return entry.Shadow.IsSet(source);

        return BaseSource(entry) == source;
    }

    // The effective base source, seeing through an animation wrapper.
    private static ValueSource BaseSource(in Entry entry)
        => entry.Value is AnimatedEntry animated ? animated.BaseSource : entry.Source;

    // Sets a source slot to a value (null is a legitimate value) and keeps Value/Source (the
    // effective base cache) in sync. Never touches an animation overlay: callers unwrap first when a
    // direct set must drop the animation.
    private static void SetSlotValue(ref Entry entry, ValueSource source, object? value)
    {
        if (entry.Shadow == null)
        {
            if (entry.Source == ValueSource.Default || entry.Source == source)
            {
                entry.Value = value;
                entry.Source = source;
                return;
            }

            // The single set slot is a re-resolvable Inherited cache and a higher source arrives:
            // replace it inline instead of allocating a shadow to preserve it.
            if (entry.Source == ValueSource.Inherited && source > ValueSource.Inherited)
            {
                entry.Value = value;
                entry.Source = source;
                return;
            }

            // A second, distinct slot appears: promote to a shadow set holding both.
            var shadow = new SlotSet();
            shadow.Set(entry.Source, entry.Value);
            shadow.Set(source, value);
            entry.Shadow = shadow;
            RecomputeFromShadow(ref entry);
            return;
        }

        entry.Shadow.Set(source, value);
        RecomputeFromShadow(ref entry);
    }

    // Unsets a source slot, revealing the next-highest set slot (or the default).
    private static void ClearSlotValue(ref Entry entry, ValueSource source)
    {
        if (entry.Shadow == null)
        {
            if (entry.Source == source)
            {
                entry.Value = null;
                entry.Source = ValueSource.Default;
            }
            return;
        }

        entry.Shadow.Unset(source);
        RecomputeFromShadow(ref entry);
    }

    // Recomputes Value/Source from the shadow set and folds back to the inline form when at most one
    // slot remains set.
    private static void RecomputeFromShadow(ref Entry entry)
    {
        var shadow = entry.Shadow!;
        int setCount = 0;
        ValueSource highest = ValueSource.Default;
        object? highestValue = null;

        for (var source = ValueSource.Local; source > ValueSource.Default; source--)
        {
            if (shadow.IsSet(source))
            {
                setCount++;
                if (highest == ValueSource.Default)
                {
                    highest = source;
                    highestValue = shadow.Get(source);
                }
            }
        }

        if (setCount <= 1)
        {
            entry.Shadow = null;
            entry.Value = highestValue;
            entry.Source = highest;
            return;
        }

        entry.Value = highestValue;
        entry.Source = highest;
    }

    private void UnwrapAnimation(ref Entry entry)
    {
        if (entry.Value is AnimatedEntry animated)
        {
            entry.Value = animated.BaseValue;
            entry.Source = animated.BaseSource;
        }
    }

    private void RewrapAnimation(ref Entry entry, AnimatedEntry animated)
    {
        // After WriteSlot updated the base cache in Value/Source, restore the animation overlay.
        animated.BaseValue = entry.Value!;
        animated.BaseSource = entry.Source;
        entry.Value = animated;
    }

    private Entry GetEntry(int id)
    {
        if (_entries != null)
        {
            if (id < _entries.Length)
                return _entries[id];
            return default;
        }

        if (_sparseEntries != null)
        {
            for (int i = 0; i < _sparseCount; i++)
            {
                if (_sparseEntries[i].PropertyId == id)
                    return _sparseEntries[i].Entry;
            }
        }

        return default;
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

        Array.Resize(ref _sparseEntries, _sparseEntries.Length * 2);
        _sparseEntries[_sparseCount].PropertyId = id;
        return ref _sparseEntries[_sparseCount++].Entry;
    }

    private struct Entry
    {
        public object? Value;       // effective base value, or AnimatedEntry when animating
        public ValueSource Source;  // effective base source
        public SlotSet? Shadow;     // non-null only when two or more base sources are set at once
    }

    private struct SparseEntry
    {
        public int PropertyId;
        public Entry Entry;
    }

    // Holds every set base slot when two or more coexist. A per-slot mask tracks set-ness
    // independently of the value, so an explicit null is a real slot value rather than "unset".
    // Default is never stored (it is the absence of any slot). Animation is not a slot here - it
    // wraps the effective base value.
    private sealed class SlotSet
    {
        private object? _inherited;
        private object? _style;
        private object? _trigger;
        private object? _local;
        private int _setMask;

        private static int Bit(ValueSource source) => 1 << (int)source;

        public bool IsSet(ValueSource source) => (_setMask & Bit(source)) != 0;

        public object? Get(ValueSource source) => source switch
        {
            ValueSource.Inherited => _inherited,
            ValueSource.Style => _style,
            ValueSource.Trigger => _trigger,
            ValueSource.Local => _local,
            _ => null,
        };

        public void Set(ValueSource source, object? value)
        {
            switch (source)
            {
                case ValueSource.Inherited: _inherited = value; break;
                case ValueSource.Style: _style = value; break;
                case ValueSource.Trigger: _trigger = value; break;
                case ValueSource.Local: _local = value; break;
                default: return;
            }
            _setMask |= Bit(source);
        }

        public void Unset(ValueSource source)
        {
            switch (source)
            {
                case ValueSource.Inherited: _inherited = null; break;
                case ValueSource.Style: _style = null; break;
                case ValueSource.Trigger: _trigger = null; break;
                case ValueSource.Local: _local = null; break;
                default: return;
            }
            _setMask &= ~Bit(source);
        }
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
