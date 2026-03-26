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
    private readonly WeakReference<IPropertyOwner> _ownerRef;
    private readonly Type _ownerType;
    private Entry[]? _entries;

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
    public void SetLocal<T>(MewProperty<T> property, T value)
    {
        SetValue(property, value!, ValueSource.Local);
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
    public void SetValue(MewProperty property, object value, ValueSource source)
    {
        ref var entry = ref EnsureEntry(property.Id);

        // Don't overwrite a higher-priority source (e.g. Local beats Trigger)
        if (source < entry.Source && entry.Source != ValueSource.Default)
            return;

        // No change — skip to avoid infinite invalidation loops
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
    /// Backward-compatible SetTarget — maps to Trigger source.
    /// Used by existing code (PropertyForward, MewObjectPropertyBinding, etc.)
    /// </summary>
    public void SetTarget(MewProperty property, object value)
    {
        SetValue(property, value, ValueSource.Trigger);
    }

    /// <summary>
    /// Typed SetTarget convenience.
    /// </summary>
    public void SetTarget<T>(MewProperty<T> property, T value)
    {
        SetTarget(property, (object)value!);
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
                animated.BaseSource = source.Value;
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
    }

    private void NotifyChanged(MewProperty property, object? oldValue, object? newValue)
    {
        if (_ownerRef.TryGetTarget(out var owner))
            owner.OnPropertyChanged(property, oldValue, newValue);
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
        if (_entries == null || id >= _entries.Length)
            return ref Entry.Empty;

        return ref _entries[id];
    }

    private ref Entry EnsureEntry(int id)
    {
        if (_entries == null)
        {
            _entries = new Entry[Math.Max(id + 1, 8)];
        }
        else if (id >= _entries.Length)
        {
            Array.Resize(ref _entries, Math.Max(id + 1, _entries.Length * 2));
        }

        return ref _entries[id];
    }

    private struct Entry
    {
        public static Entry Empty;

        public object? Value;       // plain value, or AnimatedEntry when animating
        public ValueSource Source;  // who set this value
    }
}

/// <summary>
/// Stores MewProperty references indexed by property Id for fast lookup.
/// </summary>
internal static class MewPropertyRegistry
{
    private static MewProperty?[] s_properties = new MewProperty?[16];
    private static readonly object s_lock = new();

    /// <summary>
    /// Registers a property reference. Called during <see cref="MewProperty{T}"/> construction.
    /// </summary>
    internal static void Register(MewProperty property)
    {
        lock (s_lock)
        {
            int id = property.Id;
            if (id >= s_properties.Length)
            {
                int newSize = Math.Max(id + 1, s_properties.Length * 2);
                Array.Resize(ref s_properties, newSize);
            }
            s_properties[id] = property;
        }
    }

    internal static MewProperty? GetProperty(int id)
    {
        return id < s_properties.Length ? s_properties[id] : null;
    }
}
