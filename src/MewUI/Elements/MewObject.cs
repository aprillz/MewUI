using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class providing the MewProperty system: per-instance value storage,
/// change notification, and data binding.
/// Analogous to WPF's DependencyObject.
/// </summary>
public abstract class MewObject : IPropertyOwner
{
    private PropertyValueStore? _propertyStore;
    private Dictionary<int, IPropertyBinding>? _propertyBindings;
    private Dictionary<int, BindingRuntimeState>? _bindingStates;
    private Dictionary<int, Action<BindingError?>>? _bindingErrorChangedCallbacks;
    private Dictionary<int, Action>? _propertyBindingCallbacks;
    // Value is a PropertyForwardEntry for the common single-forward case, or a
    // List<PropertyForwardEntry> once a second forward is registered for the same source property.
    private Dictionary<int, object>? _propertyForwards;

    /// <summary>
    /// Per-instance value storage and animation management.
    /// Lazy - objects that don't use MewProperty have no allocation.
    /// </summary>
    internal PropertyValueStore PropertyStore
        => _propertyStore ??= new PropertyValueStore(this);

    /// <summary>
    /// Returns true if the lazy <see cref="PropertyStore"/> has been allocated.
    /// Used by inheritance resolution to avoid unnecessary allocation on ancestor elements.
    /// </summary>
    internal bool HasPropertyStore => _propertyStore != null;

    /// <summary>
    /// IPropertyOwner - notification pipeline:
    /// 1. OnMewPropertyChanged (virtual) - cross-cutting: layout/render invalidation, font cache, inheritance
    /// 2. ChangedCallback - per-property side effects registered at MewProperty.Register time
    /// 3. Binding callbacks - propagate final value to bound ObservableValues
    /// </summary>
    void IPropertyOwner.OnPropertyChanged(MewProperty property, object? oldValue, object? newValue)
    {
        OnMewPropertyChanged(property);
        NotifyObservers(property, oldValue, newValue);
    }

    // Fires the binding-observer side of a value change (property forwards, binding callbacks,
    // ChangedWithValues), WITHOUT the cross-cutting OnMewPropertyChanged work. The inherited-change
    // propagation path uses this directly so it doesn't re-trigger inheritance recursion.
    private void NotifyObservers(MewProperty property, object? oldValue, object? newValue)
    {
        if (_propertyForwards != null &&
            _propertyForwards.TryGetValue(property.Id, out var forward))
        {
            if (forward is List<PropertyForwardEntry> list)
            {
                for (var index = 0; index < list.Count; index++)
                {
                    var entry = list[index];
                    if (entry.TryGetTarget(out var target))
                    {
                        entry.UpdateTarget(target, newValue);
                    }
                    else
                    {
                        list.RemoveAt(index);
                        index--;
                    }
                }

                if (list.Count == 0)
                    _propertyForwards.Remove(property.Id);
            }
            else
            {
                var entry = (PropertyForwardEntry)forward;
                if (entry.TryGetTarget(out var target))
                {
                    entry.UpdateTarget(target, newValue);
                }
                else
                {
                    _propertyForwards.Remove(property.Id);
                }
            }
        }

        if (_propertyBindingCallbacks?.TryGetValue(property.Id, out var cb) == true)
        {
            cb();
        }
        property.ChangedWithValuesCallback?.Invoke(this, oldValue, newValue);
    }

    /// <summary>
    /// True if a property-to-property forward or a binding callback is registered for the property.
    /// Used by inherited-change propagation to decide whether to eagerly resolve + notify.
    /// </summary>
    internal bool HasChangeObservers(int propertyId)
        => (_propertyForwards?.ContainsKey(propertyId) ?? false)
           || (_propertyBindingCallbacks?.ContainsKey(propertyId) ?? false);

    /// <summary>Adds the ids of every property something is observing on this object.</summary>
    internal void GetObservedPropertyIds(List<int> result)
    {
        if (_propertyForwards != null)
        {
            foreach (int id in _propertyForwards.Keys)
            {
                result.Add(id);
            }
        }

        if (_propertyBindingCallbacks != null)
        {
            foreach (int id in _propertyBindingCallbacks.Keys)
            {
                if (!result.Contains(id))
                {
                    result.Add(id);
                }
            }
        }
    }

