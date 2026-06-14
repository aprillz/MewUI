using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock.Extended;

/// <summary>
/// Entry point for the Extended (Visual Studio style) docking layer. Builds a <see cref="FlexLayoutView"/> whose
/// borders use <see cref="ExtendedBorderBar"/> (caption header + bottom tab strip) instead of the faithful edge
/// strip, while everything else (model, tabsets, drag-drop) stays the standard MewDock port.
/// </summary>
internal static class ExtendedDock
{
    public static FlexLayoutView CreateView(
        ExtendedDockModel model,
        Func<TabNode, UIElement?> content,
        Func<TabNode, UIElement?>? header = null,
        Action<TabNode, ContextMenu>? configureTabMenu = null,
        Action<TabSetNode, ContextMenu>? configureGroupMenu = null)
    {
        // No flags anywhere: the model behavior comes from the ExtendedDockModel type, the view behavior from the
        // Extended view types this factory wires (ExtendedLayoutView / ExtendedBorderBar / ExtendedBorderButton).
        var context = new FlexViewContext(content, header,
            (border, ctx) => new ExtendedBorderBar(border, ctx),
            tabSet => DockCaption.ForTool(tabSet),
            configureTabMenu,
            configureGroupMenu);
        return new ExtendedLayoutView(model, context);
    }
}
