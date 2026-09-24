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
        Func<TabNode, PaneHost?> host,
        Func<TabNode, UIElement?>? header = null,
        Action<TabNode, ContextMenu, CommandScope>? configureTabMenu = null,
        Action<TabSetNode, ContextMenu, CommandScope>? configureGroupMenu = null,
        Action<TabNode>? requestClose = null)
    {
        // No flags anywhere: the model behavior comes from the ExtendedDockModel type, the view behavior from the
        // Extended view types this factory wires (ExtendedLayoutView / ExtendedBorderBar / ExtendedBorderButton).
        FlexViewContext? context = null;
        context = new FlexViewContext(host, header,
            (border, ctx) => new ExtendedBorderBar(border, ctx),
            tabSet => DockCaption.ForTool(tabSet, context!.Close),
            configureTabMenu,
            configureGroupMenu,
            requestClose);
        var view = new ExtendedLayoutView(model, context);
        view.Initialize();
        return view;
    }
}
