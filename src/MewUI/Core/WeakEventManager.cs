using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI;

internal sealed class WeakEventKey<TSource, THandler>
    where TSource : class
    where THandler : Delegate
{
    private readonly Action<TSource, THandler> _addHandler;
    private readonly Action<TSource, THandler> _removeHandler;

    internal WeakEventKey(
        Action<TSource, THandler> addHandler,
        Action<TSource, THandler> removeHandler)
        : this(addHandler, removeHandler, requireStaticAccessors: true)
    {
    }

    internal WeakEventKey(
        Action<TSource, THandler> addHandler,
        Action<TSource, THandler> removeHandler,
        bool requireStaticAccessors)
    {
        ArgumentNullException.ThrowIfNull(addHandler);
        ArgumentNullException.ThrowIfNull(removeHandler);

        if (requireStaticAccessors)
        {
            ThrowIfCaptured(addHandler, nameof(addHandler));
            ThrowIfCaptured(removeHandler, nameof(removeHandler));
        }

        _addHandler = addHandler;
        _removeHandler = removeHandler;
    }

    internal void AddHandler(TSource source, THandler handler) => _addHandler(source, handler);

    internal void RemoveHandler(TSource source, THandler handler) => _removeHandler(source, handler);

    private static void ThrowIfCaptured(Delegate handler, string parameterName)
    {
        if (WeakEventDelegate.CapturesState(handler))
        {
            throw new ArgumentException(
                "Weak event accessors must be static and use the supplied source argument.",
                parameterName);
        }
    }

}

internal static class WeakEventManager
{
    private static readonly ConditionalWeakTable<object, SourceBucket> Sources = new();

    internal static void AddHandler<TSource, TTarget>(
        WeakEventKey<TSource, Action> eventKey,
        TSource source,
        TTarget target,
        Action<TTarget> invoke)
        where TSource : class
        where TTarget : class
    {
        Validate(eventKey, source, target, invoke);
        Add(source, new ActionRegistration<TSource, TTarget>(eventKey, source, target, invoke));
    }

    internal static void AddHandler<TSource, TTarget>(
        WeakEventKey<TSource, NotifyCollectionChangedEventHandler> eventKey,
        TSource source,
        TTarget target,
        Action<TTarget, object?, NotifyCollectionChangedEventArgs> invoke)
        where TSource : class
        where TTarget : class
    {
        Validate(eventKey, source, target, invoke);
        Add(source, new CollectionChangedRegistration<TSource, TTarget>(eventKey, source, target, invoke));
    }

    internal static void AddHandler<TSource, TTarget>(
        WeakEventKey<TSource, PropertyChangedEventHandler> eventKey,
        TSource source,
        TTarget target,
        string propertyName,
        Action<TTarget> invoke)
        where TSource : class
        where TTarget : class
    {
        Validate(eventKey, source, target, invoke);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        Add(source, new PropertyChangedRegistration<TSource, TTarget>(
            eventKey, source, target, propertyName, matchPrefix: false, invoke));
    }

    /// <summary>
    /// Subscribes to indexer change notifications, which name the property <c>Item[]</c> for every
    /// indexer or <c>Item[0]</c> for one.
    /// </summary>
    internal static void AddIndexerHandler<TSource, TTarget>(
        WeakEventKey<TSource, PropertyChangedEventHandler> eventKey,
        TSource source,
        TTarget target,
        Action<TTarget> invoke)
        where TSource : class
        where TTarget : class
    {
        Validate(eventKey, source, target, invoke);
        Add(source, new PropertyChangedRegistration<TSource, TTarget>(
            eventKey, source, target, "Item[", matchPrefix: true, invoke));
    }

