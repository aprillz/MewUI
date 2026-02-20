
namespace Aprillz.MewUI.Controls;

public sealed partial class ToggleButton
{
    private static readonly BindingSlot ContentBindingSlot = new(nameof(ContentBindingSlot));

    public void SetContentBinding(Func<string> get, Action<Action>? subscribe = null, Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);

        BindCore.Replace(
            this,
            ContentBindingSlot,
            get,
            set: null,
            subscribe,
            unsubscribe,
            () => Content = get() ?? string.Empty);

        Content = get() ?? string.Empty;
    }
}

