using System.Diagnostics;

namespace Aprillz.MewUI.Controls;

// Element triggers and transitions: conditional values and animated changes owned by the element
// instance, independent of the Style system, so they work on elements that are not controls
// (Image, TextBlock, shapes).
public abstract partial class UIElement
{
    // Declarations are snapshotted on assignment: in-place mutation of the caller's collection is
    // never observed, so validation and observer wiring happen in exactly one place (the setter).
    private Transition[]? _elementTransitions;
    private ElementTrigger[]? _elementTriggers;
    // Condition property ids the evaluation callback is registered on, for symmetric removal.
    private List<int>? _observedTriggerConditionIds;
    private Dictionary<int, ResolvedElementTriggerValue>? _appliedTriggerValues;
    private Dictionary<int, ResolvedElementTriggerValue>? _nextTriggerValues;
    private Action? _evaluateTriggersCallback;
    private bool _evaluatingElementTriggers;
    private bool _elementTriggerReevalPending;

    /// <summary>
    /// Transitions owned by this element. A registered property animates on every external value
    /// change - a direct set, a binding push, or an element trigger - unlike a style transition,
    /// which only animates style-resolved changes. Later entries win for the same property.
    /// <remarks>
    /// A registered property animates on every external value change (a direct set, a binding push,
    /// or an element trigger), unlike a style transition, which only animates style-resolved
    /// changes. Later entries win for the same property.
    /// </remarks>
    [FluentExcluded("Configured through the Transition builder")]
    /// The list is snapshotted on assignment; to change the set, assign a new list.
    /// </summary>
    public IReadOnlyList<Transition>? Transitions
    {
        get => _elementTransitions;
        set
        {
            _elementTransitions = value == null ? null : [.. value];
            if (value != null)
            {
                // The hook stays installed once set: with no registered transition it declines
                // every write, so an emptied list costs one lookup per set, not a stale animation.
                PropertyStore.AnimateSetCallback ??= TryAnimateExternalSet;
            }
        }
    }

    /// <summary>
    /// Conditional values owned by this element. While a trigger's condition property equals its
    /// <remarks>
    /// While a trigger's condition property equals its value, its setters apply above style values;
    /// leaving the condition removes them, revealing whatever is underneath. Later triggers win for
    /// the same target property. Style state triggers remain in the Style slot, so these values
    /// keep their independent, higher-priority ElementTrigger slot on controls as well as
    /// non-control elements.
    /// </remarks>
    [FluentExcluded("Configured through the trigger builders")]
    /// value, its setters apply above style values; leaving the condition removes them, revealing
    /// whatever is underneath. Later triggers win for the same target property. The list is
    /// snapshotted and validated on assignment; to change the set, assign a new list.
    /// Style state triggers remain in the Style slot, so these values keep their independent,
    /// higher-priority ElementTrigger slot on controls as well as non-control elements.
    /// </summary>
    public IReadOnlyList<ElementTrigger>? Triggers
    {
        get => _elementTriggers;
        set
        {
            ElementTrigger[]? snapshot = value == null ? null : [.. value];
            ValidateElementTriggers(snapshot);
            DetachTriggerObservers();
            _elementTriggers = snapshot;
            if (snapshot != null)
            {
                PropertyStore.AnimateSetCallback ??= TryAnimateExternalSet;
                AttachTriggerObservers();
            }
            EvaluateElementTriggers();
        }
    }

