using Aprillz.MewUI.Controls;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI;

/// <summary>
/// Defines default and state-conditional property values for a control type.
/// Created by Theme with palette colors; immutable after construction.
/// </summary>
public sealed class Style
{
    private static readonly object _freezeGate = new();
    private Style? _basedOn;
    private IReadOnlyList<SetterBase> _setters = [];
    private IReadOnlyList<StateTrigger> _triggers = [];
    private IReadOnlyList<Transition> _transitions = [];
    private bool _isFrozen;

    /// <summary>
    /// Gets whether this application style completely replaces the framework default style.
    /// The style's own <see cref="BasedOn"/> chain is still applied.
    /// </summary>
    public bool OverridesDefaultStyle { get; init; }

    /// <summary>
    /// Gets the default theme style for the specified control type.
    /// </summary>
    [RequiresUnreferencedCode(
        "Dynamic style lookup runs the target control's static constructor. Use ForType<T>() when the control type is known statically.")]
    public static Style? ForType(Type controlType)
    {
        ArgumentNullException.ThrowIfNull(controlType);
        if (!typeof(Control).IsAssignableFrom(controlType))
        {
            throw new ArgumentException(
                $"Style target type '{controlType.FullName}' must derive from Control.",
                nameof(controlType));
        }

        RuntimeHelpers.RunClassConstructor(controlType.TypeHandle);
        return DefaultStyles.GetStyle(controlType);
    }

    /// <summary>
    /// Gets the default theme style for the specified control type.
    /// </summary>
    public static Style? ForType<T>()
        where T : Control
    {
        EnsureDefaultStyleRegistration<T>();
        return DefaultStyles.GetStyle(typeof(T));
    }

    /// <summary>
    /// Creates a style that explicitly extends the nearest framework default style for
    /// <typeparamref name="T"/>. This does not change ordinary Style or StyleSheet lookup.
    /// </summary>
    public static Style DeriveFromDefault<
        T>(
        IReadOnlyList<SetterBase>? setters = null,
        IReadOnlyList<StateTrigger>? triggers = null,
        IReadOnlyList<Transition>? transitions = null)
        where T : Control
    {
        Type targetType = typeof(T);
        EnsureDefaultStyleRegistration<T>();
        DefaultStyles.EnsureRegistered<Control>(DefaultStyles.CreateControlBaseStyle);
        return DeriveFromRegisteredDefault(targetType, setters, triggers, transitions);
    }

