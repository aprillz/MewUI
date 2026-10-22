namespace Aprillz.MewUI;

/// <summary>
/// Base class for property value setters used in <see cref="Style"/> definitions.
/// </summary>
public abstract class SetterBase
{
    /// <summary>Gets the target property.</summary>
    public MewProperty Property { get; }

    /// <summary>Gets the boxed static value, or null if this setter uses a theme resolver.</summary>
    public object? Value { get; }

    /// <summary>Gets the theme resolver function, or null if this setter uses a static value.</summary>
    internal Func<Theme, object>? ThemeResolver { get; }

    /// <summary>Resolves the effective value using the current theme.</summary>
    internal object ResolveValue(Theme theme)
        => ThemeResolver != null ? ThemeResolver(theme) : Value!;

    private protected SetterBase(MewProperty property, object value)
    {
        Property = property;
        Value = value;
    }

    private protected SetterBase(MewProperty property, Func<Theme, object> themeResolver)
    {
        Property = property;
        ThemeResolver = themeResolver;
    }

    private protected SetterBase(MewProperty property)
    {
        Property = property;
    }
}

/// <summary>
/// Sets a static or theme-resolved value on a property of the styled control.
/// </summary>
public sealed class Setter : SetterBase
{
    private Setter(MewProperty property, object value) : base(property, value) { }
    private Setter(MewProperty property, Func<Theme, object> themeResolver) : base(property, themeResolver) { }

    /// <summary>
    /// Creates a type-safe setter with a static value.
    /// </summary>
    public static Setter Create<T>(MewProperty<T> property, T value)
        => new(property, value!);

    /// <summary>
    /// Creates a type-safe setter that resolves its value from the current theme.
    /// </summary>
    /// <remarks>
    /// Use static lambdas to avoid allocations: <c>(Theme t) => t.Palette.ButtonFace</c>.
    /// </remarks>
    public static Setter Create<T>(MewProperty<T> property, Func<Theme, T> resolve)
        => new(property, t => resolve(t)!);

    /// <summary>
    /// Creates a setter that unsets <paramref name="property"/> within a derived (<see cref="Style.BasedOn"/>) style,
    /// reverting it to the inherited value, or the type default when nothing is inherited.
    /// </summary>
    /// <remarks>
    /// The result is as if no style in the chain set the property. Higher-priority sources (a local value,
    /// an animation, or a matching trigger) still win.
    /// </remarks>
    public static UnsetSetter Unset(MewProperty property)
        => new(property);
}

/// <summary>
/// Unsets a property within a derived <see cref="Style"/> (<see cref="Style.BasedOn"/>), reverting it to the inherited
/// value (or the type default) instead of a value the base style would provide.
/// </summary>
public sealed class UnsetSetter : SetterBase
{
    internal UnsetSetter(MewProperty property) : base(property) { }
}
