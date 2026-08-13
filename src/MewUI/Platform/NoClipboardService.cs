namespace Aprillz.MewUI.Platform;

internal sealed class NoClipboardService : IClipboardService
{
    public bool TrySetText(string text) => false;

    public bool TryGetText(out string text)
    {
        text = string.Empty;
        return false;
    }

    public bool HasText() => false;
}

