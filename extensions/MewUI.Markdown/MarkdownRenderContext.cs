using Aprillz.MewUI.Controls;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Aprillz.MewUI.Markdown;

/// <summary>Services a custom renderer uses to read source text and delegate child content to the default pipeline.</summary>
public sealed class MarkdownRenderContext
{
    private readonly ParsedMarkdown _document;

    internal MarkdownRenderContext(MarkdownPresenter presenter, ParsedMarkdown document)
    {
        Presenter = presenter;
        _document = document;
    }

    /// <summary>Gets the presenter whose theme, base URI and resolver apply to the rendered element.</summary>
    public MarkdownPresenter Presenter { get; }

    /// <summary>Gets the document theme.</summary>
    public MarkdownTheme Theme => Presenter.MarkdownTheme;

    /// <summary>Returns the source text a node was parsed from.</summary>
    public string GetSource(MarkdownObject node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return MarkdownParser.GetSourceText(node, _document.Source);
    }

    /// <summary>Renders a container's child blocks with the default pipeline, including registered renderers.</summary>
    public FrameworkElement RenderBlocks(ContainerBlock container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return Presenter.RenderBlocks(MarkdownParser.MapChildren(
            container, _document.Source, _document.Options, _document.MappingProfile, _document.Anchors));
    }

    /// <summary>Renders inline content as a paragraph element with the default pipeline.</summary>
    public FrameworkElement RenderInlines(ContainerInline? inlines)
    {
        return Presenter.CreateParagraph(MarkdownParser.Flatten(
            inlines, _document.Options, _document.MappingProfile));
    }
}
