using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A single-line password input control that masks entered text.
/// </summary>
public sealed class PasswordBox : SingleLineTextBase
{
    public static readonly MewProperty<string> PasswordProperty =
        MewProperty<string>.Register<PasswordBox>(nameof(Password), string.Empty,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, value) => self.ApplyExternalTextCore(value));

    public static readonly MewProperty<char> PasswordCharProperty =
        MewProperty<char>.Register<PasswordBox>(nameof(PasswordChar), '●',
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.InvalidateTextPipeline());

    public PasswordBox()
    {
        _extensions.Projections.Add(new MaskProjection(this));
        // Undo entries keep the replaced text verbatim, so a history would hold every password the
        // box has held, surviving even the caller clearing Password.
        _document.History.SizeLimit = 0;
    }

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
        get => GetTextSnapshot();
        set => SetExternalText(PasswordProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Occurs when the password text changes. Carries no value so the password is never exposed
    /// through this notification channel; read <see cref="Password"/> directly if the value is needed.
    /// </summary>
    public event Action? PasswordChanged;

    private protected override MewProperty<string>? TextSyncProperty => PasswordProperty;

    private protected override bool HasTextChangedSubscribers => PasswordChanged is not null;

    private protected override void RaiseTextChanged(string text)
    {
        // Suppress the plaintext-carrying TextChanged event for passwords; notify without a value instead.
        PasswordChanged?.Invoke();
    }

    private protected override string GetMeasureSample()
        => _document.TextLength == 0 ? string.Empty : new string(PasswordChar, _document.TextLength);

    /// <summary>
    /// Projects every UTF-16 unit to the mask character so layout, caret geometry, and hit-testing
    /// all operate on the masked shape while the document keeps the real characters.
    /// </summary>
    private sealed class MaskProjection(PasswordBox owner) : ITextProjection
    {
        public ProjectedText Project(in TextProjectionContext context)
            => new(new string(owner.PasswordChar, context.SourceText.Length).AsMemory(), IdentityTextOffsetMap.Instance);
    }
}
