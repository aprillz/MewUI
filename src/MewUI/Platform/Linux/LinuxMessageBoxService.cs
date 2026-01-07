namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxMessageBoxService : IMessageBoxService
{
    public Core.MessageBoxResult Show(nint owner, string text, string caption, Core.MessageBoxButtons buttons, Core.MessageBoxIcon icon)
        => throw new PlatformNotSupportedException("MessageBox is not implemented on Linux yet.");
}