    /// <summary>
    /// Fires observers (forwards/binding callbacks) for an inherited-value change that was resolved
    /// outside the normal SetValue path. Does not run OnMewPropertyChanged (the caller already
    /// invalidates and walks descendants).
    /// </summary>
    internal void NotifyObserversBoxed(MewProperty property, object? oldValue, object? newValue)
        => NotifyObservers(property, oldValue, newValue);

    /// <summary>
    /// Called when a MewProperty value changes. Override to add control-specific handling.
    /// </summary>
    protected virtual void OnMewPropertyChanged(MewProperty property) { }

    /// <summary>
    /// Called when a mutation through this object's API moved a property to a different value tier,
    /// whether or not the effective value changed. Override for state that depends on which tier
    /// supplies a value (e.g. "the caller supplied content" versus "nothing did") rather than on the
    /// value itself.
    /// </summary>
    /// <param name="property">The property whose value source changed.</param>
    protected virtual void OnValueSourceChanged(MewProperty property) { }

    // A mutation can move a property between tiers while the effective value stays equal, and the
    // change pipeline stays silent then (see PropertyValueStore.SetValueCore). Provenance-dependent
    // state would go stale, so the tier transition itself is reported here.
    private void NotifyIfValueSourceChanged(MewProperty property, ValueSource oldSource)
    {
        if (PropertyStore.GetSource(property.Id) != oldSource)
        {
            OnValueSourceChanged(property);
        }
    }

    /// <summary>
    /// Gets the current (possibly interpolated) value of a visual property.
    /// For properties with <see cref="MewPropertyOptions.Inherits"/>, walks the parent chain
    /// when no local or style value exists on this element.
    /// </summary>
    protected T GetValue<T>(MewProperty<T> property)
    {
        if (!property.Inherits)
            return PropertyStore.GetValue(property);

        var source = PropertyStore.GetSource(property.Id);
        if (source > ValueSource.Inherited)
            return PropertyStore.GetValue(property);

        if (source == ValueSource.Inherited && IsInheritedCacheCurrent())
            return PropertyStore.GetValue(property);

        return ResolveInheritedValue(property);
    }

    /// <summary>
    /// Reads an effective property value for the binding infrastructure, including inheritance.
    /// </summary>
    internal T GetBindingValue<T>(MewProperty<T> property) => GetValue(property);

    /// <summary>
    /// Boxed effective-value read for binding and inherited-context refresh paths.
    /// </summary>
    internal object? GetBindingValue(MewProperty property)
    {
        if (!property.Inherits)
            return PropertyStore.GetBoxedValue(property);

        var source = PropertyStore.GetSource(property.Id);
        if (source > ValueSource.Inherited)
            return PropertyStore.GetBoxedValue(property);

        if (source == ValueSource.Inherited && IsInheritedCacheCurrent())
            return PropertyStore.GetBoxedValue(property);

        return ResolveInheritedValueBoxed(property);
    }

    // Whether cached inherited values still match the current ancestor chain.
    // Elements override this with a context-version check so a reparent invalidates lazily.
    private protected virtual bool IsInheritedCacheCurrent() => true;

    /// <summary>
    /// Resolves an inherited property value by walking the parent chain.
    /// Override in subclasses that participate in a visual tree.
    /// </summary>
    protected virtual T ResolveInheritedValue<T>(MewProperty<T> property)
        => property.GetDefaultForType(PropertyStore.OwnerType);

    /// <summary>
    /// Boxed counterpart of <see cref="ResolveInheritedValue{T}"/>.
    /// </summary>
    internal virtual object? ResolveInheritedValueBoxed(MewProperty property)
        => property.GetBoxedDefaultForType(PropertyStore.OwnerType);

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

        var oldSource = PropertyStore.GetSource(property.Id);

        bool hadBinding = HasPropertyBinding(property.Id);
        if (!hadBinding)
        {
            PropertyStore.SetLocal(property, value);
            NotifyIfValueSourceChanged(property, oldSource);
            return;
        }

