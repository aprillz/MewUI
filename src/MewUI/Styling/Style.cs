namespace Aprillz.MewUI.Styling;

/// <summary>
/// Defines default and state-conditional property values for a control type.
/// Created by Theme with palette colors; immutable after construction.
/// </summary>
public sealed class Style
{
    /// <summary>
    /// Gets the default theme style for the specified control type.
    /// Equivalent to <c>Theme.GetStyle(type)</c> but without requiring a Theme instance.
    /// </summary>
    public static Style? ForType(Type controlType)
        => Theme.GetStyleStatic(controlType);

    /// <summary>
    /// Gets the default theme style for the specified control type.
    /// </summary>
    public static Style? ForType<T>() where T : class
        => ForType(typeof(T));

    /// <summary>
    /// Gets the target control type this style applies to.
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// Gets the parent style to inherit from. Properties not defined in this style
    /// fall through to <see cref="BasedOn"/>.
    /// </summary>
    public Style? BasedOn { get; init; }

    /// <summary>
    /// Gets the base setters applied regardless of visual state (lowest priority within this style).
    /// </summary>
    public IReadOnlyList<SetterBase> Setters { get; init; } = [];

    /// <summary>
    /// Gets the state-conditional triggers. Evaluated per-property: highest
    /// <see cref="StateTrigger.Specificity"/> wins; ties broken by later declaration.
    /// </summary>
    public IReadOnlyList<StateTrigger> Triggers { get; init; } = [];

    /// <summary>
    /// Gets the transitions that animate property changes from this style's setters and triggers.
    /// Resolved via <see cref="FindTransition"/> which walks the BasedOn chain.
    /// </summary>
    public IReadOnlyList<Transition> Transitions { get; init; } = [];

    public Style(Type targetType)
    {
        TargetType = targetType;
    }

    /// <summary>
    /// Finds the transition for the given property, walking the BasedOn chain.
    /// </summary>
    public Transition? FindTransition(int propertyId)
    {
        for (int i = 0; i < Transitions.Count; i++)
        {
            if (Transitions[i].Property.Id == propertyId)
                return Transitions[i];
        }

        return BasedOn?.FindTransition(propertyId);
    }
}
