namespace Aprillz.MewUI.MewDock.Model;

/// <summary>The kind of space a <see cref="Layout"/> occupies (port of ILayoutType).</summary>
public enum LayoutType
{
    /// <summary>The main window, or a popped-out window.</summary>
    Window,

    /// <summary>A floating layout.</summary>
    Float,

    /// <summary>A sub-layout hosted inside a tab.</summary>
    Tab,

    /// <summary>An edge-pinned tool dock.</summary>
    Dock,
}

/// <summary>
/// A window / float / tab layout space that hosts a root row (port of FlexLayout model/Layout.ts).
/// Unsealed so the feature layer can derive an edge-pinned variant carrying its own placement data.
/// </summary>
internal class Layout
{
    private readonly string _layoutId;
    private TabSetNode? _maximizedTabSet;
    private TabSetNode? _activeTabSet;

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

    /// <summary>Raised after the root row was replaced.</summary>
    internal event Action<Layout>? RootRowChanged;

    /// <summary>Raised after the maximized tabset changed.</summary>
    internal event Action<Layout>? MaximizedChanged;

    /// <summary>Raised after the active tabset changed.</summary>
    internal event Action<Layout>? ActiveChanged;

    internal TabSetNode? MaximizedTabSet
    {
        get => _maximizedTabSet;
        set
        {
            if (!ReferenceEquals(value, _maximizedTabSet))
            {
                _maximizedTabSet = value;
                MaximizedChanged?.Invoke(this);
            }
        }
    }

    internal TabSetNode? ActiveTabSet
    {
        get => _activeTabSet;
        set
        {
            if (!ReferenceEquals(value, _activeTabSet))
            {
                _activeTabSet = value;
                ActiveChanged?.Invoke(this);
            }
        }
    }

    internal bool IsMainLayout => _layoutId == Model.MainLayoutId;

    internal void SetRootRow(RowNode? rowNode)
    {
        rowNode?.SetLayout(this);
        if (!ReferenceEquals(rowNode, RootRow))
        {
            RootRow = rowNode;
            RootRowChanged?.Invoke(this);
        }
    }

    internal void VisitNodes(Action<Node, int> fn) => RootRow?.ForEachNode(fn, 0);

    /// <summary>Whether a node may dock into the given layout space (sub-layout tab rules deferred to Phase 5).</summary>
    internal static bool CanDockToLayout(Node node, Layout layout) => layout.Type switch
    {
        LayoutType.Window => node.IsAllowedInWindow(),
        _ => true,
    };
}
