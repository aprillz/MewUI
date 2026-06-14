namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// Holds the up-to-four edge borders (port of FlexLayout model/BorderSet.ts). Not part of the main node tree.
/// </summary>
internal sealed class BorderSet
{
    private readonly List<BorderNode> _borders = new();
    private readonly Dictionary<DockLocation, BorderNode> _borderMap = new();

    internal BorderSet(Model model)
    {
    }

    public IReadOnlyList<BorderNode> Borders => _borders;

    internal IReadOnlyDictionary<DockLocation, BorderNode> BorderMap => _borderMap;

    internal bool LayoutHorizontal { get; } = true;

    internal void Add(BorderNode border)
    {
        _borders.Add(border);
        _borderMap[border.Location] = border;
    }

    internal void ForEachNode(Action<Node, int> fn)
    {
        foreach (var borderNode in _borders)
        {
            fn(borderNode, 0);
            foreach (var node in borderNode.Children)
            {
                node.ForEachNode(fn, 1);
            }
        }
    }

    internal void SetPaths()
    {
        foreach (var borderNode in _borders)
        {
            string path = "/border/" + borderNode.Location.GetName();
            borderNode.Path = path;
            int i = 0;
            foreach (var node in borderNode.Children)
            {
                node.Path = path + "/t" + i;
                i++;
            }
        }
    }

    internal DropInfo? FindDropTargetNode(Node dragNode, double x, double y)
    {
        foreach (var border in _borders)
        {
            if (border.IsShowing)
            {
                var dropInfo = border.CanDrop(dragNode, x, y);
                if (dropInfo is not null)
                {
                    return dropInfo;
                }
            }
        }
        return null;
    }
}
