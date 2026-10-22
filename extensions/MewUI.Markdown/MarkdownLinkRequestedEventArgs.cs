namespace Aprillz.MewUI.Markdown;

/// <summary>Describes a link request; the library never launches an external application.</summary>
public sealed class MarkdownLinkRequestedEventArgs : EventArgs
{
    internal MarkdownLinkRequestedEventArgs(string url, Uri? resolvedUri, string? title, int sourceStart, int sourceLength)
    {
        Url = url;
        ResolvedUri = resolvedUri;
        Title = title;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
    }

    /// <summary>Gets the original Markdown URL.</summary>
    public string Url { get; }
    /// <summary>Gets the absolute resolved URI, or null when resolution is unavailable.</summary>
    public Uri? ResolvedUri { get; }
    /// <summary>Gets the optional Markdown title.</summary>
    public string? Title { get; }
    /// <summary>Gets the start of the link in the Markdown source in UTF-16 code units, or -1 when unavailable.</summary>
    public int SourceStart { get; }
    /// <summary>Gets the length of the link in the Markdown source in UTF-16 code units.</summary>
    public int SourceLength { get; }
}
