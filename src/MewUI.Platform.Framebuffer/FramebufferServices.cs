namespace Aprillz.MewUI.Platform.Framebuffer;

internal sealed class FramebufferMessageBoxService : IMessageBoxService
{
    public bool? Show(nint owner, string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
    {
        Console.WriteLine($"[{caption}] {text}");
        return true;
    }
}

internal sealed class FramebufferFileDialogService : IFileDialogService
{
    public string[]? OpenFile(OpenFileDialogOptions options) => null;

    public string? SaveFile(SaveFileDialogOptions options) => null;

    public string? SelectFolder(FolderDialogOptions options) => null;
}

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
        return !string.IsNullOrEmpty(text);
    }
}
