namespace Aprillz.MewUI.Platform.Linux.X11;

internal sealed class X11MessageBoxService : IMessageBoxService
{
    public MessageBoxResult Show(nint owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => LinuxExternalDialogs.ShowMessageBox(owner, text ?? string.Empty, caption ?? string.Empty, buttons, icon);
}
