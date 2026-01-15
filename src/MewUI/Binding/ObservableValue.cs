namespace Aprillz.MewUI;

public sealed class ObservableValue<T>
{
    private readonly Func<T, T>? _coerce;
    private readonly IEqualityComparer<T> _comparer;
    private T _value;

    public event Action? Changed;

    public ObservableValue(
        T initialValue = default!,
        Func<T, T>? coerce = null,
        IEqualityComparer<T>? comparer = null)
    {
        _coerce = coerce;
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _value = _coerce != null ? _coerce(initialValue) : initialValue;
    }

    public T Value
    {
        get => _value;
        set => Set(value);
    }

    public bool Set(T value)
    {
        if (_coerce != null)
        {
            value = _coerce(value);
        }

        if (_comparer.Equals(_value, value))
        {
            return false;
        }

        _value = value;
        Changed?.Invoke();
        return true;
    }

    public void NotifyChanged() => Changed?.Invoke();

    public void Subscribe(Action handler) => Changed += handler;

    public void Unsubscribe(Action handler) => Changed -= handler;
}

