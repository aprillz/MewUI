namespace Aprillz.MewUI.Input;

/// <summary>
/// Platform text input target that receives committed text (including IME commits) without exposing text
/// input events on every <see cref="Controls.UIElement"/>.
/// </summary>
public interface ITextInputClient
{
    /// <summary>
    /// Receives committed text, including IME commits.
    /// </summary>
    void HandleTextInput(TextInputEventArgs e);
}