    private void ValidateElementTriggers(ElementTrigger[]? triggers)
    {
        if (triggers == null)
        {
            return;
        }

        for (int i = 0; i < triggers.Length; i++)
        {
            var trigger = triggers[i];

            if (!IsAssignableToProperty(trigger.Property, trigger.Value))
            {
                throw new ArgumentException(
                    $"Element trigger condition value for '{trigger.Property.Name}' is not compatible with {trigger.Property.ValueType.Name}.");
            }

            for (int j = 0; j < trigger.Setters.Count; j++)
            {
                var setter = trigger.Setters[j];
                if (setter is UnsetSetter)
                {
                    throw new ArgumentException(
                        $"Element triggers cannot contain {setter.GetType().Name}: there is no style candidate to unset.");
                }

                if (setter.Property.IsReadOnly)
                {
                    throw new ArgumentException(
                        $"Element trigger setters cannot target read-only property '{setter.Property.Name}'.");
                }

                if (setter.ThemeResolver == null && !IsAssignableToProperty(setter.Property, setter.Value))
                {
                    throw new ArgumentException(
                        $"Element trigger setter value for '{setter.Property.Name}' is not compatible with {setter.Property.ValueType.Name}.");
                }

                // A setter whose target is also some trigger's condition would re-enter evaluation:
                // self and mutual references oscillate, chains hide ordering. All are declarable
                // without the indirection (condition the later trigger on the original cause).
                for (int k = 0; k < triggers.Length; k++)
                {
                    if (triggers[k].Property.Id == setter.Property.Id)
                    {
                        throw new ArgumentException(
                            $"Element trigger setter targets '{setter.Property.Name}', which another trigger in the same list uses as its condition. Condition the dependent trigger on the original cause instead.");
                    }
                }

                if (HasPropertyBinding(setter.Property.Id))
                {
                    // Well-defined by tier priority (Local shadows the trigger value, which stays
                    // preserved underneath), but usually not what the author meant.
                    Debug.WriteLine(
                        $"MewUI: '{setter.Property.Name}' on {GetType().Name} has both a binding and an element trigger; the trigger value is shadowed while the binding is attached.");
                }
            }
        }
    }

    private static bool IsAssignableToProperty(MewProperty property, object? value)
    {
        if (value == null)
        {
            // Null is a value only for reference types and Nullable<T>.
            return !property.ValueType.IsValueType || Nullable.GetUnderlyingType(property.ValueType) != null;
        }

        return property.ValueType.IsInstanceOfType(value);
    }

    private void AttachTriggerObservers()
    {
        var triggers = _elementTriggers;
        if (triggers == null)
        {
            return;
        }

        _evaluateTriggersCallback ??= EvaluateElementTriggers;
        _observedTriggerConditionIds ??= new List<int>(capacity: 2);
        for (int i = 0; i < triggers.Length; i++)
        {
            int conditionId = triggers[i].Property.Id;
            if (!_observedTriggerConditionIds.Contains(conditionId))
            {
                _observedTriggerConditionIds.Add(conditionId);
                AddPropertyBindingCallback(conditionId, _evaluateTriggersCallback);
            }
        }
    }

    private void DetachTriggerObservers()
    {
        if (_observedTriggerConditionIds == null || _evaluateTriggersCallback == null)
        {
            return;
        }

        for (int i = 0; i < _observedTriggerConditionIds.Count; i++)
        {
            RemovePropertyBindingCallback(_observedTriggerConditionIds[i], _evaluateTriggersCallback);
        }
        _observedTriggerConditionIds.Clear();
    }

    /// <summary>
    /// Re-applies the trigger set from current condition values. Runs when a condition property
    /// changes and when the theme changes (theme-resolver setters resolve against the new theme).
    /// In-list feedback is a configuration error (validated at assignment); reentry through
    /// external feedback (an observer of a setter property writing a condition property back) is
    /// merged into one extra pass, with a small cap as a runaway stop.
    /// </summary>
    internal void EvaluateElementTriggers()
    {
        if (_evaluatingElementTriggers)
        {
            _elementTriggerReevalPending = true;
            return;
        }

        _evaluatingElementTriggers = true;
        try
        {
            const int MAX_MERGED_PASSES = 4;
            int pass = 0;
            do
            {
                _elementTriggerReevalPending = false;
                EvaluateElementTriggersCore();
            }
            while (_elementTriggerReevalPending && ++pass < MAX_MERGED_PASSES);

            if (_elementTriggerReevalPending)
            {
                Debug.WriteLine(
                    $"MewUI: element trigger evaluation on {GetType().Name} did not settle after {MAX_MERGED_PASSES} passes; external feedback keeps rewriting a condition property.");
            }
        }
        finally
        {
            _evaluatingElementTriggers = false;
            _elementTriggerReevalPending = false;
        }
    }

