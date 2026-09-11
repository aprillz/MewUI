using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Aprillz.MewUI.Markdown;

/// <summary>Advanced hook that maps Markdig node types to custom presentation; assign a configured instance, later registrations are not observed.</summary>
public sealed class MarkdownRenderers
{
    private readonly Dictionary<Type, Func<Block, MarkdownRenderContext, FrameworkElement?>> _blocks = [];
    private readonly Dictionary<Type, Func<Inline, MarkdownRenderContext, IInlineTextObject?>> _inlines = [];

    /// <summary>Registers a block renderer; the most derived registered type wins and a null result falls back to the default presentation.</summary>
    public MarkdownRenderers RegisterBlock<TBlock>(Func<TBlock, MarkdownRenderContext, FrameworkElement?> render) where TBlock : Block
    {
        ArgumentNullException.ThrowIfNull(render);
        _blocks[typeof(TBlock)] = (node, context) => render((TBlock)node, context);
        return this;
    }

    /// <summary>Registers an inline renderer whose object occupies the node's text columns; a null result shows the node as text.</summary>
    public MarkdownRenderers RegisterInline<TInline>(Func<TInline, MarkdownRenderContext, IInlineTextObject?> render) where TInline : Inline
    {
        ArgumentNullException.ThrowIfNull(render);
        _inlines[typeof(TInline)] = (node, context) => render((TInline)node, context);
        return this;
    }

    internal bool HasInlineRenderers => _inlines.Count > 0;

    internal bool CanRenderBlock(Type nodeType)
    {
        for (Type? type = nodeType; type != null && type != typeof(object); type = type.BaseType)
        {
            if (_blocks.ContainsKey(type))
            {
                return true;
            }
        }
        return false;
    }

    internal bool CanRenderInline(Type nodeType)
    {
        for (Type? type = nodeType; type != null && type != typeof(object); type = type.BaseType)
        {
            if (_inlines.ContainsKey(type))
            {
                return true;
            }
        }
        return false;
    }

    internal FrameworkElement? RenderBlock(Block node, MarkdownRenderContext context)
    {
        for (Type? type = node.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            if (_blocks.TryGetValue(type, out var render))
            {
                return render(node, context);
            }
        }
        return null;
    }

    internal IInlineTextObject? RenderInline(Inline node, MarkdownRenderContext context)
    {
        for (Type? type = node.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            if (_inlines.TryGetValue(type, out var render))
            {
                return render(node, context);
            }
        }
        return null;
    }
}
