using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.MewDock.Model;

/// <summary>Lifecycle events a tab/node can raise (port of NodeEventType).</summary>
internal enum NodeEventType
{
    Save,
    Resize,
    Visibility,
    Close,
}

/// <summary>
/// Base of the layout node tree (port of FlexLayout model/Node.ts). C#-idiomatic: field-like accessors are
/// properties, the original generic attribute dictionary is replaced by typed members on the subclasses, and the
/// IDraggable/IDropTarget interfaces become virtual members here (default no-op) overridden by the drop targets.
/// </summary>
internal abstract class Node
{
    private readonly List<Node> _children = new();
    private readonly Dictionary<NodeEventType, Action<object?>> _listeners = new();
    private string? _id;

    private protected Node(Model model)
    {
        Model = model;
    }

    /// <summary>Discriminator type: "row" / "tabset" / "tab" / "border".</summary>
    public abstract string Type { get; }

    public Model Model { get; }

    public Node? Parent { get; internal set; }

    public IReadOnlyList<Node> Children => _children;

    public Rect Rect { get; internal set; } = Rect.Empty;

    /// <summary>Transient view control hosting this node (set by the view layer; not serialized).</summary>
    public UIElement? View { get; set; }

    public string Path { get; internal set; } = "";

    /// <summary>The node's layout orientation: the root's from the model, otherwise the parent's flipped.</summary>
    public Orientation Orientation =>
        Parent is null
            ? (Model.IsRootOrientationVertical ? Orientation.Vertical : Orientation.Horizontal)
            : Parent.Orientation.Flip();

    public string LayoutId => GetLayout().LayoutId;

    /// <summary>Gets the id, generating and assigning one on first access if none was set (port of getId).</summary>
    public string GetId()
    {
        if (_id is not null)
        {
            return _id;
        }
        _id = Model.NextUniqueId();
        return _id;
    }

    internal void SetId(string id) => _id = id;

    /// <summary>The assigned id, or null if none has been generated yet (no side effect).</summary>
    internal string? IdOrNull => _id;

    public virtual bool IsCloseable()
    {
        foreach (var child in _children)
        {
            if (!child.IsCloseable())
            {
                return false;
            }
        }
        return true;
    }

    public virtual bool IsAllowedInWindow()
    {
        foreach (var child in _children)
        {
            if (!child.IsAllowedInWindow())
            {
                return false;
            }
        }
        return true;
    }

    internal virtual Layout GetLayout() => Parent is not null ? Parent.GetLayout() : Model.MainLayout;

    public void SetEventListener(NodeEventType evt, Action<object?> callback) => _listeners[evt] = callback;

    public void RemoveEventListener(NodeEventType evt) => _listeners.Remove(evt);

    internal void FireEvent(NodeEventType evt, object? parameters)
    {
        if (_listeners.TryGetValue(evt, out var callback))
        {
            callback(parameters);
        }
    }

    internal void ForEachNode(Action<Node, int> fn, int level)
    {
        fn(this, level);
        level++;
        foreach (var node in _children)
        {
            node.ForEachNode(fn, level);
        }
    }

    internal void SetPaths(string path)
    {
        int i = 0;
        foreach (var node in _children)
        {
            string newPath = path + node.Type switch
            {
                "row" => "/r" + i,
                "tabset" => "/ts" + i,
                "tab" => "/t" + i,
                _ => "",
            };
            node.Path = newPath;
            node.SetPaths(newPath);
            i++;
        }
    }

    /// <summary>Mutable child list for the model layer (subclasses restructure it directly during drop/tidy).</summary>
    private protected List<Node> ChildList => _children;

    internal DropInfo? FindDropTargetNode(string layoutId, Node dragNode, double x, double y)
    {
        DropInfo? rtn = null;
        if (Rect.ContainsInclusive(x, y))
        {
            var maximized = Model.GetMaximizedTabset(layoutId);
            if (maximized is not null)
            {
                rtn = maximized.CanDrop(dragNode, x, y);
            }
            else
            {
                rtn = CanDrop(dragNode, x, y);
                if (rtn is null && _children.Count != 0)
                {
                    foreach (var child in _children)
                    {
                        rtn = child.FindDropTargetNode(layoutId, dragNode, x, y);
                        if (rtn is not null)
                        {
                            break;
                        }
                    }
                }
            }
        }
        return rtn;
    }

    // Drop-target / draggable surface (original IDropTarget / IDraggable): default to non-target / non-draggable.
    internal virtual DropInfo? CanDrop(Node dragNode, double x, double y) => null;

    internal virtual void Drop(Node dragNode, DockLocation location, int index, bool? select = null) =>
        throw new NotSupportedException($"Node type '{Type}' is not a drop target.");

    internal int IndexOfChild(Node child) => _children.IndexOf(child);

    public virtual bool IsEnableDrop => false;

    public virtual bool IsEnableDivide => true;

    public virtual bool IsEnableDrag => false;

    public virtual bool IsEnableClose => true;

    public virtual string? Name => null;

    internal bool CanDockInto(Node dragNode, DropInfo? dropInfo)
    {
        if (dropInfo is not null)
        {
            if (dropInfo.Location == DockLocation.Center && dropInfo.Node.IsEnableDrop == false)
            {
                return false;
            }

            // Prevent a tabset with enableClose=false from docking into another tabset.
            if (dropInfo.Location == DockLocation.Center && dragNode.Type == "tabset" && dragNode.IsEnableClose == false)
            {
                return false;
            }

            if (dropInfo.Location != DockLocation.Center && dropInfo.Node.IsEnableDivide == false)
            {
                return false;
            }

            var onAllowDrop = Model.OnAllowDrop;
            if (onAllowDrop is not null)
            {
                return onAllowDrop(dragNode, dropInfo);
            }
        }
        return true;
    }

    internal int RemoveChild(Node childNode)
    {
        int pos = _children.IndexOf(childNode);
        if (pos != -1)
        {
            _children.RemoveAt(pos);
        }
        return pos;
    }

    internal int AddChild(Node childNode, int? pos = null)
    {
        if (pos is not null)
        {
            _children.Insert(pos.Value, childNode);
        }
        else
        {
            _children.Add(childNode);
            pos = _children.Count - 1;
        }
        childNode.Parent = this;
        return pos.Value;
    }

    internal void RemoveAll() => _children.Clear();
}
