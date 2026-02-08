namespace Aprillz.MewUI.Controls;

public sealed partial class ProgressBar
{
    private static readonly BindingSlot ValueBindingSlot = new(nameof(ValueBindingSlot));

    private void SetValueBindingCore(Func<double> get, Action<Action>? subscribe, Action<Action>? unsubscribe)
    {
        BindCore.Replace(
            this,
            ValueBindingSlot,
            get,
            set: null,
            subscribe,
            unsubscribe,
            () => Value = get());

        Value = get();
    }
}

