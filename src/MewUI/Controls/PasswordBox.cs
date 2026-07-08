namespace Aprillz.MewUI.Controls;

/// <summary>
/// A single-line password input control that masks entered text.
/// </summary>
public sealed class PasswordBox : SingleLineTextBase
{
    public static readonly MewProperty<string> PasswordProperty =
        MewProperty<string>.Register<PasswordBox>(nameof(Password), string.Empty,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.ApplyExternalTextPropertyChange(newVal));

    public static readonly MewProperty<char> PasswordCharProperty =
        MewProperty<char>.Register<PasswordBox>(nameof(PasswordChar), '●', MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Gets or sets the character used to mask the password.
    /// </summary>
    public char PasswordChar
    {
        get => GetValue(PasswordCharProperty);
        set => SetValue(PasswordCharProperty, value);
    }

    /// <summary>
    /// Gets or sets the password text.
    /// </summary>
    /// <remarks>
    /// The value is stored as a plain (unencrypted) string.
    /// Clear it manually after use (e.g., <c>passwordBox.Password = string.Empty;</c>) to minimize exposure in memory.
    /// </remarks>
    public string Password
    {
        get => GetTextCore();
        set => SetMirroredTextProperty(PasswordProperty, value);
    }

    /// <summary>
    /// Occurs when the password text changes. Carries no value so the password is never exposed
    /// through this notification channel; read <see cref="Password"/> directly if the value is needed.
    /// </summary>
    public event Action? PasswordChanged;

    /// <summary>The currently selected text, masked to its length (never the real characters).</summary>
    public override string SelectedText
    {
        get
        {
            var (start, end) = SelectionRange;
            int length = end - start;
            return length > 0 ? new string(PasswordChar, length) : string.Empty;
        }
    }

    protected override void CopyDocumentTo(char[] buffer, int start, int length)
    {
        Array.Fill(buffer, PasswordChar, 0, length);
    }

    protected override void CopyToClipboardCore()
    {
        // Prevent copying password to clipboard.
    }

    protected override void CutToClipboardCore()
    {
        // Prevent cutting password to clipboard.
    }

    protected override void NotifyTextChanged()
    {
        SyncTextPropertyFromDocument(PasswordProperty);
        base.NotifyTextChanged();
    }

    protected override void RaiseTextChanged()
    {
        // Suppress the plaintext-carrying TextChanged event for passwords; notify without a value instead.
        PasswordChanged?.Invoke();
    }
}
