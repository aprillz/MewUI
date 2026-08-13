namespace Aprillz.MewUI.Platform;

public interface IClipboardService
{
    bool TrySetText(string text);

    bool TryGetText(out string text);

    /// <summary>
    /// Whether the clipboard currently holds text, without fetching it. Asked whenever a paste
    /// command re-evaluates, so an implementation that can answer from a format query should:
    /// the default reads the text and throws it away.
    /// </summary>
    bool HasText() => TryGetText(out _);
}
