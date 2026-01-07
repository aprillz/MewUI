using Aprillz.MewUI.Core;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Platform.Linux.X11;

internal sealed class X11MessageBoxService : IMessageBoxService
{
    public MessageBoxResult Show(nint owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => throw new PlatformNotSupportedException("MessageBox is not implemented for X11 yet.");
}

