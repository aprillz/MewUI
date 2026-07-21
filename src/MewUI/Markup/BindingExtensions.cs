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
    /// <typeparam name="TElement">Target object type.</typeparam>
    /// <typeparam name="T">Property value type.</typeparam>
    /// <param name="element">Target object.</param>
    /// <param name="property">Target property.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="mode">Optional binding mode.</param>
    /// <returns>The target object for chaining.</returns>
    public static TElement Bind<TElement, T>(this TElement element,
        MewProperty<T> property, ObservableValue<T> source,
        BindingMode? mode = null)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, source, mode);
        return element;
    }

    /// <summary>
    /// Binds a <see cref="MewProperty{T}"/> to another <see cref="MewObject"/>'s <see cref="MewProperty{T}"/>
    /// and returns the element for fluent chaining.
    /// </summary>
    /// <typeparam name="TElement">Target object type.</typeparam>
    /// <typeparam name="T">Property value type.</typeparam>
    /// <param name="element">Target object.</param>
    /// <param name="property">Target property.</param>
    /// <param name="source">Source object.</param>
    /// <param name="sourceProperty">Source property.</param>
    /// <returns>The target object for chaining.</returns>
    public static TElement Bind<TElement, T>(this TElement element,
        MewProperty<T> property, MewObject source, MewProperty<T> sourceProperty)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, source, sourceProperty);
        return element;
    }

    /// <summary>
    /// Binds a <see cref="MewProperty{TProp}"/> to an <see cref="ObservableValue{TSource}"/>
    /// with type conversion and returns the element for fluent chaining.
    /// </summary>
    /// <typeparam name="TElement">Target object type.</typeparam>
    /// <typeparam name="TProp">Target property value type.</typeparam>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="element">Target object.</param>
    /// <param name="property">Target property.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-target converter.</param>
    /// <param name="convertBack">Optional target-to-source converter.</param>
    /// <param name="mode">Optional binding mode.</param>
    /// <returns>The target object for chaining.</returns>
    public static TElement Bind<TElement, TProp, TSource>(this TElement element,
        MewProperty<TProp> property,
        ObservableValue<TSource> source,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack = null,
        BindingMode? mode = null)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, source, convert, convertBack, mode);
        return element;
    }

    /// <summary>
    /// Binds a <see cref="MewProperty{TProp}"/> to another <see cref="MewObject"/>'s <see cref="MewProperty{TSource}"/>
    /// with type conversion and returns the element for fluent chaining.
    /// </summary>
    /// <typeparam name="TElement">Target object type.</typeparam>
    /// <typeparam name="TProp">Target property value type.</typeparam>
    /// <typeparam name="TSource">Source property value type.</typeparam>
    /// <param name="element">Target object.</param>
    /// <param name="property">Target property.</param>
    /// <param name="source">Source object.</param>
    /// <param name="sourceProperty">Source property.</param>
    /// <param name="convert">Source-to-target converter.</param>
    /// <param name="convertBack">Optional target-to-source converter.</param>
    /// <param name="mode">Optional binding mode.</param>
    /// <returns>The target object for chaining.</returns>
    public static TElement Bind<TElement, TProp, TSource>(this TElement element,
        MewProperty<TProp> property,
        MewObject source, MewProperty<TSource> sourceProperty,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack = null,
        BindingMode? mode = null)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, source, sourceProperty, convert, convertBack, mode);
        return element;
    }

    /// <summary>
    /// Binds a property to a reusable delegate-based path and returns the target for chaining.
    /// </summary>
    public static TElement Bind<TElement, TRoot, T>(
        this TElement element,
        MewProperty<T> property,
        TRoot source,
        BindingPath<TRoot, T> path,
        BindingMode? mode = null,
        T fallbackValue = default!)
        where TElement : MewObject
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, source, path, mode, fallbackValue);
        return element;
    }

    /// <summary>
    /// Binds a property to a reusable delegate-based path with type conversion and returns the
    /// target for chaining.
    /// </summary>
    public static TElement Bind<TElement, TProp, TRoot, TSource>(
        this TElement element,
        MewProperty<TProp> property,
        TRoot source,
        BindingPath<TRoot, TSource> path,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack = null,
        BindingMode? mode = null,
        TProp fallbackValue = default!)
        where TElement : MewObject
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, source, path, convert, convertBack, mode, fallbackValue);
        return element;
    }
}
