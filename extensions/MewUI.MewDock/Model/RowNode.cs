namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// A splittable row/column container of tabsets and child rows (port of FlexLayout model/RowNode.ts).
/// Sizing/normalize ported; splitter drag math, canDrop/drop and tidy arrive in the next Phase 1 step.
/// </summary>
internal sealed class RowNode : SizedNode
{
    private Layout? _layout;

    internal RowNode(Model model) : base(model)
    {
        NormalizeWeights();
        model.AddNode(this);
    }

    public override string Type => "row";

    public override bool IsEnableDrop => true;

    /// <summary>Sub-layout root rows carry an explicit layout; otherwise the parent chain resolves it.</summary>
    internal void SetLayout(Layout layout) => _layout = layout;

    internal override Layout GetLayout() => _layout ?? base.GetLayout();

    internal override void CalcMinMaxSize()
    {
        MinHeight = ModelDefaults.Min;
        MinWidth = ModelDefaults.Min;
        MaxHeight = ModelDefaults.Max;
        MaxWidth = ModelDefaults.Max;

        bool first = true;
        foreach (var child in Children)
        {
            var c = (SizedNode)child;
            c.CalcMinMaxSize();
            double splitterSize = Model.SplitterSize;

            if (Orientation == Orientation.Vertical)
            {
                MinHeight += c.MinHeight;
                MaxHeight += c.MaxHeight;
                if (!first)
                {
                    MinHeight += splitterSize;
                    MaxHeight += splitterSize;
                }
                MinWidth = Math.Max(MinWidth, c.MinWidth);
                MaxWidth = Math.Min(MaxWidth, c.MaxWidth);
            }
            else
            {
                MinWidth += c.MinWidth;
                MaxWidth += c.MaxWidth;
                if (!first)
                {
                    MinWidth += splitterSize;
                    MaxWidth += splitterSize;
                }
                MinHeight = Math.Max(MinHeight, c.MinHeight);
                MaxHeight = Math.Min(MaxHeight, c.MaxHeight);
            }
            first = false;
        }
    }

    /// <summary>The allowable [min, max] travel (in px) for the splitter before child <paramref name="index"/>.</summary>
    internal (double Min, double Max) GetSplitterBounds(int index)
    {
        bool h = Orientation == Orientation.Horizontal;
        var c = Children;
        double ss = Model.SplitterSize;
        var fr = c[0].Rect;
        var lr = c[c.Count - 1].Rect;
        double p0 = h ? fr.X : fr.Y;
        double p1 = h ? lr.Right : lr.Bottom;
        double q0 = h ? fr.X : fr.Y;
        double q1 = h ? lr.Right : lr.Bottom;

        for (int i = 0; i < index; i++)
        {
            var n = (SizedNode)c[i];
            p0 += h ? n.MinWidth : n.MinHeight;
            q0 += h ? n.MaxWidth : n.MaxHeight;
            if (i > 0)
            {
                p0 += ss;
                q0 += ss;
            }
        }

        for (int i = c.Count - 1; i >= index; i--)
        {
            var n = (SizedNode)c[i];
            p1 -= (h ? n.MinWidth : n.MinHeight) + ss;
            q1 -= (h ? n.MaxWidth : n.MaxHeight) + ss;
        }

        return (Math.Max(q1, p0), Math.Min(q0, p1));
    }

    /// <summary>The per-child sizes, their sum and the splitter's rest position at the start of a drag.</summary>
    internal (double[] InitialSizes, double Sum, double StartPosition) GetSplitterInitials(int index)
    {
        bool h = Orientation == Orientation.Horizontal;
        var c = Children;
        double ss = Model.SplitterSize;
        var initialSizes = new double[c.Count];
        double sum = 0;

        for (int i = 0; i < c.Count; i++)
        {
            var r = c[i].Rect;
            double s = h ? r.Width : r.Height;
            initialSizes[i] = s;
            sum += s;
        }

        var startRect = c[index].Rect;
        double startPosition = (h ? startRect.X : startRect.Y) - ss;

        return (initialSizes, sum, startPosition);
    }

