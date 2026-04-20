namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class providing the MewProperty system: per-instance value storage,
/// change notification, and data binding.
/// Analogous to WPF's DependencyObject.
/// </summary>
public abstract class MewObject : IPropertyOwner
{
    private PropertyValueStore? _propertyStore;
    private Dictionary<int, IDisposable>? _propertyBindings;
    private Dictionary<int, Action>? _propertyBindingCallbacks;
    private Dictionary<int, PropertyForwardEntry>? _propertyForwards;

    /// <summary>
    /// Per-instance value storage and animation management.
    /// Lazy — objects that don't use MewProperty have no allocation.
    /// </summary>
    internal PropertyValueStore PropertyStore
        => _propertyStore ??= new PropertyValueStore(this);

    /// <summary>
    /// Returns true if the lazy <see cref="PropertyStore"/> has been allocated.
    /// Used by inheritance resolution to avoid unnecessary allocation on ancestor elements.
    /// </summary>
    internal bool HasPropertyStore => _propertyStore != null;

    /// <summary>
    /// IPropertyOwner — notification pipeline:
    /// 1. OnMewPropertyChanged (virtual) — cross-cutting: layout/render invalidation, font cache, inheritance
    /// 2. ChangedCallback — per-property side effects registered at MewProperty.Register time
    /// 3. Binding callbacks — propagate final value to bound ObservableValues
    /// </summary>
    void IPropertyOwner.OnPropertyChanged(MewProperty property, object? oldValue, object? newValue)
    {
        OnMewPropertyChanged(property);

        if (_propertyForwards != null && newValue != null &&
            _propertyForwards.TryGetValue(property.Id, out var fwd))
        {
            fwd.Target.PropertyStore.SetTarget(fwd.TargetProperty, newValue);
        }

        if (_propertyBindingCallbacks?.TryGetValue(property.Id, out var cb) == true)
        {
            cb();
        }
        property.ChangedWithValuesCallback?.Invoke(this, oldValue, newValue);
    }

    /// <summary>
    /// Called when a MewProperty value changes. Override to add control-specific handling.
    /// </summary>
    protected virtual void OnMewPropertyChanged(MewProperty property) { }

    /// <summary>
    /// Gets the current (possibly interpolated) value of a visual property.
    /// For properties with <see cref="MewPropertyOptions.Inherits"/>, walks the parent chain
    /// when no local or style value exists on this element.
    /// </summary>
    protected T GetValue<T>(MewProperty<T> property)
    {
        if (PropertyStore.HasOwnValue(property.Id) || !property.Inherits)
            return PropertyStore.GetValue(property);

        return ResolveInheritedValue(property);
    }

    /// <summary>
    /// Resolves an inherited property value by walking the parent chain.
    /// Override in subclasses that participate in a visual tree.
    /// </summary>
    protected virtual T ResolveInheritedValue<T>(MewProperty<T> property)
        => property.GetDefaultForType(PropertyStore.OwnerType);

    /// <summary>
    /// Sets the local (user-defined) value of a property.
    /// Highest priority in value resolution.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="property"/> was registered via
    /// <see cref="MewProperty{T}.RegisterReadOnly{TOwner}"/>. Use the
    /// <see cref="SetValue{T}(MewPropertyKey{T}, T)"/> overload instead.
    /// </exception>
    protected void SetValue<T>(MewProperty<T> property, T value)
    {
        if (property.IsReadOnly)
        {
            throw new InvalidOperationException(
                $"'{property.Name}' is a read-only property. " +
                $"Use SetValue(MewPropertyKey<T>, T) with the registered key.");
        }

        PropertyStore.SetLocal(property, value);
    }

    /// <summary>
    /// Sets the local value of a read-only property using its capability key.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> does not match the property's registered key.</exception>
    protected void SetValue<T>(MewPropertyKey<T> key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);

        var property = key.Property;
        if (!ReferenceEquals(property.ReadOnlyKey, key))
        {
            throw new ArgumentException(
                $"Key does not match the registered read-only key for '{property.Name}'.",
                nameof(key));
        }

