namespace Aprillz.MewUI.Platform;

/// <summary>
/// Platform implementation for message box dialogs.
/// </summary>
public interface IMessageBoxService
{
    /// <summary>
    /// Shows a message box and returns the user selection.
    /// </summary>
    MessageBoxResult Show(nint owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon);
}