    /// <summary>Computes the new child weights for a splitter dragged to <paramref name="splitterPos"/>.</summary>
    internal double[] CalculateSplit(int index, double splitterPos, double[] initialSizes, double sum, double startPosition)
    {
        bool h = Orientation == Orientation.Horizontal;
        var c = Children;
        var sn = (SizedNode)c[index];
        double smax = h ? sn.MaxWidth : sn.MaxHeight;

        var sizes = (double[])initialSizes.Clone();

        if (splitterPos < startPosition) // moved toward the start
        {
            double shift = startPosition - splitterPos;
            double altShift = 0;
            if (sizes[index] + shift > smax)
            {
                altShift = sizes[index] + shift - smax;
                sizes[index] = smax;
            }
            else
            {
                sizes[index] += shift;
            }

            for (int i = index - 1; i >= 0; i--)
            {
                var n = (SizedNode)c[i];
                double m = h ? n.MinWidth : n.MinHeight;
                if (sizes[i] - shift > m)
                {
                    sizes[i] -= shift;
                    break;
                }
                else
                {
                    shift -= sizes[i] - m;
                    sizes[i] = m;
                }
            }

            for (int i = index + 1; i < c.Count; i++)
            {
                var n = (SizedNode)c[i];
                double m = h ? n.MaxWidth : n.MaxHeight;
                if (sizes[i] + altShift < m)
                {
                    sizes[i] += altShift;
                    break;
                }
                else
                {
                    altShift -= m - sizes[i];
                    sizes[i] = m;
                }
            }
        }
        else
        {
            double shift = splitterPos - startPosition;
            double altShift = 0;
            if (sizes[index - 1] + shift > smax)
            {
                altShift = sizes[index - 1] + shift - smax;
                sizes[index - 1] = smax;
            }
            else
            {
                sizes[index - 1] += shift;
            }

            for (int i = index; i < c.Count; i++)
            {
                var n = (SizedNode)c[i];
                double m = h ? n.MinWidth : n.MinHeight;
                if (sizes[i] - shift > m)
                {
                    sizes[i] -= shift;
                    break;
                }
                else
                {
                    shift -= sizes[i] - m;
                    sizes[i] = m;
                }
            }

            for (int i = index - 1; i >= 0; i--)
            {
                var n = (SizedNode)c[i];
                double m = h ? n.MaxWidth : n.MaxHeight;
                if (sizes[i] + altShift < m)
                {
                    sizes[i] += altShift;
                    break;
                }
                else
                {
                    altShift -= m - sizes[i];
                    sizes[i] = m;
                }
            }
        }

        // 0.1 keeps a weight from ever reaching zero.
        var weights = new double[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            weights[i] = Math.Max(0.1, sizes[i]) * 100 / sum;
        }
        return weights;
    }

    internal override DropInfo? CanDrop(Node dragNode, double x, double y)
    {
        // Panes and documents do not dock into each other (a pane cannot create/join a document row, or vice versa).
        if (ModelUtils.IsPane(dragNode) != ModelUtils.IsPane(this))
        {
            return null;
        }

        double yy = y - Rect.Y;
        double xx = x - Rect.X;
        double w = Rect.Width;
        double h = Rect.Height;
        const double margin = 10; // height of the edge rect
        double half = Model.EnableEdgeDockIndicators ? 50 : 9999; // half width of the edge rect
        DropInfo? dropInfo = null;

        var layout = GetLayout();
        if (LayoutId != Model.MainLayoutId && !Layout.CanDockToLayout(dragNode, layout))
        {
            return null;
        }

        if (Model.EnableEdgeDock && Parent is null)
        {
            if (x < Rect.X + margin && yy > h / 2 - half && yy < h / 2 + half)
            {
                var dock = DockLocation.Left.GetDockRect(Rect);
                var outline = new Rect(dock.X, dock.Y, dock.Width / 2, dock.Height);
                dropInfo = new DropInfo(this, outline, DockLocation.Left, -1, DropOutlineKind.Edge);
            }
            else if (x > Rect.Right - margin && yy > h / 2 - half && yy < h / 2 + half)
            {
                var dock = DockLocation.Right.GetDockRect(Rect);
                double newW = dock.Width / 2;
                var outline = new Rect(dock.X + newW, dock.Y, newW, dock.Height);
                dropInfo = new DropInfo(this, outline, DockLocation.Right, -1, DropOutlineKind.Edge);
            }
            else if (y < Rect.Y + margin && xx > w / 2 - half && xx < w / 2 + half)
            {
                var dock = DockLocation.Top.GetDockRect(Rect);
                var outline = new Rect(dock.X, dock.Y, dock.Width, dock.Height / 2);
                dropInfo = new DropInfo(this, outline, DockLocation.Top, -1, DropOutlineKind.Edge);
            }
            else if (y > Rect.Bottom - margin && xx > w / 2 - half && xx < w / 2 + half)
            {
                var dock = DockLocation.Bottom.GetDockRect(Rect);
                double newH = dock.Height / 2;
                var outline = new Rect(dock.X, dock.Y + newH, dock.Width, newH);
                dropInfo = new DropInfo(this, outline, DockLocation.Bottom, -1, DropOutlineKind.Edge);
            }

            if (dropInfo is not null && !dragNode.CanDockInto(dragNode, dropInfo))
            {
                return null;
            }
        }

        return dropInfo;
    }

