namespace Aprillz.MewUI.Controls;

public abstract partial class TextBase
{
    private static readonly BindingSlot TextBindingSlot = new(nameof(TextBindingSlot));

    private void SetTextBindingCore(
        Func<string> get,
        Action<string> set,
        Action<Action>? subscribe,
        Action<Action>? unsubscribe)
    {
        BindCore.Replace(
            this,
            TextBindingSlot,
            get,
            set,
            subscribe,
            unsubscribe,
            () =>
            {
                if (IsFocused)
                {
                    return;
                }

                var value = NormalizeText(get() ?? string.Empty);
                if (GetTextCore() == value)
                {
                    return;
                }

                _suppressBindingSet = true;
                try { Text = value; }
                finally { _suppressBindingSet = false; }
            });

        // Ensure the binding forwarder is registered once (no duplicates), without tracking extra state.
        TextChanged -= ForwardTextChangedToBinding;
        TextChanged += ForwardTextChangedToBinding;

        _suppressBindingSet = true;
        try { Text = NormalizeText(get() ?? string.Empty); }
        finally { _suppressBindingSet = false; }
    }
}

