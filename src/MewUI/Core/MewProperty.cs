namespace Aprillz.MewUI;

/// <summary>
/// Non-generic base for <see cref="MewProperty{T}"/>.
/// Enables type-erased storage in <see cref="SetterBase"/> and <see cref="PropertyValueStore"/>.
/// </summary>
public abstract class MewProperty
{
    private static readonly List<MewProperty> s_registry = new();
    private static readonly object s_lock = new();

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
    /// Optional per-property callback that also receives old and new effective values (boxed).
    /// Registered via the <c>changed</c> parameter of <see cref="MewProperty{T}.Register{TOwner}"/>
    /// overload that accepts <c>Action{TOwner, T, T}</c>.
    /// </summary>
    internal Action<IPropertyOwner, object?, object?>? ChangedWithValuesCallback { get; set; }

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
        lock (s_lock)
        {
            Id = s_registry.Count;
            s_registry.Add(this);
        }
    }

    /// <summary>Gets the total number of registered properties (for sizing internal arrays).</summary>
    internal static int RegisteredCount
    {
        get { lock (s_lock) { return s_registry.Count; } }
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
        => GetDefaultForType(ownerType)!;

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
    /// <param name="changed">Callback invoked when the property value changes.
    /// Receives the owner instance, old value, and new value.</param>
    public static MewProperty<T> Register<TOwner>(
        string name,
        T defaultValue,
        MewPropertyOptions options = MewPropertyOptions.None,
        Action<TOwner, T, T>? changed = null)
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
        return property;
    }
}
