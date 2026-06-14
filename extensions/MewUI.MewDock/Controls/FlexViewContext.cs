using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>A tool-group caption bar hosted at the top of a Tool tabset. The Extended layer supplies it; the faithful
/// tabset view only hosts it and asks it to refresh on selection changes (no dependency on the Extended type).</summary>
internal interface IToolHeader
{
    void Refresh();
}

/// <summary>
/// The factories the view layer threads through the tree: <see cref="Content"/> builds the pane content for a tab,
/// <see cref="Header"/> builds custom tab-button header content, <see cref="BorderView"/> swaps the border view, and
/// <see cref="ToolHeader"/> builds the caption bar for a Tool tabset (Extended docking).
/// </summary>
internal sealed record FlexViewContext(
    Func<TabNode, UIElement?> Content,
    Func<TabNode, UIElement?>? Header = null,
    Func<BorderNode, FlexViewContext, FlexBorderBar>? BorderView = null,
    Func<TabSetNode, UIElement?>? ToolHeader = null,
    Action<TabNode, ContextMenu>? ConfigureTabMenu = null,
    Action<TabSetNode, ContextMenu>? ConfigureGroupMenu = null);
