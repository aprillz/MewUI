namespace Aprillz.MewUI.Platform;

/// <summary>Reads and writes the system clipboard as plain text.</summary>
public interface IClipboardService
{
    /// <summary>Places <paramref name="text"/> on the clipboard; returns <see langword="false"/> when the clipboard could not be written.</summary>
    bool TrySetText(string text);

    /// <summary>Reads the clipboard text; returns <see langword="false"/> when the clipboard holds no text.</summary>
    bool TryGetText(out string text);

    /// <summary>Returns whether the clipboard currently holds text, without fetching it.</summary>
    /// <remarks>
    /// Asked whenever a paste command re-evaluates, so an implementation that can answer from a format
    /// query should; the default reads the text and throws it away.
    /// </remarks>
    bool HasText() => TryGetText(out _);
}
