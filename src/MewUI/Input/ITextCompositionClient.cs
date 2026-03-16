namespace Aprillz.MewUI.Input;

/// <summary>
/// Receives IME text composition (preedit) events.
/// Implement this only on elements that actively edit text (e.g. TextBox).
/// </summary>
public interface ITextCompositionClient
{
    void HandleTextCompositionStart(TextCompositionEventArgs e);

    void HandleTextCompositionUpdate(TextCompositionEventArgs e);

    void HandleTextCompositionEnd(TextCompositionEventArgs e);

    /// <summary>
    /// Gets the character index where the current composition started.
    /// </summary>
    int CompositionStartIndex { get; }

    /// <summary>
    /// Returns the rectangle at the given character index in window coordinates (DIPs).
    /// Used for IME candidate window positioning.
    /// </summary>
    Rect GetCharRectInWindow(int charIndex);
}

