using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

using DockModel = Aprillz.MewUI.MewDock.Model.Model;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>A tool-group caption bar hosted at the top of a Tool tabset. The Extended layer supplies it; the faithful
/// tabset view only hosts it and asks it to refresh on selection changes (no dependency on the Extended type).</summary>
internal interface IToolHeader
{
    void Refresh();
}

/// <summary>A view made for one layout node; it follows that node until the node leaves the model.</summary>
internal interface INodeView
{
    Node Node { get; }

    /// <summary>Stops following the node and the model; the view is not used again.</summary>
    void Release();
}

/// <summary>
/// What the view layer shares across one layout (the main window and its popouts): the factories for tab content,
/// tab headers, border views, tool captions and menus, and the registry of the views made for layout nodes. A node has
/// one view for as long as it is in the model, wherever in the tree it moves.
/// </summary>
internal sealed class FlexViewContext
{
    private readonly Dictionary<Node, INodeView> _views = new();
    private readonly Dictionary<TabNode, UIElement?> _headers = new();
    private readonly Func<TabNode, UIElement?>? _header;
    private readonly Action<TabNode>? _requestClose;

    public FlexViewContext(
        Func<TabNode, PaneHost?> host,
        Func<TabNode, UIElement?>? header = null,
        Func<BorderNode, FlexViewContext, FlexBorderBar>? borderView = null,
        Func<TabSetNode, UIElement?>? toolHeader = null,
        Action<TabNode, ContextMenu, CommandScope>? configureTabMenu = null,
        Action<TabSetNode, ContextMenu, CommandScope>? configureGroupMenu = null,
        Action<TabNode>? requestClose = null)
    {
        Host = host;
        _requestClose = requestClose;
        _header = header;
        BorderView = borderView;
        ToolHeader = toolHeader;
        ConfigureTabMenu = configureTabMenu;
        ConfigureGroupMenu = configureGroupMenu;
    }

    /// <summary>The host holding a tab's content for the tab's whole life.</summary>
    public Func<TabNode, PaneHost?> Host { get; }

    public Func<BorderNode, FlexViewContext, FlexBorderBar>? BorderView { get; }

    public Func<TabSetNode, UIElement?>? ToolHeader { get; }

    public Action<TabNode, ContextMenu, CommandScope>? ConfigureTabMenu { get; }

    public Action<TabSetNode, ContextMenu, CommandScope>? ConfigureGroupMenu { get; }

    /// <summary>Closes a tab the way the user asked from the dock's controls: the host may keep it open.</summary>
    public void Close(TabNode tab)
    {
        if (_requestClose is not null)
        {
            _requestClose(tab);
        }
        else if (tab.IsEnableClose)
        {
            tab.Model.DoAction(DockAction.DeleteTab(tab.GetId()));
        }
    }

    /// <summary>The host-built header of a tab, made once and handed from one tab button to the next as the tab moves.</summary>
    public UIElement? HeaderFor(TabNode tab)
    {
        if (_header is null)
        {
            return null;
        }
        if (!_headers.TryGetValue(tab, out var header))
        {
            header = _header(tab);
            _headers[tab] = header;
        }
        return header;
    }

    /// <summary>Takes a header out of the tab button that showed it, so the tab's next button can show it.</summary>
    public static void DetachHeader(UIElement header)
    {
        switch (header.Parent)
        {
            case Panel panel:
                panel.Remove(header);
                break;
            case RotationDecorator rotation when ReferenceEquals(rotation.Child, header):
                rotation.Child = null;
                break;
            case ContentControl owner when ReferenceEquals(owner.Content, header):
                owner.Content = null;
                break;
        }
    }

    /// <summary>The view of a row or tabset node, made on first use.</summary>
    public UIElement ViewFor(Node node)
    {
        if (!_views.TryGetValue(node, out var view))
        {
            view = FlexViewFactory.BuildNodeView(node, this);
            _views[node] = view;
        }
        return (UIElement)view;
    }

    /// <summary>
    /// Releases the views and headers of nodes that are no longer in <paramref name="model"/>. Called once an action is
    /// done, so a node that only passed out of the tree while it moved keeps its view.
    /// </summary>
    public void Sweep(DockModel model)
    {
        List<Node>? gone = null;
        foreach (var node in _views.Keys)
        {
            if (!ReferenceEquals(model.GetNodeById(node.GetId()), node))
            {
                (gone ??= new List<Node>()).Add(node);
            }
        }
        if (gone is not null)
        {
            foreach (var node in gone)
            {
                var view = _views[node];
                _views.Remove(node);
                view.Release();
                if (view is UIElement element && element.Parent is Panel panel)
                {
                    panel.Remove(element);
                }
            }
        }

        List<TabNode>? goneTabs = null;
        foreach (var tab in _headers.Keys)
        {
            if (!ReferenceEquals(model.GetNodeById(tab.GetId()), tab))
            {
                (goneTabs ??= new List<TabNode>()).Add(tab);
            }
        }
        if (goneTabs is not null)
        {
            foreach (var tab in goneTabs)
            {
                _headers.Remove(tab);
            }
        }
    }

    /// <summary>Releases every view; the layout they showed is being replaced.</summary>
    public void ReleaseAll()
    {
        foreach (var view in _views.Values.ToList())
        {
            view.Release();
        }
        _views.Clear();
        _headers.Clear();
    }
}
