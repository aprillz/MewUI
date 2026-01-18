namespace Aprillz.MewUI;

public enum MessageBoxButtons : uint
{
    Ok = 0x00000000,
    OkCancel = 0x00000001,
    YesNo = 0x00000004,
    YesNoCancel = 0x00000003
}

public enum MessageBoxIcon : uint
{
    None = 0x00000000,
    Information = 0x00000040,
    Warning = 0x00000030,
    Error = 0x00000010,
    Question = 0x00000020
}

public enum MessageBoxResult
{
    Ok = 1,
    Cancel = 2,
    Yes = 6,
    No = 7
}

public static class MessageBox
{
    public static MessageBoxResult Show(string text, string caption = "Aprillz.MewUI", MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None)
        => Show(0, text, caption, buttons, icon);

    public static MessageBoxResult Show(nint owner, string text, string caption = "Aprillz.MewUI", MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None)
    {
        // Route through platform host so non-Win32 platforms can provide their own implementation.
        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        return host.MessageBox.Show(owner, text ?? string.Empty, caption ?? string.Empty, buttons, icon);
    }
}
