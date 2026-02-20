namespace Aprillz.MewUI.Input;

/// <summary>
/// Platform text input target. Used to deliver committed text (including IME commits) to text controls
/// without exposing text input events on every <c>UIElement</c>.
/// </summary>
public interface ITextInputClient
{
    void HandleTextInput(TextInputEventArgs e);
}

