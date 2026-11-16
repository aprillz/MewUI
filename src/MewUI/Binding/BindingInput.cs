using System.ComponentModel;
using System.Runtime.CompilerServices;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Creates the inputs of a binding whose value is computed from several sources.
/// </summary>
public static class BindingInput
{
    /// <summary>
    /// Creates an input that follows <paramref name="path"/> from <paramref name="source"/>.
    /// </summary>
    public static BindingInput<TValue> From<TRoot, TValue>(
        TRoot source,
        BindingPath<TRoot, TValue> path)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);
        return new BindingInput<TValue>(() => path.Attach(source));
    }

    /// <summary>
    /// Creates an input that follows an observable value.
    /// </summary>
    public static BindingInput<TValue> From<TValue>(ObservableValue<TValue> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Func<ObservableValue<TValue>, ObservableValue<TValue>> self = static value => value;
        return From(source, BindingPath.From<ObservableValue<TValue>>().Then(self));
    }

    /// <summary>
    /// Creates an input that follows a property of another object.
    /// </summary>
    public static BindingInput<TValue> From<TValue>(MewObject source, MewProperty<TValue> property)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(property);
        return From(source, BindingPath.From<MewObject>().Then(property));
    }

    /// <summary>
    /// Creates an input that follows a notifying property of <paramref name="source"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="getter"/> is not a single member access such as
    /// <c>x =&gt; x.Name</c> and the binding path generator did not rewrite it.
    /// </exception>
    public static BindingInput<TValue> From<TSource, TValue>(
        TSource source,
        Func<TSource, TValue> getter,
        [CallerArgumentExpression(nameof(getter))] string? getterExpression = null)
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(getter);

        var path = BindingPath.From<TSource>()
            .ThenNotifying(getter, getterExpression: getterExpression);
        return From(source, path);
    }

    internal static MultiBindingSource<TResult> Combine<T1, T2, TResult>(
        BindingInput<T1> first,
        BindingInput<T2> second,
        Func<T1, T2, TResult> combine)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(combine);

        return new MultiBindingSource<TResult>(
            new IBindingInput[] { first, second },
            observers => combine(
                ValueOf<T1>(observers[0]),
                ValueOf<T2>(observers[1])));
    }

    internal static MultiBindingSource<TResult> Combine<T1, T2, T3, TResult>(
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        Func<T1, T2, T3, TResult> combine)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        ArgumentNullException.ThrowIfNull(combine);

        return new MultiBindingSource<TResult>(
            new IBindingInput[] { first, second, third },
            observers => combine(
                ValueOf<T1>(observers[0]),
                ValueOf<T2>(observers[1]),
                ValueOf<T3>(observers[2])));
    }

    internal static MultiBindingSource<TResult> Combine<T1, T2, T3, T4, TResult>(
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        BindingInput<T4> fourth,
        Func<T1, T2, T3, T4, TResult> combine)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        ArgumentNullException.ThrowIfNull(fourth);
        ArgumentNullException.ThrowIfNull(combine);

        return new MultiBindingSource<TResult>(
            new IBindingInput[] { first, second, third, fourth },
            observers => combine(
                ValueOf<T1>(observers[0]),
                ValueOf<T2>(observers[1]),
                ValueOf<T3>(observers[2]),
                ValueOf<T4>(observers[3])));
    }

    private static TValue ValueOf<TValue>(IBindingValueObserver observer)
        => ((IBindingValueObserver<TValue>)observer).CurrentValue;
}

/// <summary>
/// One source of a binding whose value is computed from several sources, created by
/// <see cref="BindingInput.From{TRoot, TValue}(TRoot, BindingPath{TRoot, TValue})"/> and its
/// overloads.
/// </summary>
/// <remarks>
/// An input keeps its source alive and can be bound any number of times; each binding observes the
/// source on its own.
/// </remarks>
public sealed class BindingInput<TValue> : IBindingInput
{
    private readonly Func<IBindingValueObserver<TValue>> _attach;

    internal BindingInput(Func<IBindingValueObserver<TValue>> attach)
    {
        _attach = attach;
    }

    IBindingValueObserver IBindingInput.Attach() => _attach();
}

internal interface IBindingInput
{
    IBindingValueObserver Attach();
}

internal sealed class MultiBindingSource<TValue>
{
    private readonly IBindingInput[] _inputs;
    private readonly Func<IBindingValueObserver[], TValue> _combine;

    internal MultiBindingSource(
        IBindingInput[] inputs,
        Func<IBindingValueObserver[], TValue> combine)
    {
        _inputs = inputs;
        _combine = combine;
    }

    internal IBindingValueObserver[] Attach()
    {
        var observers = new IBindingValueObserver[_inputs.Length];
        int attached = 0;
        try
        {
            for (; attached < _inputs.Length; attached++)
            {
                observers[attached] = _inputs[attached].Attach();
            }
        }
        catch
        {
            for (int index = 0; index < attached; index++)
            {
                observers[index].Dispose();
            }

            throw;
        }

        return observers;
    }

    internal TValue Combine(IBindingValueObserver[] observers) => _combine(observers);
}
