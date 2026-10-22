namespace Aprillz.MewUI;

/// <summary>
/// Applies its setters to a single element while the observed property equals <see cref="Value"/>,
/// attached through <see cref="Controls.UIElement.Triggers"/>.
/// </summary>
/// <remarks>
/// When the value stops matching, the setters are removed and whatever the style system, inheritance
/// or defaults provide shows again. Independent of <see cref="Style"/>, so it works on elements that
/// are not controls.
/// </remarks>
public sealed class ElementTrigger
{
    /// <summary>Gets the property whose value is the condition.</summary>
    public required MewProperty Property { get; init; }

    /// <summary>Gets the value the condition property must equal for the trigger to match.</summary>
    public required object? Value { get; init; }

    /// <summary>
    /// Gets the setters applied while the trigger matches; <see cref="UnsetSetter"/> is not valid here.
    /// </summary>
    public required IReadOnlyList<SetterBase> Setters { get; init; }

    /// <summary>
    /// Creates a trigger that applies <paramref name="setters"/> while
    /// <paramref name="property"/> equals <paramref name="value"/>.
    /// </summary>
    public static ElementTrigger When<T>(MewProperty<T> property, T value, params SetterBase[] setters)
        => new() { Property = property, Value = value, Setters = setters };

    internal bool Matches(object? currentValue) => Equals(currentValue, Value);
}
