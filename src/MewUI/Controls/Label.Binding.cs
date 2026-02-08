namespace Aprillz.MewUI.Controls;

public partial class Label
{
    private static readonly BindingSlot TextBindingSlot = new(nameof(TextBindingSlot));

    private void SetTextBindingCore(Func<string> get, Action<Action>? subscribe, Action<Action>? unsubscribe)
    {
        BindCore.Replace(
            this,
            TextBindingSlot,
            get,
            set: null,
            subscribe,
            unsubscribe,
            () => SetTextFromBinding(get()));

        SetTextFromBinding(get());
    }
}

