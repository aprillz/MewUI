namespace Aprillz.MewUI.Controls;

public sealed partial class TemplateContext
{
    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two inputs and
    /// clears that binding and its input subscriptions on the next <see cref="Reset"/>. The binding
    /// is always OneWay and shows the default value of <typeparamref name="TProp"/> while any input
    /// is unavailable.
    /// </summary>
    public void Bind<TProp, T1, T2>(
        MewObject target,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        Func<T1, T2, TProp> combine)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetBinding(property, first, second, combine);
        RegisterPropertyBinding(target, property);
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from two inputs and
    /// clears that binding and its input subscriptions on the next <see cref="Reset"/>. The binding
    /// is always OneWay and shows <paramref name="fallbackValue"/> while any input is unavailable.
    /// </summary>
    public void Bind<TProp, T1, T2>(
        MewObject target,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        TProp fallbackValue,
        Func<T1, T2, TProp> combine)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetBinding(property, first, second, fallbackValue, combine);
        RegisterPropertyBinding(target, property);
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three inputs and
    /// clears that binding and its input subscriptions on the next <see cref="Reset"/>. The binding
    /// is always OneWay and shows the default value of <typeparamref name="TProp"/> while any input
    /// is unavailable.
    /// </summary>
    public void Bind<TProp, T1, T2, T3>(
        MewObject target,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        Func<T1, T2, T3, TProp> combine)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetBinding(property, first, second, third, combine);
        RegisterPropertyBinding(target, property);
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from three inputs and
    /// clears that binding and its input subscriptions on the next <see cref="Reset"/>. The binding
    /// is always OneWay and shows <paramref name="fallbackValue"/> while any input is unavailable.
    /// </summary>
    public void Bind<TProp, T1, T2, T3>(
        MewObject target,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        TProp fallbackValue,
        Func<T1, T2, T3, TProp> combine)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetBinding(property, first, second, third, fallbackValue, combine);
        RegisterPropertyBinding(target, property);
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four inputs and
    /// clears that binding and its input subscriptions on the next <see cref="Reset"/>. The binding
    /// is always OneWay and shows the default value of <typeparamref name="TProp"/> while any input
    /// is unavailable.
    /// </summary>
    public void Bind<TProp, T1, T2, T3, T4>(
        MewObject target,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        BindingInput<T4> fourth,
        Func<T1, T2, T3, T4, TProp> combine)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetBinding(property, first, second, third, fourth, combine);
        RegisterPropertyBinding(target, property);
    }

    /// <summary>
    /// Binds a property to a value that <paramref name="combine"/> computes from four inputs and
    /// clears that binding and its input subscriptions on the next <see cref="Reset"/>. The binding
    /// is always OneWay and shows <paramref name="fallbackValue"/> while any input is unavailable.
    /// </summary>
    public void Bind<TProp, T1, T2, T3, T4>(
        MewObject target,
        MewProperty<TProp> property,
        BindingInput<T1> first,
        BindingInput<T2> second,
        BindingInput<T3> third,
        BindingInput<T4> fourth,
        TProp fallbackValue,
        Func<T1, T2, T3, T4, TProp> combine)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetBinding(property, first, second, third, fourth, fallbackValue, combine);
        RegisterPropertyBinding(target, property);
    }
}
