namespace Aprillz.MewUI.MewDock.Model;

/// <summary>The kind of space a <see cref="Layout"/> occupies (port of ILayoutType). <see cref="Dock"/> is set only
/// by the feature layer's Layout subclass (an edge-pinned sub-layout); the base model never creates one.</summary>
public enum LayoutType
{
    Window,
    Float,
    Tab,
    Dock,
}

/// <summary>
/// A window / float / tab layout space that hosts a root row (port of FlexLayout model/Layout.ts).
/// Unsealed so the feature layer can derive an edge-pinned variant carrying its own placement data.
/// </summary>
internal class Layout
{
    private readonly string _layoutId;

    internal Layout(string layoutId, LayoutType type, Rect rect)
    {
        _layoutId = layoutId;
        Type = type;
        Rect = rect;
    }

    public string LayoutId => _layoutId;

    internal LayoutType Type { get; set; }

    internal Rect Rect { get; set; }

    internal RowNode? RootRow { get; private set; }

    internal TabSetNode? MaximizedTabSet { get; set; }

    internal TabSetNode? ActiveTabSet { get; set; }

    internal bool IsMainLayout => _layoutId == Model.MainLayoutId;

    internal void SetRootRow(RowNode? rowNode)
    {
        rowNode?.SetLayout(this);
        RootRow = rowNode;
    }

    internal void VisitNodes(Action<Node, int> fn) => RootRow?.ForEachNode(fn, 0);

    /// <summary>Whether a node may dock into the given layout space (sub-layout tab rules deferred to Phase 5).</summary>
    internal static bool CanDockToLayout(Node node, Layout layout) => layout.Type switch
    {
        LayoutType.Window => node.IsAllowedInWindow(),
        _ => true,
    };
}
