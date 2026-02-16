namespace Aprillz.MewUI.Controls;

public sealed partial class NumericUpDown
{
    private static readonly BindingSlot ValueBindingSlot = new(nameof(ValueBindingSlot));

    public void SetValueBinding(
        Func<double> get,
        Action<double> set,
        Action<Action>? subscribe,
        Action<Action>? unsubscribe)
    {
        BindCore.Replace(
            this,
            ValueBindingSlot,
            get,
            set,
            subscribe,
            unsubscribe,
            () => SetValueFromSource(get()));

        SetValueFromSource(get());
    }
}