    internal override void Drop(Node dragNode, DockLocation location, int index, bool? select = null)
    {
        var dockLocation = location;
        var parent = dragNode.Parent;
        parent?.RemoveChild(dragNode);

        if (parent is TabSetNode parentTabSet)
        {
            parentTabSet.SetSelected(0);
        }
        if (parent is BorderNode parentBorder)
        {
            parentBorder.SetSelected(-1);
        }

        SizedNode node;
        if (dragNode is TabSetNode or RowNode)
        {
            node = (SizedNode)dragNode;
            // Turn the dragged row round if it has the same orientation, unless docking the opposite direction.
            if (node is RowNode draggedRow && draggedRow.Orientation == Orientation &&
                (location.GetOrientation() == Orientation || location == DockLocation.Center))
            {
                var wrapper = new RowNode(Model);
                wrapper.AddChild(dragNode);
                node = wrapper;
            }
        }
        else
        {
            // TODO Phase 1: apply onCreateTabSet attributes to the new tabset.
            var tabSet = new TabSetNode(Model);
            tabSet.AddChild(dragNode);
            node = tabSet;
        }

        double size = 0;
        foreach (var child in Children)
        {
            size += ((SizedNode)child).Weight;
        }
        if (size == 0)
        {
            size = 100;
        }
        node.Weight = size / 3;

        bool horz = !Model.IsRootOrientationVertical;
        if (dockLocation == DockLocation.Center)
        {
            AddChild(node, index == -1 ? Children.Count : index);
        }
        else if ((horz && dockLocation == DockLocation.Left) || (!horz && dockLocation == DockLocation.Top))
        {
            AddChild(node, 0);
        }
        else if ((horz && dockLocation == DockLocation.Right) || (!horz && dockLocation == DockLocation.Bottom))
        {
            AddChild(node);
        }
        else if ((horz && dockLocation == DockLocation.Top) || (!horz && dockLocation == DockLocation.Left))
        {
            var vrow = new RowNode(Model);
            var hrow = new RowNode(Model);
            hrow.Weight = 75;
            node.Weight = 25;
            foreach (var child in Children.ToList())
            {
                hrow.AddChild(child);
            }
            RemoveAll();
            vrow.AddChild(node);
            vrow.AddChild(hrow);
            AddChild(vrow);
        }
        else if ((horz && dockLocation == DockLocation.Bottom) || (!horz && dockLocation == DockLocation.Right))
        {
            var vrow = new RowNode(Model);
            var hrow = new RowNode(Model);
            hrow.Weight = 75;
            node.Weight = 25;
            foreach (var child in Children.ToList())
            {
                hrow.AddChild(child);
            }
            RemoveAll();
            vrow.AddChild(hrow);
            vrow.AddChild(node);
            AddChild(vrow);
        }

        if (node is TabSetNode droppedTabSet)
        {
            Model.SetActiveTabset(droppedTabSet, LayoutId);
        }

        Model.Tidy();
    }

    internal void Tidy()
    {
        int i = 0;
        while (i < Children.Count)
        {
            var child = Children[i];
            if (child is RowNode childRow)
            {
                childRow.Tidy();
                var childChildren = childRow.Children;
                if (childChildren.Count == 0)
                {
                    RemoveChild(childRow);
                }
                else if (childChildren.Count == 1)
                {
                    // Hoist the single grandchild (or its children) up to this level.
                    var subchild = childChildren[0];
                    RemoveChild(childRow);
                    if (subchild is RowNode subRow)
                    {
                        double subChildrenTotal = 0;
                        foreach (var ssc in subRow.Children)
                        {
                            subChildrenTotal += ((SizedNode)ssc).Weight;
                        }
                        var snapshot = subRow.Children.ToArray();
                        for (int j = 0; j < snapshot.Length; j++)
                        {
                            var subsubChild = (SizedNode)snapshot[j];
                            subsubChild.Weight = childRow.Weight * subsubChild.Weight / subChildrenTotal;
                            AddChild(subsubChild, i + j);
                        }
                    }
                    else
                    {
                        ((SizedNode)subchild).Weight = childRow.Weight;
                        AddChild(subchild, i);
                    }
                }
                else
                {
                    i++;
                }
            }
            else if (child is TabSetNode childTabSet && childTabSet.Children.Count == 0)
            {
                if (childTabSet.IsEnableDeleteWhenEmpty && childTabSet.IsEnableClose)
                {
                    RemoveChild(childTabSet);
                    if (ReferenceEquals(childTabSet, Model.GetMaximizedTabset(LayoutId)))
                    {
                        Model.SetMaximizedTabset(null, LayoutId);
                    }
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        // Put a fresh tabset into an emptied root (or drop an emptied sub-layout).
        if (ReferenceEquals(this, Model.GetRootRow(LayoutId)) && Children.Count == 0)
        {
            var layout = GetLayout();
            if (layout.Type != LayoutType.Tab && LayoutId != Model.MainLayoutId)
            {
                Model.Layouts.Remove(LayoutId);
            }
            else
            {
                // TODO Phase 1: apply onCreateTabSet attributes.
                var child = new TabSetNode(Model);
                child.SetSelected(-1);
                Model.SetActiveTabset(child, LayoutId);
                AddChild(child);
            }
        }
    }

    // NOTE: flex-grow cannot have values < 1 otherwise it will not fill the parent, so weights are normalized.
    internal void NormalizeWeights()
    {
        double sum = 0;
        foreach (var n in Children)
        {
            sum += ((SizedNode)n).Weight;
        }
        if (sum == 0)
        {
            sum = 1;
        }
        foreach (var n in Children)
        {
            var node = (SizedNode)n;
            node.Weight = Math.Max(0.001, 100 * node.Weight / sum);
        }
    }
}
