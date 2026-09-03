namespace Aprillz.MewUI.Controls;

/// <summary>
/// A single-line text input control.
/// </summary>
public sealed class TextBox : SingleLineTextBase
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<TextBox>(nameof(Text), string.Empty,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, value) => self.ApplyExternalTextCore(value));

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
    {
        get => GetTextSnapshot();
        set => SetExternalText(TextProperty, value ?? string.Empty);
    }

    private protected override MewProperty<string>? TextSyncProperty => TextProperty;

    /// <summary>
    /// Gets the currently selected text.
    /// </summary>
    public string SelectedText => GetSelectedDocumentText();

    private protected override bool SupportsClipboardCopy => true;

    private protected override string? GetClipboardCopyText() => SelectedText;
}
