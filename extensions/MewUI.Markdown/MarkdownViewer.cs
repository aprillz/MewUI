namespace Aprillz.MewUI.Markdown;

/// <summary>Displays Markdown in an owned vertical scroll viewport with the same resource policy as the presenter.</summary>
public sealed class MarkdownViewer : MarkdownPresenter
{
    /// <summary>Creates a scrolling Markdown viewer.</summary>
    public MarkdownViewer() : base(true) { }
}
