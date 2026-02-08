namespace Aprillz.MewUI;

internal static class BindCore
{
    public static void Replace<T>(
        UIElement owner,
        BindingSlot slot,
        Func<T> get,
        Action<T>? set,
        Action<Action>? subscribe,
        Action<Action>? unsubscribe,
        Action onSourceChanged)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(onSourceChanged);

        owner.ReplaceBinding(slot, new ValueBinding<T>(get, set, subscribe, unsubscribe, onSourceChanged));
    }

    public static void Replace<T>(
        ref ValueBinding<T>? binding,
        Func<T> get,
        Action<T>? set,
        Action<Action>? subscribe,
        Action<Action>? unsubscribe,
        Action onSourceChanged)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(onSourceChanged);

        binding?.Dispose();
        binding = new ValueBinding<T>(get, set, subscribe, unsubscribe, onSourceChanged);
    }
}
