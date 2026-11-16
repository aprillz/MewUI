using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Controls;

public static partial class BindingExtensions
{
    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two inputs and
    /// returns the element for chaining. The binding is always <see cref="BindingMode.OneWay"/> and
    /// shows the default value of <typeparamref name="TProp"/> while any input is unavailable.
    /// </summary>
    public static TElement Bind<TElement, TProp, T1, T2>(
        this TElement element,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        Func<T1, T2, TProp> combine)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, first, second, combine);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two inputs and
    /// returns the element for chaining. The binding is always <see cref="BindingMode.OneWay"/> and
    /// shows <paramref name="fallbackValue"/> while any input is unavailable.
    /// </summary>
    public static TElement Bind<TElement, TProp, T1, T2>(
        this TElement element,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        TProp fallbackValue,
        Func<T1, T2, TProp> combine)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, first, second, fallbackValue, combine);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three inputs and
    /// returns the element for chaining. The binding is always <see cref="BindingMode.OneWay"/> and
    /// shows the default value of <typeparamref name="TProp"/> while any input is unavailable.
    /// </summary>
    public static TElement Bind<TElement, TProp, T1, T2, T3>(
        this TElement element,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        Func<T1, T2, T3, TProp> combine)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, first, second, third, combine);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three inputs and
    /// returns the element for chaining. The binding is always <see cref="BindingMode.OneWay"/> and
    /// shows <paramref name="fallbackValue"/> while any input is unavailable.
    /// </summary>
    public static TElement Bind<TElement, TProp, T1, T2, T3>(
        this TElement element,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        TProp fallbackValue,
        Func<T1, T2, T3, TProp> combine)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, first, second, third, fallbackValue, combine);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four inputs and
    /// returns the element for chaining. The binding is always <see cref="BindingMode.OneWay"/> and
    /// shows the default value of <typeparamref name="TProp"/> while any input is unavailable.
    /// </summary>
    public static TElement Bind<TElement, TProp, T1, T2, T3, T4>(
        this TElement element,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        BindingInput<T4> fourth,
        Func<T1, T2, T3, T4, TProp> combine)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, first, second, third, fourth, combine);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four inputs and
    /// returns the element for chaining. The binding is always <see cref="BindingMode.OneWay"/> and
    /// shows <paramref name="fallbackValue"/> while any input is unavailable.
    /// </summary>
    public static TElement Bind<TElement, TProp, T1, T2, T3, T4>(
        this TElement element,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        BindingInput<T4> fourth,
        TProp fallbackValue,
        Func<T1, T2, T3, T4, TProp> combine)
        where TElement : MewObject
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(property, first, second, third, fourth, fallbackValue, combine);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two notifying
    /// properties of <paramref name="source"/> and returns the element for chaining. The binding is
    /// always <see cref="BindingMode.OneWay"/> and shows the default value of
    /// <typeparamref name="TProp"/> while any input is unavailable.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public static TElement Bind<TElement, TProp, TSource, T1, T2>(
        this TElement element,
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, T1> first,
        Func<TSource, T2> second,
        Func<T1, T2, TProp> combine,
        [CallerArgumentExpression(nameof(first))] string? firstExpression = null,
        [CallerArgumentExpression(nameof(second))] string? secondExpression = null)
        where TElement : MewObject
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(
            property,
            source,
            first,
            second,
            combine,
            firstExpression: firstExpression,
            secondExpression: secondExpression);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two notifying
    /// properties of <paramref name="source"/> and returns the element for chaining. The binding is
    /// always <see cref="BindingMode.OneWay"/> and shows <paramref name="fallbackValue"/> while any
    /// input is unavailable.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public static TElement Bind<TElement, TProp, TSource, T1, T2>(
        this TElement element,
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, T1> first,
        Func<TSource, T2> second,
        TProp fallbackValue,
        Func<T1, T2, TProp> combine,
        [CallerArgumentExpression(nameof(first))] string? firstExpression = null,
        [CallerArgumentExpression(nameof(second))] string? secondExpression = null)
        where TElement : MewObject
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(
            property,
            source,
            first,
            second,
            fallbackValue,
            combine,
            firstExpression: firstExpression,
            secondExpression: secondExpression);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three notifying
    /// properties of <paramref name="source"/> and returns the element for chaining. The binding is
    /// always <see cref="BindingMode.OneWay"/> and shows the default value of
    /// <typeparamref name="TProp"/> while any input is unavailable.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public static TElement Bind<TElement, TProp, TSource, T1, T2, T3>(
        this TElement element,
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, T1> first,
        Func<TSource, T2> second,
        Func<TSource, T3> third,
        Func<T1, T2, T3, TProp> combine,
        [CallerArgumentExpression(nameof(first))] string? firstExpression = null,
        [CallerArgumentExpression(nameof(second))] string? secondExpression = null,
        [CallerArgumentExpression(nameof(third))] string? thirdExpression = null)
        where TElement : MewObject
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(
            property,
            source,
            first,
            second,
            third,
            combine,
            firstExpression: firstExpression,
            secondExpression: secondExpression,
            thirdExpression: thirdExpression);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three notifying
    /// properties of <paramref name="source"/> and returns the element for chaining. The binding is
    /// always <see cref="BindingMode.OneWay"/> and shows <paramref name="fallbackValue"/> while any
    /// input is unavailable.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public static TElement Bind<TElement, TProp, TSource, T1, T2, T3>(
        this TElement element,
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, T1> first,
        Func<TSource, T2> second,
        Func<TSource, T3> third,
        TProp fallbackValue,
        Func<T1, T2, T3, TProp> combine,
        [CallerArgumentExpression(nameof(first))] string? firstExpression = null,
        [CallerArgumentExpression(nameof(second))] string? secondExpression = null,
        [CallerArgumentExpression(nameof(third))] string? thirdExpression = null)
        where TElement : MewObject
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(
            property,
            source,
            first,
            second,
            third,
            fallbackValue,
            combine,
            firstExpression: firstExpression,
            secondExpression: secondExpression,
            thirdExpression: thirdExpression);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four notifying
    /// properties of <paramref name="source"/> and returns the element for chaining. The binding is
    /// always <see cref="BindingMode.OneWay"/> and shows the default value of
    /// <typeparamref name="TProp"/> while any input is unavailable.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public static TElement Bind<TElement, TProp, TSource, T1, T2, T3, T4>(
        this TElement element,
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, T1> first,
        Func<TSource, T2> second,
        Func<TSource, T3> third,
        Func<TSource, T4> fourth,
        Func<T1, T2, T3, T4, TProp> combine,
        [CallerArgumentExpression(nameof(first))] string? firstExpression = null,
        [CallerArgumentExpression(nameof(second))] string? secondExpression = null,
        [CallerArgumentExpression(nameof(third))] string? thirdExpression = null,
        [CallerArgumentExpression(nameof(fourth))] string? fourthExpression = null)
        where TElement : MewObject
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(
            property,
            source,
            first,
            second,
            third,
            fourth,
            combine,
            firstExpression: firstExpression,
            secondExpression: secondExpression,
            thirdExpression: thirdExpression,
            fourthExpression: fourthExpression);
        return element;
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four notifying
    /// properties of <paramref name="source"/> and returns the element for chaining. The binding is
    /// always <see cref="BindingMode.OneWay"/> and shows <paramref name="fallbackValue"/> while any
    /// input is unavailable.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public static TElement Bind<TElement, TProp, TSource, T1, T2, T3, T4>(
        this TElement element,
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, T1> first,
        Func<TSource, T2> second,
        Func<TSource, T3> third,
        Func<TSource, T4> fourth,
        TProp fallbackValue,
        Func<T1, T2, T3, T4, TProp> combine,
        [CallerArgumentExpression(nameof(first))] string? firstExpression = null,
        [CallerArgumentExpression(nameof(second))] string? secondExpression = null,
        [CallerArgumentExpression(nameof(third))] string? thirdExpression = null,
        [CallerArgumentExpression(nameof(fourth))] string? fourthExpression = null)
        where TElement : MewObject
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetBinding(
            property,
            source,
            first,
            second,
            third,
            fourth,
            fallbackValue,
            combine,
            firstExpression: firstExpression,
            secondExpression: secondExpression,
            thirdExpression: thirdExpression,
            fourthExpression: fourthExpression);
        return element;
    }
}
