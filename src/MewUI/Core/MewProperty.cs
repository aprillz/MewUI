namespace Aprillz.MewUI;

/// <summary>
/// Non-generic base for <see cref="MewProperty{T}"/>.
/// Enables type-erased storage in <see cref="SetterBase"/> and <see cref="PropertyValueStore"/>.
/// </summary>
public abstract class MewProperty
{
    private static readonly List<MewProperty> _registry = new();
    private static readonly object _lock = new();

    /// <summary>Gets the property name (for diagnostics).</summary>
    public abstract string Name { get; }

    /// <summary>Gets the CLR type of the property value.</summary>
    public abstract Type ValueType { get; }

    /// <summary>Gets the metadata options.</summary>
    public abstract MewPropertyOptions Options { get; }

    /// <summary>Gets the boxed default value.</summary>
    internal abstract object BoxedDefaultValue { get; }

    /// <summary>
    /// Gets the boxed default value for a specific owner type,
    /// considering overrides registered via <see cref="MewProperty{T}.OverrideDefaultValue{TOwner}"/>.
    /// </summary>
    internal abstract object GetBoxedDefaultForType(Type ownerType);

    /// <summary>Gets the globally unique property identifier (array index).</summary>
    internal int Id { get; }

    /// <summary>
    /// True when this property was registered via <see cref="MewProperty{T}.RegisterReadOnly{TOwner}"/>.
    /// Read-only properties reject the public <c>SetValue(MewProperty&lt;T&gt;, T)</c> and
    /// <c>SetBinding</c> paths; the owner must use the <see cref="MewPropertyKey{T}"/> overload.
    /// </summary>
    public bool IsReadOnly => ReadOnlyKey != null;

    /// <summary>
    /// Capability token associated with a read-only registration. Used to verify that
    /// callers of the <see cref="MewPropertyKey{T}"/> overload actually possess the key.
    /// </summary>
    internal object? ReadOnlyKey { get; set; }

    /// <summary>
    /// Optional per-property callback that also receives old and new effective values (boxed).
    /// Registered via the <c>changed</c> parameter of <see cref="MewProperty{T}.Register{TOwner}"/>
    /// overload that accepts <c>Action{TOwner, T, T}</c>.
    /// </summary>
    internal Action<IPropertyOwner, object?, object?>? ChangedWithValuesCallback { get; set; }

    /// <summary>
    /// Optional coerce callback that adjusts the proposed value before it is stored.
    /// Receives the owner instance and proposed value (boxed), returns the coerced value.
    /// </summary>
    internal Func<IPropertyOwner, object, object>? CoerceCallback { get; set; }

    /// <summary>Whether value changes should trigger InvalidateVisual.</summary>
    public bool AffectsRender => (Options & MewPropertyOptions.AffectsRender) != 0;

    /// <summary>Whether value changes should trigger InvalidateLayout.</summary>
    public bool AffectsLayout => (Options & MewPropertyOptions.AffectsLayout) != 0;

    /// <summary>Whether this property inherits values from parent elements.</summary>
    public bool Inherits => (Options & MewPropertyOptions.Inherits) != 0;

    /// <summary>Whether Bind() defaults to TwoWay mode for this property.</summary>
    public bool BindsTwoWayByDefault => (Options & MewPropertyOptions.BindsTwoWayByDefault) != 0;

    internal MewProperty()
    {
        lock (_lock)
        {
            Id = _registry.Count;
            _registry.Add(this);
        }
    }

    /// <summary>Gets the total number of registered properties (for sizing internal arrays).</summary>
    internal static int RegisteredCount
    {
        get { lock (_lock) { return _registry.Count; } }
    }
}

/// <summary>
/// Identifies a visual property with value resolution and animation support.
/// Static descriptors — one instance per property, shared across all control instances.
/// </summary>
/// <typeparam name="T">The property value type.</typeparam>
public sealed class MewProperty<T> : MewProperty
{
    /// <inheritdoc/>
    public override string Name { get; }

    /// <inheritdoc/>
    public override Type ValueType => typeof(T);

    /// <inheritdoc/>
    public override MewPropertyOptions Options { get; }

    /// <inheritdoc/>
    internal override object BoxedDefaultValue { get; }

    /// <summary>Gets the default value for this property.</summary>
    public T DefaultValue { get; }

    private volatile KeyValuePair<Type, T>[]? _defaultOverrides;

