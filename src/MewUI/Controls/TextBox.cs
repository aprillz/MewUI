namespace Aprillz.MewUI.Controls;

/// <summary>
/// A single-line text input control.
/// </summary>
public sealed class TextBox : SingleLineTextBase
{
    private bool _syncingText;

    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<TextBox>(nameof(Text), string.Empty,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnTextPropertyChanged(newVal));

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
    {
        get => GetTextCore();
        set
        {
            var normalized = NormalizeText(value ?? string.Empty);
            if (GetTextCore() == normalized)
                return;
            SetValue(TextProperty, normalized);
        }
    }

    protected override void NotifyTextChanged()
    {
        _syncingText = true;
        try { SetValue(TextProperty, GetTextCore()); }
        finally { _syncingText = false; }
        base.NotifyTextChanged();
    }

    private void OnTextPropertyChanged(string newValue)
    {
        if (_syncingText) return;
        _syncingText = true;
        try { ApplyExternalTextChange(newValue); }
        finally { _syncingText = false; }
    }
}