        PropertyStore.ValidateValueCandidate(property, value);
        BindingDiagnostics.ReportDirectWrite(this, property);
        DisposeExistingBinding(property.Id);
        PropertyStore.SetLocalPrevalidated(property, value);
        PropertyStore.ClearSource(property.Id, ValueSource.Binding);
        NotifyIfValueSourceChanged(property, oldSource);
    }

    /// <summary>
    /// Updates a binding-owned target value without bypassing read-only, validation, or coercion
    /// rules.
    /// </summary>
    internal void UpdateBindingTarget<T>(MewProperty<T> property, T value)
    {
        ThrowIfReadOnly(property);
        PropertyStore.SetBinding(property, value);
    }

    internal void UpdateBindingTarget(MewProperty property, object? value)
    {
        ThrowIfReadOnly(property);
        PropertyStore.SetBinding(property, value);
    }

    internal bool ApplyBindingTargetValue<T>(MewProperty<T> property, T value)
    {
        RecordBindingCandidate(property.Id, value);
        if (HasPropertyBinding(property.Id) &&
            PropertyStore.GetSource(property.Id) == ValueSource.Default &&
            EqualityComparer<T>.Default.Equals(GetBindingValue(property), value))
        {
            RecordBindingSuccess(property.Id, value);
            return true;
        }

        try
        {
            UpdateBindingTarget(property, value);
            object? effectiveCandidate = PropertyStore.GetSourceValue(property, ValueSource.Binding);
            RecordBindingSuccess(property.Id, effectiveCandidate);
            return true;
        }
        catch (Exception ex)
        {
            if (!HasPropertyBinding(property.Id))
            {
                throw;
            }

            ReportBindingError(
                property,
                value,
                BindingStatus.ValidationError,
                BindingErrorStage.TargetValidation,
                ex);
            return false;
        }
    }

    internal bool ApplyBindingTargetValue(MewProperty property, object? value)
    {
        RecordBindingCandidate(property.Id, value);
        if (HasPropertyBinding(property.Id) &&
            PropertyStore.GetSource(property.Id) == ValueSource.Default &&
            Equals(GetBindingValue(property), value))
        {
            RecordBindingSuccess(property.Id, value);
            return true;
        }

        try
        {
            UpdateBindingTarget(property, value);
            object? effectiveCandidate = PropertyStore.GetSourceValue(property, ValueSource.Binding);
            RecordBindingSuccess(property.Id, effectiveCandidate);
            return true;
        }
        catch (Exception ex)
        {
            if (!HasPropertyBinding(property.Id))
            {
                throw;
            }

            ReportBindingError(
                property,
                value,
                BindingStatus.ValidationError,
                BindingErrorStage.TargetValidation,
                ex);
            return false;
        }
    }

    /// <summary>
    /// Changes the current target value without replacing a binding. A later binding update can
    /// replace this value. When no binding supplies a target value, this sets a local value.
    /// </summary>
    public void SetCurrentValue<T>(MewProperty<T> property, T value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ThrowIfReadOnly(property);

        if (_propertyBindings?.TryGetValue(property.Id, out var binding) == true &&
            binding.Capabilities.ProvidesTargetValue)
        {
            RecordBindingCandidate(property.Id, value);
            try
            {
                binding.UpdateTargetValue(value);
                object? candidate = PropertyStore.GetSourceValue(property, ValueSource.Binding);
                RecordBindingSuccess(property.Id, candidate);
            }
            catch (Exception ex)
            {
                ReportBindingError(
                    property,
                    value,
                    BindingStatus.ValidationError,
                    BindingErrorStage.TargetValidation,
                    ex);
            }
            return;
        }

        PropertyStore.SetLocal(property, value);
    }

    /// <summary>
    /// Commits a target value to a TwoWay binding. OneWay bindings retain the target candidate
    /// without updating their source, and an unbound property receives a local value.
    /// </summary>
    protected void CommitTargetValue<T>(MewProperty<T> property, T value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ThrowIfReadOnly(property);

        if (_propertyBindings == null ||
            !_propertyBindings.TryGetValue(property.Id, out var binding) ||
            !binding.Capabilities.ProvidesTargetValue)
        {
            PropertyStore.SetLocal(property, value);
            return;
        }

        RecordBindingCandidate(property.Id, value);

        if (!binding.Capabilities.AcceptsTargetCommit)
        {
            try
            {
                binding.UpdateTargetValue(value);
                object? targetValue = PropertyStore.GetSourceValue(property, ValueSource.Binding);
                RecordBindingSuccess(property.Id, targetValue);
            }
            catch (Exception ex)
            {
                ReportBindingError(
                    property,
                    value,
                    BindingStatus.ValidationError,
                    BindingErrorStage.TargetValidation,
                    ex);
            }
            return;
        }

        object? candidate = value;
        try
        {
            PropertyStore.ValidateValueCandidate(property, value);
            if (candidate != null)
            {
                candidate = PropertyStore.CoerceValueCandidate(property, candidate);
            }
        }
        catch (Exception ex)
        {
            ReportBindingError(
                property,
                value,
                BindingStatus.ValidationError,
                BindingErrorStage.TargetValidation,
                ex);
            return;
        }

        BindingCommitResult result = binding.CommitTargetValue(candidate);
        if (!result.Succeeded)
        {
            try
            {
                binding.UpdateTargetValue(value);
            }
            catch (Exception ex)
            {
                ReportBindingError(
                    property,
                    value,
                    BindingStatus.ValidationError,
                    BindingErrorStage.TargetValidation,
                    ex);
                return;
            }

            ReportBindingError(property.Id, candidate, result.Error!);
            return;
        }

        try
        {
            binding.UpdateTargetValue(result.Value);
            object? normalized = PropertyStore.GetSourceValue(property, ValueSource.Binding);
            RecordBindingSuccess(property.Id, normalized);
        }
        catch (Exception ex)
        {
            ReportBindingError(
                property,
                candidate,
                BindingStatus.BindingError,
                BindingErrorStage.Consistency,
                ex);
        }
    }

    /// <summary>
    /// Clears only the local value for a property. Attached bindings and their target values are
    /// preserved.
    /// </summary>
    public void ClearLocalValue<T>(MewProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        ThrowIfReadOnly(property);
        if (PropertyStore.HasValue(property.Id, ValueSource.Local))
        {
            BindingDiagnostics.ReportLocalClear(this, property);
        }

        var oldSource = PropertyStore.GetSource(property.Id);
        PropertyStore.ClearLocalValue(property);
        NotifyIfValueSourceChanged(property, oldSource);
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
        PropertyStore.CoerceValue(property);
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
        PreparePropertyBinding(property.Id);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault ? BindingMode.TwoWay : BindingMode.OneWay);
        var binding = new MewPropertyBinding<T>(this, property, source, resolvedMode);
        ActivatePropertyBinding(property.Id, binding);
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

        PreparePropertyBinding(property.Id);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault ? BindingMode.TwoWay : BindingMode.OneWay);
        if (resolvedMode == BindingMode.TwoWay && convertBack == null)
        {
            resolvedMode = BindingMode.OneWay;
        }

        var binding = new MewPropertyBinding<TProp, TSource>(
            this, property, source, convert, convertBack, resolvedMode);
        ActivatePropertyBinding(property.Id, binding);
    }

    /// <summary>
    /// Binds a property to a reusable delegate-based path rooted at <paramref name="source"/>.
    /// Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TRoot, T>(
        MewProperty<T> property,
        TRoot source,
        BindingPath<TRoot, T> path,
        BindingMode? mode = null,
        T fallbackValue = default!)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfReadOnly(property);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault
            ? BindingMode.TwoWay
            : BindingMode.OneWay);
        if (resolvedMode == BindingMode.TwoWay && !path.CanWrite)
        {
            throw new ArgumentException(
                "A TwoWay BindingPath must end in a writable ObservableValue or MewProperty.",
                nameof(path));
        }

        PreparePropertyBinding(property.Id);

        var binding = new MewPropertyPathBinding<T, TRoot, T>(
            this,
            property,
            source,
            path,
            static value => value,
            static value => value,
            resolvedMode,
            fallbackValue);
        ActivatePropertyBinding(property.Id, binding);
    }

    /// <summary>
    /// Binds a property to a reusable delegate-based path with type conversion.
    /// Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TProp, TRoot, TSource>(
        MewProperty<TProp> property,
        TRoot source,
        BindingPath<TRoot, TSource> path,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack = null,
        BindingMode? mode = null,
        TProp fallbackValue = default!)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(convert);
        ThrowIfReadOnly(property);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault
            ? BindingMode.TwoWay
            : BindingMode.OneWay);
        if (resolvedMode == BindingMode.TwoWay && convertBack == null)
        {
            throw new ArgumentException(
                "A converted TwoWay BindingPath requires convertBack.",
                nameof(convertBack));
        }

        if (resolvedMode == BindingMode.TwoWay && !path.CanWrite)
        {
            throw new ArgumentException(
                "A TwoWay BindingPath must end in a writable ObservableValue or MewProperty.",
                nameof(path));
        }

        PreparePropertyBinding(property.Id);

        var binding = new MewPropertyPathBinding<TProp, TRoot, TSource>(
            this,
            property,
            source,
            path,
            convert,
            convertBack,
            resolvedMode,
            fallbackValue);
        ActivatePropertyBinding(property.Id, binding);
    }

    /// <summary>
    /// Binds a property to a single notifying property of <paramref name="source"/>. Omitting
    /// <paramref name="setter"/> makes the binding OneWay. Replaces any existing binding for the
    /// same property.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="getter"/> is not a single member access such as
    /// <c>x =&gt; x.Name</c>.
    /// </exception>
    public void SetBinding<TSource, T>(
        MewProperty<T> property,
        TSource source,
        Func<TSource, T> getter,
        Action<TSource, T>? setter = null,
        BindingMode? mode = null,
        [CallerArgumentExpression(nameof(getter))] string? getterExpression = null)
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);

        var path = BindingPath.From<TSource>().ThenNotifying(getter, setter, getterExpression);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault
            ? BindingMode.TwoWay
            : BindingMode.OneWay);
        if (resolvedMode == BindingMode.TwoWay && setter == null)
        {
            throw new ArgumentException(BuildMissingSetterMessage(property), nameof(setter));
        }

        SetBinding(property, source, path, resolvedMode, fallbackValue: default!);
    }

    /// <summary>
    /// Binds a property to a single notifying property of <paramref name="source"/> with type
    /// conversion. The binding is TwoWay only when both <paramref name="setter"/> and
    /// <paramref name="convertBack"/> are supplied.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="getter"/> is not a single member access such as
    /// <c>x =&gt; x.Name</c>.
    /// </exception>
    public void SetBinding<TProp, TSource, TValue>(
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, TValue> getter,
        Func<TValue, TProp> convert,
        Action<TSource, TValue>? setter = null,
        Func<TProp, TValue>? convertBack = null,
        BindingMode? mode = null,
        [CallerArgumentExpression(nameof(getter))] string? getterExpression = null)
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        var path = BindingPath.From<TSource>().ThenNotifying(getter, setter, getterExpression);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault
            ? BindingMode.TwoWay
            : BindingMode.OneWay);
        if (resolvedMode == BindingMode.TwoWay && setter == null)
        {
            throw new ArgumentException(BuildMissingSetterMessage(property), nameof(setter));
        }

        if (resolvedMode == BindingMode.TwoWay && convertBack == null)
        {
            resolvedMode = BindingMode.OneWay;
        }

        SetBinding(
            property, source, path, convert, convertBack, resolvedMode, fallbackValue: default!);
    }

    /// <summary>
    /// Binds a property to an <see cref="ObservableValue{T}"/> reached through
    /// <paramref name="source"/>. The wrapper supplies the change notification, so
    /// <paramref name="source"/> need not raise property change events.
    /// </summary>
    public void SetBinding<TSource, T>(
        MewProperty<T> property,
        TSource source,
        Func<TSource, ObservableValue<T>> selector,
        BindingMode? mode = null)
        where TSource : class
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        SetBinding(
            property,
            source,
            BindingPath.From<TSource>().Then(selector),
            mode,
            fallbackValue: default!);
    }

    private static string BuildMissingSetterMessage(MewProperty property)
        => $"A TwoWay binding to '{property.Name}' needs a way to write back. Pass a setter, "
            + "bind with BindingMode.OneWay, or build with an SDK new enough to run the MewUI "
            + "binding path generator, which writes the setter for you.";

    /// <summary>
    /// Removes the binding and its target value from the specified property, revealing the next
    /// lower value source.
    /// </summary>
    public void ClearBinding<T>(MewProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (HasPropertyBinding(property.Id) || HasBindingTargetValue(property.Id))
        {
            BindingDiagnostics.ReportBindingClear(this, property);
        }

        var oldSource = PropertyStore.GetSource(property.Id);
        DisposeExistingBinding(property.Id);
        PropertyStore.ClearSource(property.Id, ValueSource.Binding);
        NotifyIfValueSourceChanged(property, oldSource);
    }

    private static void ThrowIfReadOnly(MewProperty property)
    {
        if (property.IsReadOnly)
        {
            throw new InvalidOperationException(
                $"'{property.Name}' is a read-only property and cannot be bound externally.");
        }
    }

    internal bool HasPropertyBinding(int propertyId)
        => _propertyBindings?.ContainsKey(propertyId) == true;

    internal bool HasBindingTargetValue(int propertyId)
        => _propertyStore?.HasValue(propertyId, ValueSource.Binding) == true;

    internal BindingStateSnapshot? GetBindingState(int propertyId)
    {
        if (_bindingStates == null || !_bindingStates.TryGetValue(propertyId, out var state))
        {
            return null;
        }

        return new BindingStateSnapshot(
            state.HasCurrentCandidate,
            state.CurrentCandidate,
            state.HasLastSuccessfulTargetValue,
            state.LastSuccessfulTargetValue,
            state.Error);
    }

    internal PropertyValueTrace GetPropertyValueTrace(MewProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        // Resolve inherited values through the same context path as GetValue before taking the
        // slot snapshot. This materializes the current Inherited candidate when one exists.
        _ = GetBindingValue(property);
        return PropertyStore.GetValueTrace(property, GetBindingState(property.Id));
    }

    internal void AddBindingErrorChangedCallback(int propertyId, Action<BindingError?> callback)
    {
        _bindingErrorChangedCallbacks ??= new Dictionary<int, Action<BindingError?>>(capacity: 2);
        if (_bindingErrorChangedCallbacks.TryGetValue(propertyId, out var existing))
            _bindingErrorChangedCallbacks[propertyId] = existing + callback;
        else
            _bindingErrorChangedCallbacks[propertyId] = callback;
    }

    internal void RemoveBindingErrorChangedCallback(int propertyId, Action<BindingError?> callback)
    {
        if (_bindingErrorChangedCallbacks?.TryGetValue(propertyId, out var existing) != true)
        {
            return;
        }

        var updated = existing - callback;
        if (updated == null)
            _bindingErrorChangedCallbacks.Remove(propertyId);
        else
            _bindingErrorChangedCallbacks[propertyId] = updated;
    }

    internal void ReportBindingError<T>(
        MewProperty<T> property,
        object? candidate,
        BindingStatus status,
        BindingErrorStage stage,
        Exception exception)
        => ReportBindingError(
            property.Id,
            candidate,
            new BindingError(status, stage, exception.Message, exception));

    internal void ReportBindingError(
        MewProperty property,
        object? candidate,
        BindingStatus status,
        BindingErrorStage stage,
        Exception exception)
        => ReportBindingError(
            property.Id,
            candidate,
            new BindingError(status, stage, exception.Message, exception));

    private void ReportBindingError(int propertyId, object? candidate, BindingError error)
    {
        if (_propertyBindings?.ContainsKey(propertyId) != true)
        {
            return;
        }

        var state = GetOrCreateBindingState(propertyId);
        state.HasCurrentCandidate = true;
        state.CurrentCandidate = candidate;
        state.Error = error;
        NotifyBindingErrorChanged(propertyId, error);
    }

    private void RecordBindingCandidate(int propertyId, object? candidate)
    {
        if (_propertyBindings?.ContainsKey(propertyId) != true)
        {
            return;
        }

        var state = GetOrCreateBindingState(propertyId);
        state.HasCurrentCandidate = true;
        state.CurrentCandidate = candidate;
    }

    private void RecordBindingSuccess(int propertyId, object? value)
    {
        if (_propertyBindings?.ContainsKey(propertyId) != true)
        {
            return;
        }

        var state = GetOrCreateBindingState(propertyId);
        bool hadError = state.Error != null;
        state.HasCurrentCandidate = true;
        state.CurrentCandidate = value;
        state.HasLastSuccessfulTargetValue = true;
        state.LastSuccessfulTargetValue = value;
        state.Error = null;
        if (hadError)
        {
            NotifyBindingErrorChanged(propertyId, null);
        }
    }

    private BindingRuntimeState GetOrCreateBindingState(int propertyId)
    {
        _bindingStates ??= new Dictionary<int, BindingRuntimeState>(capacity: 2);
        if (!_bindingStates.TryGetValue(propertyId, out var state))
        {
            state = new BindingRuntimeState();
            _bindingStates[propertyId] = state;
        }

        return state;
    }

    private void ClearBindingState(int propertyId)
    {
        if (_bindingStates?.Remove(propertyId, out var state) == true && state.Error != null)
        {
            NotifyBindingErrorChanged(propertyId, null);
        }
    }

    private void NotifyBindingErrorChanged(int propertyId, BindingError? error)
    {
        OnBindingErrorChanged(propertyId, error);

        if (_bindingErrorChangedCallbacks?.TryGetValue(propertyId, out var callback) == true)
        {
            callback(error);
        }
    }

    /// <summary>
    /// Allows framework types to project per-property binding failures into a higher-level state.
    /// </summary>
    internal virtual void OnBindingErrorChanged(int propertyId, BindingError? error)
    { }

    private void DisposeExistingBinding(int propertyId)
    {
        if (_propertyBindings?.TryGetValue(propertyId, out var old) == true)
        {
            _propertyBindings.Remove(propertyId);
            try { old.Dispose(); }
            catch { /* best-effort */ }
        }

        ClearBindingState(propertyId);
    }

    private void StorePropertyBinding(int propertyId, IPropertyBinding binding)
    {
        _propertyBindings ??= new Dictionary<int, IPropertyBinding>(capacity: 2);
        _propertyBindings[propertyId] = binding;
        GetOrCreateBindingState(propertyId);
    }

    private void ActivatePropertyBinding(int propertyId, IPropertyBinding binding)
    {
        var oldSource = PropertyStore.GetSource(propertyId);
        StorePropertyBinding(propertyId, binding);
        try
        {
            binding.Initialize();
        }
        catch
        {
            DisposeExistingBinding(propertyId);
            PropertyStore.ClearSource(propertyId, ValueSource.Binding);
            throw;
        }

        if (MewPropertyRegistry.GetProperty(propertyId) is MewProperty property)
        {
            NotifyIfValueSourceChanged(property, oldSource);
        }
    }

    private void PreparePropertyBinding(int propertyId)
    {
        var property = MewPropertyRegistry.GetProperty(propertyId);
        if (property != null)
        {
            if (HasPropertyBinding(propertyId))
            {
                BindingDiagnostics.ReportBindingReplacement(this, property);
            }

            if (PropertyStore.HasValue(propertyId, ValueSource.Local))
            {
                BindingDiagnostics.ReportLocalReplacement(this, property);
            }
        }

        DisposeExistingBinding(propertyId);
        PropertyStore.ClearSource(propertyId, ValueSource.Local);
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

    // Returns the created entry so the caller can remove exactly this forward later,
    // even if another forward is later added for the same source property.
    internal PropertyForwardEntry AddPropertyForward(
        int sourcePropertyId,
        MewObject target,
        MewProperty targetProperty,
        ValueSource targetSource = ValueSource.Binding)
    {
        _propertyForwards ??= new(capacity: 2);
        var entry = new PropertyForwardEntry(target, targetProperty, targetSource);
        if (_propertyForwards.TryGetValue(sourcePropertyId, out var existing))
        {
            if (existing is List<PropertyForwardEntry> list)
                list.Add(entry);
            else
                _propertyForwards[sourcePropertyId] = new List<PropertyForwardEntry> { (PropertyForwardEntry)existing, entry };
        }
        else
        {
            _propertyForwards[sourcePropertyId] = entry;
        }

        return entry;
    }

    // Removes only the given entry, leaving any other forward registered for the same
    // source property id intact.
    internal void RemovePropertyForward(int sourcePropertyId, PropertyForwardEntry entry)
    {
        if (_propertyForwards == null || !_propertyForwards.TryGetValue(sourcePropertyId, out var existing))
            return;

        if (existing is List<PropertyForwardEntry> list)
        {
            list.Remove(entry);
            if (list.Count == 0)
                _propertyForwards.Remove(sourcePropertyId);
            else if (list.Count == 1)
                _propertyForwards[sourcePropertyId] = list[0];
        }
        else if (ReferenceEquals(existing, entry))
        {
            _propertyForwards.Remove(sourcePropertyId);
        }
    }

    /// <summary>
    /// Binds a <see cref="MewProperty{T}"/> on this object to a <see cref="MewProperty{T}"/> on a source object.
    /// When the source property changes, this object's binding value is updated without replacing
    /// a local value. Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<T>(MewProperty<T> property, MewObject source, MewProperty<T> sourceProperty)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceProperty);
        ThrowIfReadOnly(property);

        PreparePropertyBinding(property.Id);

        var binding = new MewObjectPropertyBinding<T>(this, property, source, sourceProperty);
        ActivatePropertyBinding(property.Id, binding);
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

        PreparePropertyBinding(property.Id);

        var resolvedMode = mode ?? (property.BindsTwoWayByDefault ? BindingMode.TwoWay : BindingMode.OneWay);
        if (resolvedMode == BindingMode.TwoWay && convertBack == null)
        {
            resolvedMode = BindingMode.OneWay;
        }

        var binding = new MewObjectPropertyBinding<TProp, TSource>(
            this, property, source, sourceProperty, convert, convertBack, resolvedMode);
        ActivatePropertyBinding(property.Id, binding);
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

        _bindingStates?.Clear();
        _bindingStates = null;

        _bindingErrorChangedCallbacks?.Clear();
        _bindingErrorChangedCallbacks = null;

        _propertyBindingCallbacks?.Clear();
        _propertyBindingCallbacks = null;

        _propertyForwards?.Clear();
        _propertyForwards = null;
    }

    private sealed class BindingRuntimeState
    {
        public bool HasCurrentCandidate;
        public object? CurrentCandidate;
        public bool HasLastSuccessfulTargetValue;
        public object? LastSuccessfulTargetValue;
        public BindingError? Error;
    }
}

/// <summary>
/// Stores the target of a MewProperty-to-MewProperty binding forward.
/// </summary>
internal sealed class PropertyForwardEntry
{
    private readonly WeakReference<MewObject> _target;

    public PropertyForwardEntry(
        MewObject target,
        MewProperty targetProperty,
        ValueSource targetSource)
    {
        _target = new WeakReference<MewObject>(target);
        TargetProperty = targetProperty;
        TargetSource = targetSource;
    }

    public MewProperty TargetProperty { get; }

    public ValueSource TargetSource { get; }

    public bool TryGetTarget(out MewObject target) => _target.TryGetTarget(out target!);

    public void UpdateTarget(MewObject target, object? value)
    {
        if (TargetSource == ValueSource.Binding)
            target.ApplyBindingTargetValue(TargetProperty, value);
        else
            target.PropertyStore.SetValue(TargetProperty, value, TargetSource);
    }
}
