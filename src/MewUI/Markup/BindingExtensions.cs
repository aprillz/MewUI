namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent binding extension methods that preserve the concrete element type for chaining.
/// </summary>
public static class BindingExtensions
{
    /// <summary>
    /// Binds a <see cref="MewProperty{T}"/> to an <see cref="ObservableValue{T}"/>
    /// and returns the element for fluent chaining.
    /// </summary>
    public static TElement Bind<TElement, T>(this TElement element,
        MewProperty<T> property, ObservableValue<T> source,
        BindingMode? mode = null)
        where TElement : MewObject
    {
        element.SetBinding(property, source, mode);
        return element;
    }

    /// <summary>
    /// Binds a <see cref="MewProperty{TProp}"/> to an <see cref="ObservableValue{TSource}"/>
    /// with type conversion and returns the element for fluent chaining.
    /// </summary>
    public static TElement Bind<TElement, TProp, TSource>(this TElement element,
        MewProperty<TProp> property,
        ObservableValue<TSource> source,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack = null,
        BindingMode? mode = null)
        where TElement : MewObject
    {
        element.SetBinding(property, source, convert, convertBack, mode);
        return element;
    }
}
