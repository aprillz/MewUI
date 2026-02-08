namespace Aprillz.MewUI;

public abstract partial class UIElement
{
    private static readonly BindingSlot IsVisibleBindingSlot = new(nameof(IsVisibleBindingSlot));
    private static readonly BindingSlot IsEnabledBindingSlot = new(nameof(IsEnabledBindingSlot));

    internal void SetIsVisibleBinding(Func<bool> get, Action<Action>? subscribe = null, Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);

        BindCore.Replace(
            this,
            IsVisibleBindingSlot,
            get,
            set: null,
            subscribe,
            unsubscribe,
            () => IsVisible = get());

        IsVisible = get();
    }

    internal void SetIsEnabledBinding(Func<bool> get, Action<Action>? subscribe = null, Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);

        BindCore.Replace(
            this,
            IsEnabledBindingSlot,
            get,
            set: null,
            subscribe,
            unsubscribe,
            () => IsEnabled = get());

        IsEnabled = get();
    }
}

