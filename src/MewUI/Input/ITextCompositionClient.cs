namespace Aprillz.MewUI.Input;

/// <summary>
/// Receives IME text composition (preedit) events.
/// </summary>
/// <remarks>
/// Implement this only on elements that actively edit text (e.g. TextBox).
/// </remarks>
public interface ITextCompositionClient
{
    /// <summary>
    /// Receives the start of an IME composition.
    /// </summary>
    void HandleTextCompositionStart(TextCompositionEventArgs e);

    /// <summary>
    /// Receives an update to the composition text or its attributes.
    /// </summary>
    void HandleTextCompositionUpdate(TextCompositionEventArgs e);

    /// <summary>
    /// Receives the end of an IME composition, after which the committed text arrives as text input.
    /// </summary>
    void HandleTextCompositionEnd(TextCompositionEventArgs e);

    /// <summary>
    /// Gets whether an IME composition is currently in progress.
    /// </summary>
    bool IsComposing { get; }

    /// <summary>
    /// Gets the character index where the current composition started.
    /// </summary>
    int CompositionStartIndex { get; }

    /// <summary>
    /// Returns the rectangle at the given character index in window coordinates (DIPs).
    /// </summary>
    /// <remarks>
    /// Used for IME candidate window positioning.
    /// </remarks>
    Rect GetCharRectInWindow(int charIndex);
}

