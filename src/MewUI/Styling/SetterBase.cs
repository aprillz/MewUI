namespace Aprillz.MewUI.Styling;

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
}

/// <summary>
/// Sets a property value on the control itself.
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
    /// Use static lambdas to avoid allocations: <c>(Theme t) => t.Palette.ButtonFace</c>.
    /// </summary>
    public static Setter Create<T>(MewProperty<T> property, Func<Theme, T> resolve)
        => new(property, t => resolve(t)!);
}

/// <summary>
/// Sets a property value on a named child part (resolved via <c>RegisterPart</c>/<c>GetPart</c>).
/// </summary>
public sealed class TargetSetter : SetterBase
{
    /// <summary>Gets the name of the target part.</summary>
    public string TargetName { get; }

    private TargetSetter(string targetName, MewProperty property, object value) : base(property, value)
    {
        TargetName = targetName;
    }

    private TargetSetter(string targetName, MewProperty property, Func<Theme, object> themeResolver) : base(property, themeResolver)
    {
        TargetName = targetName;
    }

    /// <summary>
    /// Creates a type-safe target setter with a static value.
    /// </summary>
    public static TargetSetter Create<T>(string targetName, MewProperty<T> property, T value)
        => new(targetName, property, value!);

    /// <summary>
    /// Creates a type-safe target setter that resolves its value from the current theme.
    /// </summary>
    public static TargetSetter Create<T>(string targetName, MewProperty<T> property, Func<Theme, T> resolve)
        => new(targetName, property, t => resolve(t)!);
}