    private void EvaluateElementTriggersCore()
    {
        var triggers = _elementTriggers;
        if (triggers == null && _appliedTriggerValues == null)
        {
            return;
        }

        _nextTriggerValues ??= new Dictionary<int, ResolvedElementTriggerValue>(capacity: 2);
        _nextTriggerValues.Clear();
        var theme = (this as FrameworkElement)?.ThemeInternal;

        // Later matching triggers overwrite earlier ones per property: declaration order, no specificity.
        if (triggers != null)
        {
            for (int i = 0; i < triggers.Length; i++)
            {
                var trigger = triggers[i];
                if (!trigger.Matches(GetBindingValue(trigger.Property)))
                {
                    continue;
                }

                for (int j = 0; j < trigger.Setters.Count; j++)
                {
                    var setter = trigger.Setters[j];
                    if (setter.ThemeResolver != null && theme == null)
                    {
                        continue;
                    }

                    _nextTriggerValues[setter.Property.Id] =
                        new ResolvedElementTriggerValue(setter.Property, setter.ResolveValue(theme!));
                }
            }
        }

        if (_appliedTriggerValues != null)
        {
            foreach (var pair in _appliedTriggerValues)
            {
                if (!_nextTriggerValues.ContainsKey(pair.Key))
                {
                    ClearTriggerValueAnimated(pair.Key);
                }
            }
        }

        foreach (var pair in _nextTriggerValues)
        {
            if (_appliedTriggerValues != null &&
                _appliedTriggerValues.TryGetValue(pair.Key, out var oldValue) &&
                Equals(oldValue.Value, pair.Value.Value))
            {
                continue;
            }

            if (!TryAnimateExternalSet(pair.Value.Property, pair.Value.Value, ValueSource.ElementTrigger))
            {
                PropertyStore.SetElementTrigger(pair.Value.Property, pair.Value.Value);
            }
        }

        (_appliedTriggerValues, _nextTriggerValues) =
            (_nextTriggerValues, _appliedTriggerValues);
    }

    private readonly record struct ResolvedElementTriggerValue(MewProperty Property, object Value);

    private void ClearTriggerValueAnimated(int propertyId)
    {
        var property = MewPropertyRegistry.GetProperty(propertyId);
        var transition = FindElementTransition(propertyId);
        if (property == null || transition == null || Parent == null)
        {
            PropertyStore.ClearSource(propertyId, ValueSource.ElementTrigger);
            return;
        }

        object? from = PropertyStore.GetCurrentVisualValue(propertyId) ?? GetBindingValue(property);
        var mutation = PropertyStore.ClearSource(propertyId, ValueSource.ElementTrigger);
        if (mutation.IsEffectiveChange && from != null && mutation.NewValue != null)
        {
            Animator.AnimateFromTo(
                property,
                from,
                mutation.NewValue,
                transition.Duration,
                transition.Easing);
        }
    }

    private bool TryAnimateExternalSet(MewProperty property, object value, ValueSource source)
    {
        var transition = FindElementTransition(property.Id);
        if (transition == null)
        {
            return false;
        }

        // Detached writes snap: object-initializer assignments and teardown are not visible changes.
        if (Parent == null)
        {
            return false;
        }

        object? from = PropertyStore.GetCurrentVisualValue(property.Id) ?? GetBindingValue(property);
        var mutation = PropertyStore.SetValue(property, value, source);
        if (!mutation.IsEffectiveChange)
        {
            return true;
        }

        if (from != null && mutation.NewValue != null)
        {
            Animator.AnimateFromTo(
                property,
                from,
                mutation.NewValue,
                transition.Duration,
                transition.Easing);
        }
        return true;
    }

    private Transition? FindElementTransition(int propertyId)
    {
        var transitions = _elementTransitions;
        if (transitions == null)
        {
            return null;
        }

        for (int i = transitions.Length - 1; i >= 0; i--)
        {
            if (transitions[i].Property.Id == propertyId)
            {
                return transitions[i];
            }
        }

        return null;
    }
}
