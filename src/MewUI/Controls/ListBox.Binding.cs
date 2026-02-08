namespace Aprillz.MewUI.Controls;

public partial class ListBox
{
    private static readonly BindingSlot SelectedIndexBindingSlot = new(nameof(SelectedIndexBindingSlot));

    private void SetSelectedIndexBindingCore(
        Func<int> get,
        Action<int> set,
        Action<Action>? subscribe,
        Action<Action>? unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        BindCore.Replace(
            this,
            SelectedIndexBindingSlot,
            get,
            set,
            subscribe,
            unsubscribe,
            () =>
            {
                _updatingFromSource = true;
                try { SelectedIndex = get(); }
                finally { _updatingFromSource = false; }
            });

        _updatingFromSource = true;
        try { SelectedIndex = get(); }
        finally { _updatingFromSource = false; }
    }
}

