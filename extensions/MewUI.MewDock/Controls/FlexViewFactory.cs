using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>Builds the MewUI control hosting a layout node (the view counterpart of the node tree).</summary>
internal static class FlexViewFactory
{
    internal static UIElement BuildNodeView(Node node, FlexViewContext context) => node switch
    {
        RowNode row => new FlexRowView(row, context),
        TabSetNode tabSet => new FlexTabSetView(tabSet, context),
        _ => throw new InvalidOperationException($"Cannot build a view for node type '{node.Type}'."),
    };
}
