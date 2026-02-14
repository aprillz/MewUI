namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxMessageBoxService : IMessageBoxService
{
    public MessageBoxResult Show(nint owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => LinuxExternalDialogs.ShowMessageBox(owner, text ?? string.Empty, caption ?? string.Empty, buttons, icon);
}
