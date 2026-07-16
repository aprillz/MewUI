namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxMessageBoxService : IMessageBoxService
{
    public bool IsNativeDialogAvailable() => false;

    public bool? Show(nint owner, string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
        => throw new PlatformNotSupportedException("No native message-box service is available on Linux.");
}
