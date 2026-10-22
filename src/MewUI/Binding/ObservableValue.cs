namespace Aprillz.MewUI;

/// <summary>
/// A mutable value that raises <see cref="Changed"/> when it is set to a different value, usable as a
/// binding source.
/// </summary>
public sealed class ObservableValue<T>
{
    private readonly Func<T, T>? _coerce;
    private readonly IEqualityComparer<T> _comparer;
    private T _value;

    /// <summary>
    /// Occurs when the value changes or <see cref="NotifyChanged"/> is called.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Creates a value with an initial value, an optional coercion applied to every assigned value and
    /// an optional comparer that decides whether an assignment is a change.
    /// </summary>
    public ObservableValue(
        T initialValue = default!,
        Func<T, T>? coerce = null,
        IEqualityComparer<T>? comparer = null)
    {
        _coerce = coerce;
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _value = _coerce != null ? _coerce(initialValue) : initialValue;
    }

    /// <summary>
    /// Gets or sets the current value; assigning an equal value raises no change.
    /// </summary>
    public T Value
    {
        get => _value;
        set => Set(value);
    }

    /// <summary>
    /// Sets the value and returns whether it changed.
    /// </summary>
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

    /// <summary>
    /// Raises <see cref="Changed"/> without changing the value.
    /// </summary>
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>
    /// Adds a <see cref="Changed"/> handler.
    /// </summary>
    public void Subscribe(Action handler) => Changed += handler;

    /// <summary>
    /// Removes a <see cref="Changed"/> handler.
    /// </summary>
    public void Unsubscribe(Action handler) => Changed -= handler;
}

