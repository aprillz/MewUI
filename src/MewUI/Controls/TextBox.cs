namespace Aprillz.MewUI.Controls;

/// <summary>
/// A single-line text input control.
/// </summary>
public sealed class TextBox : SingleLineTextBase
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<TextBox>(nameof(Text), string.Empty,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.ApplyExternalTextPropertyChange(newVal));

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
    {
        get => GetTextCore();
        set => SetMirroredTextProperty(TextProperty, value);
    }

    protected override void NotifyTextChanged()
    {
        SyncTextPropertyFromDocument(TextProperty);
        base.NotifyTextChanged();
    }
}
