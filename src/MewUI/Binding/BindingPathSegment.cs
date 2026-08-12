using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
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

    void ValidateWrite(object endpoint, object? value);

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

    public void ValidateWrite(object endpoint, object? value)
        => throw new InvalidOperationException("A getter path segment is not writable.");

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

    public void ValidateWrite(object endpoint, object? value)
    {
    }

    public void Write(object endpoint, object? value)
        => ((ObservableValue<TValue>)endpoint).Value = (TValue)value!;
}

internal sealed class InpcBindingPathSegment<TSource, TValue>(
    Func<TSource, TValue> getter,
    Action<TSource, TValue>? setter,
    string propertyName) : IBindingPathSegment
    where TSource : class, INotifyPropertyChanged
{
    public bool IsObservable => true;

    public bool CanWrite => setter != null;

    public BindingPathSegmentValue Attach(object owner, BindingPathSubscription? subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var typedOwner = (TSource)owner;
        var value = getter(typedOwner);

        WeakEventManager.AddHandler<INotifyPropertyChanged, BindingPathSubscription>(
            InpcWeakEvents.PropertyChanged,
            typedOwner,
            subscription,
            propertyName,
            static value => value.OnChanged());

        return new BindingPathSegmentValue(value, typedOwner);
    }

    public object? Read(object endpoint) => getter((TSource)endpoint);

    public void Detach(object endpoint, BindingPathSubscription subscription)
    {
        WeakEventManager.RemoveHandler(
            InpcWeakEvents.PropertyChanged,
            (INotifyPropertyChanged)endpoint,
            subscription);
    }

    public void ValidateWrite(object endpoint, object? value)
    {
    }

    public void Write(object endpoint, object? value)
        => setter!((TSource)endpoint, (TValue)value!);
}

internal sealed class IndexedBindingPathSegment<TSource, TValue>(
    Func<TSource, TValue> getter,
    int index) : IBindingPathSegment
    where TSource : class
{
    public bool IsObservable => true;

    public bool CanWrite => false;

    public BindingPathSegmentValue Attach(object owner, BindingPathSubscription? subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        // A collection reports every change through one event, so re-reading the index is both
        // simpler and cheaper than deciding whether this index moved.
        if (owner is INotifyCollectionChanged collection)
        {
            WeakEventManager.AddHandler<INotifyCollectionChanged, BindingPathSubscription>(
                CollectionWeakEvents.CollectionChanged,
                collection,
                subscription,
                static (value, _, _) => value.OnChanged());
        }
        else if (owner is INotifyPropertyChanged notifier)
        {
            WeakEventManager.AddIndexerHandler<INotifyPropertyChanged, BindingPathSubscription>(
                InpcWeakEvents.PropertyChanged,
                notifier,
                subscription,
                static value => value.OnChanged());
        }

        return new BindingPathSegmentValue(Read(owner), owner);
    }

    public object? Read(object endpoint)
    {
        // An index that no longer exists makes the path unavailable rather than faulted. Range
        // check first so an emptied collection does not throw on every read.
        if (endpoint is ICollection collection
            && (collection.Count == 0 || (index >= 0 && index >= collection.Count)))
        {
            return null;
        }

        try
        {
            return getter((TSource)endpoint);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public void Detach(object endpoint, BindingPathSubscription subscription)
    {
        if (endpoint is INotifyCollectionChanged collection)
        {
            WeakEventManager.RemoveHandler(
                CollectionWeakEvents.CollectionChanged, collection, subscription);
        }
        else if (endpoint is INotifyPropertyChanged notifier)
        {
            WeakEventManager.RemoveHandler(
                InpcWeakEvents.PropertyChanged, notifier, subscription);
        }
    }

    public void ValidateWrite(object endpoint, object? value)
        => throw new InvalidOperationException("An indexed path segment is not writable.");

    public void Write(object endpoint, object? value)
        => throw new InvalidOperationException("An indexed path segment is not writable.");
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

    public void ValidateWrite(object endpoint, object? value)
        => ((TOwner)endpoint).PropertyStore.ValidateValueCandidate(_property, value);

    public void Write(object endpoint, object? value)
        => ((TOwner)endpoint).PropertyStore.SetLocalPrevalidated(_property, value);
}