        PropertyStore.SetLocal(property, value);
    }

    /// <summary>
    /// Re-evaluates the coerce callback for a property. Call when external state
    /// that affects coercion has changed (e.g. WindowSize.IsResizable changed → re-coerce CanMaximize).
    /// </summary>
    protected void CoerceValue<T>(MewProperty<T> property)
    {
        if (property.CoerceCallback == null) return;
        var current = GetValue(property);
        PropertyStore.SetValue(property, current!, PropertyStore.GetSource(property.Id));
    }

    /// <summary>
    /// Binds a <see cref="MewProperty{T}"/> to an <see cref="ObservableValue{T}"/>.
    /// Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<T>(MewProperty<T> property, ObservableValue<T> source,
        BindingMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfReadOnly(property);

        // Dispose existing binding BEFORE creating the new one.
        // The new binding's constructor registers a callback by property.Id;
        // if the old binding were disposed afterwards, it would remove the new callback.
        DisposeExistingBinding(property.Id);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault ? BindingMode.TwoWay : BindingMode.OneWay);
        var binding = new MewPropertyBinding<T>(this, property, source, resolvedMode);
        StorePropertyBinding(property.Id, binding);
    }

    /// <summary>
    /// Binds a <see cref="MewProperty{TProp}"/> to an <see cref="ObservableValue{TSource}"/>
    /// with type conversion. Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TProp, TSource>(
        MewProperty<TProp> property,
        ObservableValue<TSource> source,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack = null,
        BindingMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);
        ThrowIfReadOnly(property);

        DisposeExistingBinding(property.Id);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault ? BindingMode.TwoWay : BindingMode.OneWay);
        if (resolvedMode == BindingMode.TwoWay && convertBack == null)
        {
            resolvedMode = BindingMode.OneWay;
        }

        var binding = new MewPropertyBinding<TProp, TSource>(
            this, property, source, convert, convertBack, resolvedMode);
        StorePropertyBinding(property.Id, binding);
    }

    private static void ThrowIfReadOnly(MewProperty property)
    {
        if (property.IsReadOnly)
        {
            throw new InvalidOperationException(
                $"'{property.Name}' is a read-only property and cannot be bound externally.");
        }
    }

    private void DisposeExistingBinding(int propertyId)
    {
        if (_propertyBindings?.TryGetValue(propertyId, out var old) == true)
        {
            _propertyBindings.Remove(propertyId);
            try { old.Dispose(); }
            catch { /* best-effort */ }
        }
    }

    private void StorePropertyBinding(int propertyId, IDisposable binding)
    {
        _propertyBindings ??= new Dictionary<int, IDisposable>(capacity: 2);
        _propertyBindings[propertyId] = binding;
    }

    internal void AddPropertyBindingCallback(int propertyId, Action callback)
    {
        _propertyBindingCallbacks ??= new Dictionary<int, Action>(capacity: 2);
        if (_propertyBindingCallbacks.TryGetValue(propertyId, out var existing))
            _propertyBindingCallbacks[propertyId] = existing + callback;
        else
            _propertyBindingCallbacks[propertyId] = callback;
    }

    internal void RemovePropertyBindingCallback(int propertyId, Action callback)
    {
        if (_propertyBindingCallbacks != null && _propertyBindingCallbacks.TryGetValue(propertyId, out var existing))
        {
            var updated = existing - callback;
            if (updated == null)
                _propertyBindingCallbacks.Remove(propertyId);
            else
                _propertyBindingCallbacks[propertyId] = updated;
        }
    }

    internal void AddPropertyForward(int sourcePropertyId, MewObject target, MewProperty targetProperty)
    {
        _propertyForwards ??= new(capacity: 2);
        _propertyForwards[sourcePropertyId] = new PropertyForwardEntry(target, targetProperty);
    }

    internal void RemovePropertyForward(int sourcePropertyId)
    {
        _propertyForwards?.Remove(sourcePropertyId);
    }

    /// <summary>
    /// Binds a <see cref="MewProperty{T}"/> on this object to a <see cref="MewProperty{T}"/> on a source object.
    /// When the source property changes, this object's property is updated at the style (target) tier,
    /// so local values on this object still take precedence.
    /// Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<T>(MewProperty<T> property, MewObject source, MewProperty<T> sourceProperty)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceProperty);
        ThrowIfReadOnly(property);

        DisposeExistingBinding(property.Id);

        var binding = new MewObjectPropertyBinding<T>(this, property, source, sourceProperty);
        StorePropertyBinding(property.Id, binding);
    }

    /// <summary>
    /// Binds a <see cref="MewProperty{TProp}"/> on this object to a <see cref="MewProperty{TSource}"/> on a source object
    /// with type conversion. Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TProp, TSource>(
        MewProperty<TProp> property,
        MewObject source,
        MewProperty<TSource> sourceProperty,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack = null,
        BindingMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceProperty);
        ArgumentNullException.ThrowIfNull(convert);
        ThrowIfReadOnly(property);

        DisposeExistingBinding(property.Id);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault ? BindingMode.TwoWay : BindingMode.OneWay);
        if (resolvedMode == BindingMode.TwoWay && convertBack == null)
        {
            resolvedMode = BindingMode.OneWay;
        }

        var binding = new MewObjectPropertyBinding<TProp, TSource>(
            this, property, source, sourceProperty, convert, convertBack, resolvedMode);
        StorePropertyBinding(property.Id, binding);
    }

    /// <summary>
    /// Disposes all property bindings. Called during element disposal.
    /// </summary>
    protected void DisposePropertyBindings()
    {
        if (_propertyBindings != null)
        {
            foreach (var kvp in _propertyBindings)
            {
                try { kvp.Value.Dispose(); }
                catch { /* best-effort */ }
            }

            _propertyBindings.Clear();
            _propertyBindings = null;
        }

        _propertyBindingCallbacks?.Clear();
        _propertyBindingCallbacks = null;

        _propertyForwards?.Clear();
        _propertyForwards = null;
    }
}

/// <summary>
/// Stores the target of a MewProperty-to-MewProperty binding forward.
/// </summary>
internal readonly record struct PropertyForwardEntry(MewObject Target, MewProperty TargetProperty);
