using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

internal readonly record struct BindingPathSegmentValue(object? Value, object? Endpoint);

internal interface IBindingPathSegment
{
    bool IsObservable { get; }

    bool CanWrite { get; }

    BindingPathSegmentValue Attach(object owner, BindingPathSubscription? subscription);

    object? Read(object endpoint);

    void Detach(object endpoint, BindingPathSubscription subscription);

    void Write(object endpoint, object? value);
}

internal sealed class GetterBindingPathSegment<TSource, TValue>(
    Func<TSource, TValue> getter) : IBindingPathSegment
{
    public bool IsObservable => false;

    public bool CanWrite => false;

    public BindingPathSegmentValue Attach(object owner, BindingPathSubscription? subscription)
        => new(getter((TSource)owner), null);

    public object? Read(object endpoint)
        => throw new InvalidOperationException("A getter path segment is not observable.");

    public void Detach(object endpoint, BindingPathSubscription subscription)
    {
    }

    public void Write(object endpoint, object? value)
        => throw new InvalidOperationException("A getter path segment is not writable.");
}

internal sealed class ObservableBindingPathSegment<TSource, TValue>(
    Func<TSource, ObservableValue<TValue>> selector) : IBindingPathSegment
{
    public bool IsObservable => true;

    public bool CanWrite => true;

    public BindingPathSegmentValue Attach(object owner, BindingPathSubscription? subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var observable = selector((TSource)owner)
            ?? throw new InvalidOperationException(
                "A BindingPath observable selector returned null.");
        var value = observable.Value;

        WeakEventManager.AddHandler(
            ObservableValueWeakEvents<TValue>.Changed,
            observable,
            subscription,
            static value => value.OnChanged());

        return new BindingPathSegmentValue(value, observable);
    }

    public object? Read(object endpoint) => ((ObservableValue<TValue>)endpoint).Value;

    public void Detach(object endpoint, BindingPathSubscription subscription)
    {
        WeakEventManager.RemoveHandler(
            ObservableValueWeakEvents<TValue>.Changed,
            (ObservableValue<TValue>)endpoint,
            subscription);
    }

    public void Write(object endpoint, object? value)
        => ((ObservableValue<TValue>)endpoint).Value = (TValue)value!;
}

internal sealed class MewPropertyBindingPathSegment<TOwner, TValue> : IBindingPathSegment
    where TOwner : MewObject
{
    private readonly MewProperty<TValue> _property;
    private readonly WeakEventKey<MewObject, Action> _changedEvent;

    public MewPropertyBindingPathSegment(MewProperty<TValue> property)
    {
        _property = property;
        _changedEvent = new WeakEventKey<MewObject, Action>(
            (owner, handler) => owner.AddPropertyBindingCallback(property.Id, handler),
            (owner, handler) => owner.RemovePropertyBindingCallback(property.Id, handler),
            requireStaticAccessors: false);
    }

    public bool IsObservable => true;

    public bool CanWrite => !_property.IsReadOnly;

    public BindingPathSegmentValue Attach(object owner, BindingPathSubscription? subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var typedOwner = (TOwner)owner;
        var value = typedOwner.GetBindingValue(_property);
        WeakEventManager.AddHandler(
            _changedEvent,
            typedOwner,
            subscription,
            static value => value.OnChanged());

        return new BindingPathSegmentValue(value, typedOwner);
    }

    public object? Read(object endpoint)
        => ((TOwner)endpoint).GetBindingValue(_property);

    public void Detach(object endpoint, BindingPathSubscription subscription)
        => WeakEventManager.RemoveHandler(_changedEvent, (TOwner)endpoint, subscription);

    public void Write(object endpoint, object? value)
        => ((TOwner)endpoint).SetBindingValue(_property, (TValue)value!);
}
