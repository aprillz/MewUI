using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Controls;

public abstract partial class MewObject
{
    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two inputs. The
    /// binding is always OneWay and shows the default value of <typeparamref name="TProp"/> while
    /// any input is unavailable. Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TProp, T1, T2>(
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        Func<T1, T2, TProp> combine)
        => SetMultiBinding(
            property,
            BindingInput.Combine(first, second, combine),
            default!);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two inputs. The
    /// binding is always OneWay and shows <paramref name="fallbackValue"/> while any input is
    /// unavailable. Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TProp, T1, T2>(
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        TProp fallbackValue,
        Func<T1, T2, TProp> combine)
        => SetMultiBinding(
            property,
            BindingInput.Combine(first, second, combine),
            fallbackValue);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three inputs. The
    /// binding is always OneWay and shows the default value of <typeparamref name="TProp"/> while
    /// any input is unavailable. Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TProp, T1, T2, T3>(
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        Func<T1, T2, T3, TProp> combine)
        => SetMultiBinding(
            property,
            BindingInput.Combine(first, second, third, combine),
            default!);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three inputs. The
    /// binding is always OneWay and shows <paramref name="fallbackValue"/> while any input is
    /// unavailable. Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TProp, T1, T2, T3>(
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        TProp fallbackValue,
        Func<T1, T2, T3, TProp> combine)
        => SetMultiBinding(
            property,
            BindingInput.Combine(first, second, third, combine),
            fallbackValue);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four inputs. The
    /// binding is always OneWay and shows the default value of <typeparamref name="TProp"/> while
    /// any input is unavailable. Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TProp, T1, T2, T3, T4>(
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        BindingInput<T4> fourth,
        Func<T1, T2, T3, T4, TProp> combine)
        => SetMultiBinding(
            property,
            BindingInput.Combine(first, second, third, fourth, combine),
            default!);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four inputs. The
    /// binding is always OneWay and shows <paramref name="fallbackValue"/> while any input is
    /// unavailable. Replaces any existing binding for the same property.
    /// </summary>
    public void SetBinding<TProp, T1, T2, T3, T4>(
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        BindingInput<T4> fourth,
        TProp fallbackValue,
        Func<T1, T2, T3, T4, TProp> combine)
        => SetMultiBinding(
            property,
            BindingInput.Combine(first, second, third, fourth, combine),
            fallbackValue);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two notifying
    /// properties of <paramref name="source"/>. The binding is always OneWay and shows the default
    /// value of <typeparamref name="TProp"/> while any input is unavailable. Replaces any existing
    /// binding for the same property.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public void SetBinding<TProp, TSource, T1, T2>(
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, T1> first,
        Func<TSource, T2> second,
        Func<T1, T2, TProp> combine,
        [CallerArgumentExpression(nameof(first))] string? firstExpression = null,
        [CallerArgumentExpression(nameof(second))] string? secondExpression = null)
        where TSource : class, INotifyPropertyChanged
        => SetMultiBinding(
            property,
            BindingInput.Combine(
                BindingInput.From(source, first, firstExpression),
                BindingInput.From(source, second, secondExpression),
                combine),
            default!);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two notifying
    /// properties of <paramref name="source"/>. The binding is always OneWay and shows
    /// <paramref name="fallbackValue"/> while any input is unavailable. Replaces any existing
    /// binding for the same property.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public void SetBinding<TProp, TSource, T1, T2>(
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, T1> first,
        Func<TSource, T2> second,
        TProp fallbackValue,
        Func<T1, T2, TProp> combine,
        [CallerArgumentExpression(nameof(first))] string? firstExpression = null,
        [CallerArgumentExpression(nameof(second))] string? secondExpression = null)
        where TSource : class, INotifyPropertyChanged
        => SetMultiBinding(
            property,
            BindingInput.Combine(
                BindingInput.From(source, first, firstExpression),
                BindingInput.From(source, second, secondExpression),
                combine),
            fallbackValue);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three notifying
    /// properties of <paramref name="source"/>. The binding is always OneWay and shows the default
    /// value of <typeparamref name="TProp"/> while any input is unavailable. Replaces any existing
    /// binding for the same property.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public void SetBinding<TProp, TSource, T1, T2, T3>(
        MewProperty<TProp> property,
        TSource source,
        Func<TSource, T1> first,
        Func<TSource, T2> second,
        Func<TSource, T3> third,
        Func<T1, T2, T3, TProp> combine,
        [CallerArgumentExpression(nameof(first))] string? firstExpression = null,
        [CallerArgumentExpression(nameof(second))] string? secondExpression = null,
        [CallerArgumentExpression(nameof(third))] string? thirdExpression = null)
        where TSource : class, INotifyPropertyChanged
        => SetMultiBinding(
            property,
            BindingInput.Combine(
                BindingInput.From(source, first, firstExpression),
                BindingInput.From(source, second, secondExpression),
                BindingInput.From(source, third, thirdExpression),
                combine),
            default!);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three notifying
    /// properties of <paramref name="source"/>. The binding is always OneWay and shows
    /// <paramref name="fallbackValue"/> while any input is unavailable. Replaces any existing
    /// binding for the same property.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public void SetBinding<TProp, TSource, T1, T2, T3>(
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
        where TSource : class, INotifyPropertyChanged
        => SetMultiBinding(
            property,
            BindingInput.Combine(
                BindingInput.From(source, first, firstExpression),
                BindingInput.From(source, second, secondExpression),
                BindingInput.From(source, third, thirdExpression),
                combine),
            fallbackValue);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four notifying
    /// properties of <paramref name="source"/>. The binding is always OneWay and shows the default
    /// value of <typeparamref name="TProp"/> while any input is unavailable. Replaces any existing
    /// binding for the same property.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public void SetBinding<TProp, TSource, T1, T2, T3, T4>(
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
        where TSource : class, INotifyPropertyChanged
        => SetMultiBinding(
            property,
            BindingInput.Combine(
                BindingInput.From(source, first, firstExpression),
                BindingInput.From(source, second, secondExpression),
                BindingInput.From(source, third, thirdExpression),
                BindingInput.From(source, fourth, fourthExpression),
                combine),
            default!);

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four notifying
    /// properties of <paramref name="source"/>. The binding is always OneWay and shows
    /// <paramref name="fallbackValue"/> while any input is unavailable. Replaces any existing
    /// binding for the same property.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a getter is not a single member access such as <c>x =&gt; x.Name</c> and the
    /// binding path generator did not rewrite it.
    /// </exception>
    public void SetBinding<TProp, TSource, T1, T2, T3, T4>(
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
        where TSource : class, INotifyPropertyChanged
        => SetMultiBinding(
            property,
            BindingInput.Combine(
                BindingInput.From(source, first, firstExpression),
                BindingInput.From(source, second, secondExpression),
                BindingInput.From(source, third, thirdExpression),
                BindingInput.From(source, fourth, fourthExpression),
                combine),
            fallbackValue);

    private void SetMultiBinding<TProp>(
        MewProperty<TProp> property,
        MultiBindingSource<TProp> source,
        TProp fallbackValue)
    {
        ArgumentNullException.ThrowIfNull(property);
        ThrowIfReadOnly(property);

        // Inputs attach before the old binding is removed, so a failing input leaves it in place.
        var propertyBinding = new MultiPropertyBinding<TProp>(
            this, property, source, fallbackValue);
        PreparePropertyBinding(property.Id);
        ActivatePropertyBinding(property.Id, propertyBinding);
    }
}
