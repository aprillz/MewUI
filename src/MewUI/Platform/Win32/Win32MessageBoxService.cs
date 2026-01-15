using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Win32;

internal sealed class Win32MessageBoxService : IMessageBoxService
{
    public MessageBoxResult Show(nint owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        var type = (uint)buttons | (uint)icon;
        int result = User32.MessageBox(owner, text ?? string.Empty, caption ?? string.Empty, type);
        return result switch
        {
            1 => MessageBoxResult.Ok,
            2 => MessageBoxResult.Cancel,
            6 => MessageBoxResult.Yes,
            7 => MessageBoxResult.No,
            _ => MessageBoxResult.Ok
        };
    }
}

