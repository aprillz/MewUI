namespace Aprillz.MewUI;

internal abstract class BindingPathObserver : IDisposable
{
    internal abstract void OnSegmentChanged(int segmentIndex);

    public abstract void Dispose();
}

internal sealed class BindingPathSubscription(
    BindingPathObserver observer,
    int segmentIndex)
{
    internal void OnChanged() => observer.OnSegmentChanged(segmentIndex);
}

internal sealed class BindingPathObserver<TRoot, TValue> : BindingPathObserver
    where TRoot : class
{
    private readonly IReadOnlyList<IBindingPathSegment> _segments;
    private readonly object?[] _values;
    private readonly object?[] _endpoints;
    private readonly BindingPathSubscription?[] _subscriptions;
    private TRoot? _root;
    private bool _disposed;

    internal BindingPathObserver(BindingPath<TRoot, TValue> path, TRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        _segments = path.Segments;
        _values = new object?[_segments.Count];
        _endpoints = new object?[_segments.Count];
        _subscriptions = new BindingPathSubscription?[_segments.Count];
        _root = root;

        try
        {
            if (_segments.Count == 0)
            {
                IsAvailable = true;
                CurrentValue = (TValue)(object)root;
            }
            else
            {
                AttachFrom(0, root);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal event Action? Changed;

    internal bool IsAvailable { get; private set; }

    internal TValue CurrentValue { get; private set; } = default!;

    internal bool CanWrite => _segments.Count > 0 && _segments[^1].CanWrite;

    internal void Write(TValue value)
    {
        if (_disposed || !IsAvailable || !CanWrite)
        {
            return;
        }

        var endpoint = _endpoints[^1];
        if (endpoint != null)
        {
            _segments[^1].Write(endpoint, value);
        }
    }

    internal override void OnSegmentChanged(int segmentIndex)
    {
        if (_disposed)
        {
            return;
        }

        DetachFrom(segmentIndex + 1);

        try
        {
            var endpoint = _endpoints[segmentIndex]
                ?? throw new InvalidOperationException("An observed BindingPath segment has no endpoint.");
            var value = _segments[segmentIndex].Read(endpoint);
            _values[segmentIndex] = value;

            if (segmentIndex == _segments.Count - 1)
            {
                SetAvailableValue(value);
            }
            else if (value == null)
            {
                SetUnavailable();
            }
            else
            {
                AttachFrom(segmentIndex + 1, value);
            }
        }
        catch
        {
            DetachFrom(segmentIndex + 1);
            SetUnavailable(notify: false);
            throw;
        }

        Changed?.Invoke();
    }

    private void AttachFrom(int startIndex, object owner)
    {
        for (var index = startIndex; index < _segments.Count; index++)
        {
            var segment = _segments[index];
            var subscription = segment.IsObservable
                ? new BindingPathSubscription(this, index)
                : null;
            var result = segment.Attach(owner, subscription);

            _values[index] = result.Value;
            _endpoints[index] = result.Endpoint;
            _subscriptions[index] = subscription;

            if (index == _segments.Count - 1)
            {
                SetAvailableValue(result.Value);
                return;
            }

            if (result.Value == null)
            {
                SetUnavailable();
                return;
            }

            owner = result.Value;
        }
    }

    private void SetAvailableValue(object? value)
    {
        CurrentValue = (TValue)value!;
        IsAvailable = true;
    }

    private void SetUnavailable(bool notify = false)
    {
        CurrentValue = default!;
        IsAvailable = false;
        if (notify)
        {
            Changed?.Invoke();
        }
    }

    private void DetachFrom(int startIndex)
    {
        for (var index = _segments.Count - 1; index >= startIndex; index--)
        {
            var endpoint = _endpoints[index];
            var subscription = _subscriptions[index];
            if (endpoint != null && subscription != null)
            {
                _segments[index].Detach(endpoint, subscription);
            }

            _values[index] = null;
            _endpoints[index] = null;
            _subscriptions[index] = null;
        }
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachFrom(0);
        Changed = null;
        _root = null;
        CurrentValue = default!;
        IsAvailable = false;
    }
}
