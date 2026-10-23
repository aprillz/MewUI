using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// The clipboard of a headless session, held in memory and never touching the system clipboard.
/// </summary>
public sealed class HeadlessClipboard : IClipboardService
{
    private readonly object _gate = new();
    private string? _text;

    internal HeadlessClipboard()
    {
    }

    /// <summary>Gets or sets the clipboard text; null when the clipboard is empty.</summary>
    public string? Text
    {
        get
        {
            lock (_gate)
            {
                return _text;
            }
        }
        set
        {
            lock (_gate)
            {
                _text = value;
            }
        }
    }

    /// <summary>Empties the clipboard.</summary>
    public void Clear() => Text = null;

    bool IClipboardService.TrySetText(string text)
    {
        Text = text;
        return true;
    }

    bool IClipboardService.TryGetText(out string text)
    {
        var value = Text;
        text = value ?? string.Empty;
        return value != null;
    }

    bool IClipboardService.HasText() => Text != null;
}
