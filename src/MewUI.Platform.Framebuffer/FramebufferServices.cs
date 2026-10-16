namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>No native message box exists; the framework shows its own dialog inside the screen.</summary>
internal sealed class FramebufferMessageBoxService : IMessageBoxService
{
    public bool IsNativeDialogAvailable() => false;

    public bool? Show(nint owner, string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
        => throw new NotSupportedException("The framebuffer platform has no native message box.");
}

/// <summary>No native file dialog exists; the framework shows its own dialog inside the screen.</summary>
internal sealed class FramebufferFileDialogService : IFileDialogService
{
    public bool IsNativeDialogAvailable() => false;

    public string[]? OpenFile(OpenFileDialogOptions options)
        => throw new NotSupportedException("The framebuffer platform has no native file dialog.");

    public string? SaveFile(SaveFileDialogOptions options)
        => throw new NotSupportedException("The framebuffer platform has no native file dialog.");

    public string? SelectFolder(FolderDialogOptions options)
        => throw new NotSupportedException("The framebuffer platform has no native file dialog.");
}

/// <summary>A clipboard that lives in this process, since there is no display server to share one with.</summary>
internal sealed class FramebufferClipboardService : IClipboardService
{
    private string _text = string.Empty;

    public bool TrySetText(string text)
    {
        _text = text ?? string.Empty;
        return true;
    }

    public bool TryGetText(out string text)
    {
        text = _text;
        return text.Length > 0;
    }

    public bool HasText() => _text.Length > 0;
}
