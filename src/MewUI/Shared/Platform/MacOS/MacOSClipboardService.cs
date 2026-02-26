namespace Aprillz.MewUI.Platform.MacOS;

internal sealed class MacOSClipboardService : IClipboardService
{
    public bool TrySetText(string text)
        => MacOSInterop.TrySetClipboardText(text);

    public bool TryGetText(out string text)
    {
        return MacOSInterop.TryGetClipboardText(out text);
    }
}
