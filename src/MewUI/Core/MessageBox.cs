namespace Aprillz.MewUI;

/// <summary>
/// Common button configurations for <see cref="MessageBox"/>.
/// </summary>
public enum MessageBoxButtons : uint
{
    Ok = 0x00000000,
    OkCancel = 0x00000001,
    YesNo = 0x00000004,
    YesNoCancel = 0x00000003
}

/// <summary>
/// Common icon configurations for <see cref="MessageBox"/>.
/// </summary>
public enum MessageBoxIcon : uint
{
    None = 0x00000000,
    Information = 0x00000040,
    Warning = 0x00000030,
    Error = 0x00000010,
    Question = 0x00000020
}

/// <summary>
/// Result values returned by <see cref="MessageBox.Show(string,string,MessageBoxButtons,MessageBoxIcon)"/>.
/// </summary>
public enum MessageBoxResult
{
    Ok = 1,
    Cancel = 2,
    Yes = 6,
    No = 7
}

/// <summary>
/// Provides a simple, cross-platform message box API routed through the active <see cref="Platform.IPlatformHost"/>.
/// </summary>
public static class MessageBox
{
    /// <summary>
    /// Shows a message box without specifying an owner.
    /// </summary>
    public static MessageBoxResult Show(string text, string caption = "Aprillz.MewUI", MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None)
        => Show(0, text, caption, buttons, icon);

    /// <summary>
    /// Shows a message box with a native owner handle.
    /// </summary>
    public static MessageBoxResult Show(nint owner, string text, string caption = "Aprillz.MewUI", MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None)
    {
        // Route through platform host so non-Win32 platforms can provide their own implementation.
        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        return host.MessageBox.Show(owner, text ?? string.Empty, caption ?? string.Empty, buttons, icon);
    }
}
