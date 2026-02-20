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
}

