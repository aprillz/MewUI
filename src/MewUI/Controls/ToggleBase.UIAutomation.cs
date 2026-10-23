namespace Aprillz.MewUI.Controls;

public abstract partial class ToggleBase
{
    /// <summary>Toggles as the keyboard does, for an automation client's toggle or select.</summary>
    internal void ToggleFromAutomation() => ToggleFromKeyboard();
}