    /// <summary>
    /// Overrides the default value of this property for a specific owner type.
    /// Call from a static constructor of the derived type.
    /// </summary>
    /// <typeparam name="TOwner">The owner type that should use the new default.</typeparam>
    /// <param name="newDefault">The new default value for that type.</param>
    public void OverrideDefaultValue<TOwner>(T newDefault)
    {
        lock (this)
        {
            var existing = _defaultOverrides;
            if (existing == null)
            {
                _defaultOverrides = [new(typeof(TOwner), newDefault)];
            }
            else
            {
                var newArray = new KeyValuePair<Type, T>[existing.Length + 1];
                existing.CopyTo(newArray, 0);
                newArray[existing.Length] = new(typeof(TOwner), newDefault);
                _defaultOverrides = newArray;
            }
        }
    }

    /// <summary>
    /// Gets the default value for a specific owner type, walking the type hierarchy
    /// to find the most derived override.
    /// </summary>
    internal T GetDefaultForType(Type ownerType)
    {
        var overrides = _defaultOverrides;
        if (overrides == null)
            return DefaultValue;

        var type = ownerType;
        while (type != null)
        {
            foreach (var kv in overrides)
            {
                if (kv.Key == type)
                    return kv.Value;
            }
            type = type.BaseType;
        }

        return DefaultValue;
    }

    /// <inheritdoc/>
    internal override object GetBoxedDefaultForType(Type ownerType)
    {
        // Fast path: no overrides — return cached boxed default to avoid re-boxing value types.
        if (_defaultOverrides == null)
            return BoxedDefaultValue;
        return GetDefaultForType(ownerType)!;
    }

    private MewProperty(string name, T defaultValue, MewPropertyOptions options)
    {
        Name = name;
        DefaultValue = defaultValue;
        BoxedDefaultValue = defaultValue!;
        Options = options;

        MewPropertyRegistry.Register(this);
    }

    /// <summary>
    /// Registers a new property descriptor with a change callback that receives old and new values.
    /// </summary>
    /// <typeparam name="TOwner">The declaring control type.</typeparam>
    /// <param name="name">Property name.</param>
    /// <param name="defaultValue">Default value when not set by style or local override.</param>
    /// <param name="options">Metadata flags.</param>
    /// <param name="changed">Callback invoked when the property value changes.</param>
    /// <param name="coerce">Callback invoked to coerce the property value.</param>
    public static MewProperty<T> Register<TOwner>(
        string name,
        T defaultValue,
        MewPropertyOptions options = MewPropertyOptions.None,
        Action<TOwner, T, T>? changed = null,
        Func<TOwner, T, T>? coerce = null)
    {
        var property = new MewProperty<T>(name, defaultValue, options);

        if (changed is not null)
        {
            property.ChangedWithValuesCallback = (owner, oldBoxed, newBoxed) =>
                changed(
                    (TOwner)(object)owner,
                    oldBoxed is T o ? o : defaultValue,
                    newBoxed is T n ? n : defaultValue);
        }

        if (coerce is not null)
        {
            property.CoerceCallback = (owner, value) =>
                coerce((TOwner)(object)owner, value is T v ? v : defaultValue)!;
        }

        return property;
    }

    /// <summary>
    /// Registers a new read-only property descriptor and returns a <see cref="MewPropertyKey{T}"/>
    /// that authorizes mutation. The owning type should expose the <c>Key.Property</c> as a
    /// public <c>MewProperty&lt;T&gt;</c> for read access while keeping the key private.
    /// </summary>
    /// <typeparam name="TOwner">The declaring control type.</typeparam>
    /// <param name="name">Property name.</param>
    /// <param name="defaultValue">Default value when not set by style or owner.</param>
    /// <param name="options">Metadata flags.</param>
    /// <param name="changed">Callback invoked when the property value changes.</param>
    /// <param name="coerce">Callback invoked to coerce the property value.</param>
    public static MewPropertyKey<T> RegisterReadOnly<TOwner>(
        string name,
        T defaultValue,
        MewPropertyOptions options = MewPropertyOptions.None,
        Action<TOwner, T, T>? changed = null,
        Func<TOwner, T, T>? coerce = null)
    {
        var property = Register<TOwner>(name, defaultValue, options, changed, coerce);
        var key = new MewPropertyKey<T>(property);
        property.ReadOnlyKey = key;
        return key;
    }
}

/// <summary>
/// Capability token for mutating a read-only <see cref="MewProperty{T}"/>.
/// Hold privately on the declaring type; expose only <see cref="Property"/> publicly.
/// </summary>
/// <typeparam name="T">The property value type.</typeparam>
public sealed class MewPropertyKey<T>
{
    /// <summary>The underlying read-only property descriptor (safe to expose publicly for reads).</summary>
    public MewProperty<T> Property { get; }

    internal MewPropertyKey(MewProperty<T> property)
    {
        Property = property;
    }
}