    internal static Style DeriveFromRegisteredDefault(
        Type targetType,
        IReadOnlyList<SetterBase>? setters = null,
        IReadOnlyList<StateTrigger>? triggers = null,
        IReadOnlyList<Transition>? transitions = null)
    {
        Style? defaultStyle = null;
        for (Type? candidate = targetType;
             candidate != null && typeof(Control).IsAssignableFrom(candidate);
             candidate = candidate.BaseType)
        {
            defaultStyle = DefaultStyles.GetStyle(candidate);
            if (defaultStyle != null)
            {
                break;
            }
        }

        if (defaultStyle == null)
        {
            throw new InvalidOperationException(
                $"No framework default style is available for control type '{targetType.FullName}'.");
        }

        return new Style(targetType)
        {
            BasedOn = defaultStyle,
            Setters = setters ?? [],
            Triggers = triggers ?? [],
            Transitions = transitions ?? [],
        };
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2059",
        Justification = "The generic control type is statically reachable and owns its default-style registration in its static constructor.")]
    private static void EnsureDefaultStyleRegistration<T>() where T : Control
        => RuntimeHelpers.RunClassConstructor(typeof(T).TypeHandle);

    /// <summary>
    /// Gets the target control type this style applies to.
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// Gets the parent style to inherit from. Properties not defined in this style
    /// fall through to <see cref="BasedOn"/>.
    /// </summary>
    public Style? BasedOn
    {
        get => _basedOn;
        init => _basedOn = value;
    }

    /// <summary>
    /// Gets the base setters applied regardless of visual state (lowest priority within this style).
    /// </summary>
    public IReadOnlyList<SetterBase> Setters
    {
        get => _setters;
        init => _setters = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the state-conditional triggers. Matching triggers are evaluated in declaration order;
    /// for each property, the last active declaration wins.
    /// </summary>
    public IReadOnlyList<StateTrigger> Triggers
    {
        get => _triggers;
        init => _triggers = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the transitions that animate property changes from this style's setters and triggers.
    /// Resolved via <see cref="FindTransition"/> which walks the BasedOn chain.
    /// </summary>
    public IReadOnlyList<Transition> Transitions
    {
        get => _transitions;
        init => _transitions = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Style(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        if (!typeof(Control).IsAssignableFrom(targetType))
        {
            throw new ArgumentException(
                $"Style target type '{targetType.FullName}' must derive from Control.",
                nameof(targetType));
        }

        TargetType = targetType;
    }

    /// <summary>
    /// Finds the transition for the given property, walking the BasedOn chain.
    /// </summary>
    public Transition? FindTransition(int propertyId)
    {
        for (int i = Transitions.Count - 1; i >= 0; i--)
        {
            if (Transitions[i].Property.Id == propertyId)
                return Transitions[i];
        }

        return BasedOn?.FindTransition(propertyId);
    }

    internal void Freeze()
    {
        if (_isFrozen)
        {
            return;
        }

        lock (_freezeGate)
        {
            if (_isFrozen)
            {
                return;
            }

            FreezeCore(new HashSet<Style>());
        }
    }

    private void FreezeCore(HashSet<Style> visiting)
    {
        if (_isFrozen)
        {
            return;
        }

        if (!visiting.Add(this))
        {
            throw new InvalidOperationException(
                $"A BasedOn cycle was found while freezing the style targeting '{TargetType.FullName}'.");
        }

        try
        {
            if (_basedOn != null)
            {
                if (!_basedOn.TargetType.IsAssignableFrom(TargetType))
                {
                    throw new InvalidOperationException(
                        $"Style targeting '{TargetType.FullName}' cannot be based on a style targeting " +
                        $"'{_basedOn.TargetType.FullName}'.");
                }

                _basedOn.FreezeCore(visiting);
            }

            var setters = _setters.ToArray();
            var triggers = _triggers.ToArray();
            var transitions = _transitions.ToArray();

            ValidateSetters(setters, "Style.Setters");

            const VisualStateFlags definedFlags =
                VisualStateFlags.Enabled |
                VisualStateFlags.Hot |
                VisualStateFlags.Focused |
                VisualStateFlags.Pressed |
                VisualStateFlags.Checked |
                VisualStateFlags.Indeterminate |
                VisualStateFlags.Active |
                VisualStateFlags.Selected |
                VisualStateFlags.ReadOnly |
                VisualStateFlags.Invalid;

            for (int i = 0; i < triggers.Length; i++)
            {
                var trigger = triggers[i] ?? throw new InvalidOperationException(
                    $"Style.Triggers[{i}] cannot be null.");
                var usedFlags = trigger.Match | trigger.Exclude;
                if ((usedFlags & ~definedFlags) != 0)
                {
                    throw new InvalidOperationException(
                        $"Style.Triggers[{i}] uses undefined VisualStateFlags bits: " +
                        $"0x{(uint)(usedFlags & ~definedFlags):X8}.");
                }

                if ((trigger.Match & trigger.Exclude) != 0)
                {
                    throw new InvalidOperationException(
                        $"Style.Triggers[{i}] requires and excludes the same visual state flags.");
                }

                ValidateSetters(trigger.SnapshotSetters(), $"Style.Triggers[{i}].Setters");
            }

            for (int i = 0; i < transitions.Length; i++)
            {
                var transition = transitions[i] ?? throw new InvalidOperationException(
                    $"Style.Transitions[{i}] cannot be null.");
                ValidatePropertyOwner(transition.Property, $"Style.Transitions[{i}]");
                if (transition.Duration <= TimeSpan.Zero)
                {
                    throw new InvalidOperationException(
                        $"Style.Transitions[{i}] must have a duration greater than zero.");
                }
            }

            _setters = setters;
            _triggers = triggers;
            _transitions = transitions;
            _isFrozen = true;
        }
        finally
        {
            visiting.Remove(this);
        }
    }

    private void ValidateSetters(SetterBase[] setters, string location)
    {
        for (int i = 0; i < setters.Length; i++)
        {
            var setter = setters[i] ?? throw new InvalidOperationException(
                $"{location}[{i}] cannot be null.");

            ValidatePropertyOwner(setter.Property, $"{location}[{i}]");

            if (setter.Property.IsReadOnly)
            {
                throw new InvalidOperationException(
                    $"{location}[{i}] cannot set read-only property " +
                    $"'{setter.Property.OwnerType.FullName}.{setter.Property.Name}'.");
            }

            if (setter is Setter && setter.ThemeResolver == null &&
                !IsCompatibleValue(setter.Property.ValueType, setter.Value))
            {
                throw new InvalidOperationException(
                    $"{location}[{i}] provides a value incompatible with property " +
                    $"'{setter.Property.OwnerType.FullName}.{setter.Property.Name}' " +
                    $"({setter.Property.ValueType.FullName}).");
            }
        }
    }

    private void ValidatePropertyOwner(MewProperty property, string location)
    {
        if (!property.OwnerType.IsAssignableFrom(TargetType))
        {
            throw new InvalidOperationException(
                $"{location} uses property '{property.OwnerType.FullName}.{property.Name}', which cannot " +
                $"be applied to style target '{TargetType.FullName}'.");
        }
    }

    private static bool IsCompatibleValue(Type valueType, object? value)
        => value != null
            ? valueType.IsInstanceOfType(value)
            : !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) != null;
}
