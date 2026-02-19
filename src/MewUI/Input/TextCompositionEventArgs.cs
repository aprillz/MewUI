namespace Aprillz.MewUI;

/// <summary>
/// Arguments for text composition (IME pre-edit) events.
/// </summary>
public sealed class TextCompositionEventArgs
{
    /// <summary>
    /// Gets the current composition text.
    /// For Start/End events this may be empty.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets or sets whether the event has been handled.
    /// </summary>
    public bool Handled { get; set; }

    public TextCompositionEventArgs(string? text = null)
    {
        Text = text ?? string.Empty;
    }
}

