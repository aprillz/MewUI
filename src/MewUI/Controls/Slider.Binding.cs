namespace Aprillz.MewUI.Controls;

public sealed partial class Slider
{
    private static readonly BindingSlot ValueBindingSlot = new(nameof(ValueBindingSlot));

    private void SetValueBindingCore(
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
            () =>
            {
                if (_isDragging)
                {
                    return;
                }

                SetValueFromSource(get());
            });

        SetValueFromSource(get());
    }
}

