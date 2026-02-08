namespace Aprillz.MewUI.Controls;

public partial class CheckBox
{
    private static readonly BindingSlot CheckedBindingSlot = new(nameof(CheckedBindingSlot));

    private void SetIsCheckedBindingCore(
        Func<bool?> get,
        Action<bool?> set,
        Action<Action>? subscribe,
        Action<Action>? unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        BindCore.Replace(
            this,
            CheckedBindingSlot,
            get,
            set,
            subscribe,
            unsubscribe,
            () =>
            {
                _updatingFromSource = true;
                try { SetIsCheckedFromSource(get()); }
                finally { _updatingFromSource = false; }
            });

        _updatingFromSource = true;
        try { SetIsCheckedFromSource(get()); }
        finally { _updatingFromSource = false; }
    }
}

