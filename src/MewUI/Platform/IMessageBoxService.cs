namespace Aprillz.MewUI.Platform;

public interface IMessageBoxService
{
    MessageBoxResult Show(nint owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon);
}

