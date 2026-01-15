namespace Aprillz.MewUI;

public sealed class ValueBinding<T> : IDisposable
{
    private readonly Func<T> _get;
    private readonly Action<T>? _set;
    private readonly Action<Action>? _subscribe;
    private readonly Action<Action>? _unsubscribe;
    private readonly Action _onSourceChanged;
    private bool _disposed;

    public ValueBinding(
        Func<T> get,
        Action<T>? set,
        Action<Action>? subscribe,
        Action<Action>? unsubscribe,
        Action onSourceChanged)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(onSourceChanged);

        _get = get;
        _set = set;
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
        _onSourceChanged = onSourceChanged;

        _subscribe?.Invoke(_onSourceChanged);
    }

    public T Get() => _get();

    public void Set(T value) => _set?.Invoke(value);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _unsubscribe?.Invoke(_onSourceChanged);
        _disposed = true;
    }
}
