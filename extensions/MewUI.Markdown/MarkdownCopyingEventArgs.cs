namespace Aprillz.MewUI.Markdown;

/// <summary>Describes a pending copy of the selection; a handler may replace the text or cancel the copy.</summary>
public sealed class MarkdownCopyingEventArgs : EventArgs
{
    internal MarkdownCopyingEventArgs(string text) => Text = text;

    /// <summary>Gets or sets the plain text written to the clipboard.</summary>
    public string Text { get; set; }

    /// <summary>Gets or sets whether the copy is canceled.</summary>
    public bool Cancel { get; set; }
}