    internal static void RemoveHandler<TSource, THandler, TTarget>(
        WeakEventKey<TSource, THandler> eventKey,
        TSource source,
        TTarget target)
        where TSource : class
        where THandler : Delegate
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(eventKey);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (Sources.TryGetValue(source, out var bucket)
            && bucket.TryRemove(eventKey, target, out var registration))
        {
            registration.Detach();
        }
    }

    private static void Validate<TSource, TTarget>(
        object eventKey,
        TSource source,
        TTarget target,
        Delegate invoke)
        where TSource : class
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(eventKey);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(invoke);

        if (WeakEventDelegate.CapturesState(invoke))
        {
            throw new ArgumentException(
                "Weak event callbacks must be static and use the supplied target argument.",
                nameof(invoke));
        }
    }

    private static void Add<TSource>(TSource source, IRegistration registration)
        where TSource : class
    {
        var bucket = Sources.GetOrCreateValue(source);
        if (!bucket.TryAdd(registration))
        {
            throw new InvalidOperationException(
                "The target is already registered for this event and source.");
        }

        try
        {
            registration.Attach();
        }
        catch
        {
            bucket.Remove(registration);
            registration.AbortAttach();
            throw;
        }
    }

    private static void RemoveDead<TSource>(TSource source, IRegistration registration)
        where TSource : class
    {
        if (Sources.TryGetValue(source, out var bucket))
        {
            bucket.Remove(registration);
        }

        registration.Detach();
    }

    private interface IRegistration
    {
        object EventKey { get; }
        bool HasTarget(object target);
        bool TryGetTargetObject(out object target);
        void Attach();
        void AbortAttach();
        void Detach();
    }

    private sealed class SourceBucket
    {
        private readonly object _lock = new();
        private readonly List<IRegistration> _registrations = [];

        public bool TryAdd(IRegistration registration)
        {
            lock (_lock)
            {
                if (registration.TryGetTargetObject(out var target)
                    && _registrations.Any(
                        current => ReferenceEquals(current.EventKey, registration.EventKey)
                            && current.HasTarget(target)))
                {
                    return false;
                }

                _registrations.Add(registration);
                return true;
            }
        }

        public bool TryRemove(object eventKey, object target, out IRegistration registration)
        {
            lock (_lock)
            {
                for (int i = 0; i < _registrations.Count; i++)
                {
                    var current = _registrations[i];
                    if (ReferenceEquals(current.EventKey, eventKey) && current.HasTarget(target))
                    {
                        _registrations.RemoveAt(i);
                        registration = current;
                        return true;
                    }
                }
            }

            registration = null!;
            return false;
        }

        public void Remove(IRegistration registration)
        {
            lock (_lock)
            {
                _registrations.Remove(registration);
            }
        }
    }

    private abstract class Registration<TSource, TTarget, THandler> : IRegistration
        where TSource : class
        where TTarget : class
        where THandler : Delegate
    {
        private const int Created = 0;
        private const int Attaching = 1;
        private const int Attached = 2;
        private const int Detached = 3;

        private readonly WeakReference<TSource> _source;
        private readonly WeakReference<TTarget> _target;
        private int _state;

        protected Registration(
            WeakEventKey<TSource, THandler> eventKey,
            TSource source,
            TTarget target)
        {
            EventKeyValue = eventKey;
            _source = new WeakReference<TSource>(source);
            _target = new WeakReference<TTarget>(target);
        }

        protected WeakEventKey<TSource, THandler> EventKeyValue { get; }
        protected abstract THandler Handler { get; }
        public object EventKey => EventKeyValue;

        public void Attach()
        {
            if (Interlocked.CompareExchange(ref _state, Attaching, Created) != Created)
            {
                return;
            }

            if (!_source.TryGetTarget(out var source))
            {
                Volatile.Write(ref _state, Detached);
                return;
            }

            EventKeyValue.AddHandler(source, Handler);
            if (Interlocked.CompareExchange(ref _state, Attached, Attaching) == Detached)
            {
                EventKeyValue.RemoveHandler(source, Handler);
            }
        }

        public void AbortAttach()
        {
            int previous = Interlocked.Exchange(ref _state, Detached);
            if ((previous == Attaching || previous == Attached) && _source.TryGetTarget(out var source))
            {
                try
                {
                    EventKeyValue.RemoveHandler(source, Handler);
                }
                catch
                {
                    // Preserve the exception raised by the add accessor.
                }
            }
        }

        public void Detach()
        {
            while (true)
            {
                int state = Volatile.Read(ref _state);
                if (state == Detached)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _state, Detached, state) != state)
                {
                    continue;
                }

                if (state == Attached && _source.TryGetTarget(out var source))
                {
                    EventKeyValue.RemoveHandler(source, Handler);
                }

                return;
            }
        }

        public bool HasTarget(object target) =>
            _target.TryGetTarget(out var current) && ReferenceEquals(current, target);

        public bool TryGetTargetObject(out object target)
        {
            if (_target.TryGetTarget(out var current))
            {
                target = current;
                return true;
            }

            target = null!;
            return false;
        }

        protected bool TryGetLiveTarget(out TTarget target) => _target.TryGetTarget(out target!);

        protected void RemoveDeadTarget()
        {
            if (_source.TryGetTarget(out var source))
            {
                RemoveDead(source, this);
            }
            else
            {
                Detach();
            }
        }
    }

    private sealed class ActionRegistration<TSource, TTarget>
        : Registration<TSource, TTarget, Action>
        where TSource : class
        where TTarget : class
    {
        private readonly Action<TTarget> _invoke;
        private readonly Action _handler;

        public ActionRegistration(
            WeakEventKey<TSource, Action> eventKey,
            TSource source,
            TTarget target,
            Action<TTarget> invoke)
            : base(eventKey, source, target)
        {
            _invoke = invoke;
            _handler = OnEvent;
        }

        protected override Action Handler => _handler;

        private void OnEvent()
        {
            if (TryGetLiveTarget(out var target))
            {
                _invoke(target);
            }
            else
            {
                RemoveDeadTarget();
            }
        }
    }

    private sealed class CollectionChangedRegistration<TSource, TTarget>
        : Registration<TSource, TTarget, NotifyCollectionChangedEventHandler>
        where TSource : class
        where TTarget : class
    {
        private readonly Action<TTarget, object?, NotifyCollectionChangedEventArgs> _invoke;
        private readonly NotifyCollectionChangedEventHandler _handler;

        public CollectionChangedRegistration(
            WeakEventKey<TSource, NotifyCollectionChangedEventHandler> eventKey,
            TSource source,
            TTarget target,
            Action<TTarget, object?, NotifyCollectionChangedEventArgs> invoke)
            : base(eventKey, source, target)
        {
            _invoke = invoke;
            _handler = OnEvent;
        }

        protected override NotifyCollectionChangedEventHandler Handler => _handler;

        private void OnEvent(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (TryGetLiveTarget(out var target))
            {
                _invoke(target, sender, args);
            }
            else
            {
                RemoveDeadTarget();
            }
        }
    }

    private sealed class PropertyChangedRegistration<TSource, TTarget>
        : Registration<TSource, TTarget, PropertyChangedEventHandler>
        where TSource : class
        where TTarget : class
    {
        private readonly string _propertyName;
        private readonly bool _matchPrefix;
        private readonly Action<TTarget> _invoke;
        private readonly PropertyChangedEventHandler _handler;

        public PropertyChangedRegistration(
            WeakEventKey<TSource, PropertyChangedEventHandler> eventKey,
            TSource source,
            TTarget target,
            string propertyName,
            bool matchPrefix,
            Action<TTarget> invoke)
            : base(eventKey, source, target)
        {
            _propertyName = propertyName;
            _matchPrefix = matchPrefix;
            _invoke = invoke;
            _handler = OnEvent;
        }

        protected override PropertyChangedEventHandler Handler => _handler;

        private void OnEvent(object? sender, PropertyChangedEventArgs args)
        {
            if (TryGetLiveTarget(out var target))
            {
                // A null or empty name is the conventional "every property changed" signal.
                string? changedName = args.PropertyName;
                if (string.IsNullOrEmpty(changedName) || Matches(changedName!))
                {
                    _invoke(target);
                }
            }
            else
            {
                RemoveDeadTarget();
            }
        }

        private bool Matches(string changedName)
            => _matchPrefix
                ? changedName.StartsWith(_propertyName, StringComparison.Ordinal)
                : string.Equals(changedName, _propertyName, StringComparison.Ordinal);
    }
}

internal static class WeakEventDelegate
{
    internal static bool CapturesState(Delegate handler)
    {
        if (handler.Target == null)
        {
            return false;
        }

        var targetType = handler.Target.GetType();
        return !targetType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            || targetType.Name.Contains("DisplayClass", StringComparison.Ordinal);
    }
}

internal static class CollectionWeakEvents
{
    internal static readonly WeakEventKey<INotifyCollectionChanged, NotifyCollectionChangedEventHandler>
        CollectionChanged = new(
            static (source, handler) => source.CollectionChanged += handler,
            static (source, handler) => source.CollectionChanged -= handler);
}

internal static class InpcWeakEvents
{
    internal static readonly WeakEventKey<INotifyPropertyChanged, PropertyChangedEventHandler>
        PropertyChanged = new(
            static (source, handler) => source.PropertyChanged += handler,
            static (source, handler) => source.PropertyChanged -= handler);
}

internal static class ObservableValueWeakEvents<T>
{
    internal static readonly WeakEventKey<ObservableValue<T>, Action> Changed = new(
        static (source, handler) => source.Changed += handler,
        static (source, handler) => source.Changed -= handler);
}
