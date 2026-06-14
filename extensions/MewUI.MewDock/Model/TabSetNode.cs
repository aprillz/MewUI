namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// A tab container with a header strip and a content area (port of FlexLayout model/TabSetNode.ts). Per-node
/// attribute overrides arrive with JSON (flags resolve to the model globals until then). The strip/content rects
/// are populated by the view during layout.
/// </summary>
internal sealed class TabSetNode : SizedNode
{
    internal TabSetNode(Model model) : base(model)
    {
        model.AddNode(this);
    }

    public override string Type => "tabset";

    /// <summary>Document group (top tabs, maximize) vs pane group (bottom tabs + caption); derived from the member
    /// tabs (homogeneous - the drop rules never mix the two), so the distinction travels with the tabs.</summary>
    public bool IsDocument => Children.Count == 0 || ((TabNode)Children[0]).IsDocument;

    /// <summary>Index of the selected tab, or -1 when none is selected.</summary>
    public int Selected { get; internal set; }

    public Rect ContentRect { get; internal set; } = Rect.Empty;

    public Rect TabStripRect { get; internal set; } = Rect.Empty;

    internal void SetSelected(int index) => Selected = index;

    internal void SetContentRect(Rect rect) => ContentRect = rect;

    internal void SetTabStripRect(Rect rect) => TabStripRect = rect;

    internal void Remove(TabNode node)
    {
        int removedIndex = RemoveChild(node);
        Model.Tidy();
        ModelUtils.AdjustSelectedIndex(this, removedIndex);
    }

    internal void Delete() => Parent?.RemoveChild(this);

    internal TabNode? GetSelectedNode() =>
        Selected != -1 && Selected < Children.Count ? (TabNode)Children[Selected] : null;

    public override bool IsEnableClose => Model.TabSetEnableClose;

    public override bool IsEnableDrop => Model.TabSetEnableDrop;

    public override bool IsEnableDivide => Model.TabSetEnableDivide;

    public override bool IsEnableDrag => Model.TabSetEnableDrag;

    public bool IsEnableDeleteWhenEmpty => Model.TabSetEnableDeleteWhenEmpty;

    public bool IsEnableMaximize => Model.TabSetEnableMaximize;

    public bool IsAutoSelectTab => Model.TabSetAutoSelectTab;

    public bool IsMaximized => ReferenceEquals(Model.GetMaximizedTabset(LayoutId), this);

    public bool IsActive => ReferenceEquals(Model.GetActiveTabset(LayoutId), this);

    public override bool IsCloseable()
    {
        bool closeable = IsEnableClose;
        if (closeable)
        {
            closeable = base.IsCloseable();
        }
        return closeable;
    }

    private double GetAttrMinWidth() => Model.TabSetMinWidth;

    private double GetAttrMinHeight() => Model.TabSetMinHeight;

    private double GetAttrMaxWidth() => Model.TabSetMaxWidth;

    private double GetAttrMaxHeight() => Model.TabSetMaxHeight;

    internal override void CalcMinMaxSize()
    {
        MinHeight = GetAttrMinHeight();
        MinWidth = GetAttrMinWidth();
        MaxHeight = GetAttrMaxHeight();
        MaxWidth = GetAttrMaxWidth();

        foreach (var child in Children)
        {
            var c = (TabNode)child;
            MinWidth = Math.Max(MinWidth, c.GetMinWidth());
            MinHeight = Math.Max(MinHeight, c.GetMinHeight());
            MaxWidth = Math.Min(MaxWidth, c.GetMaxWidth());
            MaxHeight = Math.Min(MaxHeight, c.GetMaxHeight());
        }

        MinHeight += TabStripRect.Height;
        MaxHeight += TabStripRect.Height;
    }

    internal bool CanMaximize()
    {
        if (!IsEnableMaximize)
        {
            return false;
        }
        // Always allow toggling off if already maximized.
        if (ReferenceEquals(Model.GetMaximizedTabset(LayoutId), this))
        {
            return true;
        }
        // Disable when this is the only tabset in the root row.
        if (ReferenceEquals(Parent, Model.GetRootRow(LayoutId)) && Model.GetRootRow(LayoutId).Children.Count == 1)
        {
            return false;
        }
        return true;
    }

    internal override DropInfo? CanDrop(Node dragNode, double x, double y)
    {
        // Panes and documents do not dock into each other.
        if (ModelUtils.IsPane(dragNode) != ModelUtils.IsPane(this))
        {
            return null;
        }

        DropInfo? dropInfo = null;
        var layout = GetLayout();

        if (ReferenceEquals(dragNode, this))
        {
            dropInfo = new DropInfo(this, TabStripRect, DockLocation.Center, -1, DropOutlineKind.Standard);
        }
        else if (LayoutId != Model.MainLayoutId && !Layout.CanDockToLayout(dragNode, layout))
        {
            return null;
        }
        else if (!IsDocument && TabStripRect.ContainsInclusive(x, y))
        {
            // A pane's bottom tab STRIP only joins (add a tab) - no position-based insert line. The content area below
            // still splits, which is how you place two panes side by side inside a dock.
            dropInfo = new DropInfo(this, DockLocation.Center.GetDockRect(Rect), DockLocation.Center, -1, DropOutlineKind.Standard);
        }
        else if (ContentRect.ContainsInclusive(x, y))
        {
            // Position-based docking: left/right/top/bottom split, or Center to join. Tools split too (side by side
            // inside a dock); only their strip (above) is join-only.
            var dockLocation = DockLocation.Center;
            if (Model.GetMaximizedTabset(LayoutId) is null)
            {
                dockLocation = DockLocationExtensions.GetLocation(ContentRect, x, y);
            }
            var outline = dockLocation.GetDockRect(Rect);
            dropInfo = new DropInfo(this, outline, dockLocation, -1, DropOutlineKind.Standard);
        }
        else if (TabStripRect.ContainsInclusive(x, y))
        {
            Rect r;
            double yy;
            double h;
            if (Children.Count == 0)
            {
                r = TabStripRect;
                yy = r.Y + 3;
                h = r.Height - 4;
                r = new Rect(r.X, r.Y, 2, r.Height);
            }
            else
            {
                r = ((TabNode)Children[0]).TabRect;
                yy = r.Y;
                h = r.Height;
                double p = TabStripRect.X;
                for (int i = 0; i < Children.Count; i++)
                {
                    // Tear-off: the dragged tab is hidden from the strip (its TabRect is stale), so skip it - the
                    // insert position is computed against the reflowed (visible) tabs only.
                    if (ReferenceEquals(Children[i], Model.DraggingNode))
                    {
                        continue;
                    }
                    r = ((TabNode)Children[i]).TabRect;
                    if (r.Y != yy)
                    {
                        yy = r.Y;
                        p = TabStripRect.X;
                    }
                    double childCenter = r.X + r.Width / 2;
                    if (p <= x && x < childCenter && r.Y < y && y < r.Bottom)
                    {
                        var outline = new Rect(r.X - 2, r.Y, 3, r.Height);
                        dropInfo = new DropInfo(this, outline, DockLocation.Center, i, DropOutlineKind.Standard);
                        break;
                    }
                    p = childCenter;
                }
            }

            if (dropInfo is null && r.Right < Rect.Right)
            {
                var outline = new Rect(r.Right - 2, yy, 3, h);
                dropInfo = new DropInfo(this, outline, DockLocation.Center, Children.Count, DropOutlineKind.Standard);
            }
        }

        if (!dragNode.CanDockInto(dragNode, dropInfo))
        {
            return null;
        }

        return dropInfo;
    }

    internal override void Drop(Node dragNode, DockLocation location, int index, bool? select = null)
    {
        var dockLocation = location;

        if (ReferenceEquals(this, dragNode))
        {
            return; // dock back to itself
        }

        var dragParent = dragNode.Parent;
        int fromIndex = 0;
        if (dragParent is not null)
        {
            fromIndex = dragParent.RemoveChild(dragNode);
            if (dragParent is BorderNode sourceBorder && sourceBorder.Selected == fromIndex)
            {
                sourceBorder.SetSelected(-1);
            }
            else
            {
                ModelUtils.AdjustSelectedIndex(dragParent, fromIndex);
            }
        }

        if (dragNode is TabNode && ReferenceEquals(dragParent, this) && fromIndex < index && index > 0)
        {
            index--;
        }

        if (dockLocation == DockLocation.Center)
        {
            int insertPos = index == -1 ? Children.Count : index;
            if (dragNode is TabNode tabNode)
            {
                AddChild(tabNode, insertPos);
                if (select == true || (select != false && IsAutoSelectTab))
                {
                    SetSelected(insertPos);
                }
            }
            else if (dragNode is RowNode rowNode)
            {
                int pos = insertPos;
                rowNode.ForEachNode((child, level) =>
                {
                    if (child is TabNode tabChild)
                    {
                        AddChild(tabChild, pos);
                        pos++;
                    }
                }, 0);
            }
            else
            {
                int pos = insertPos;
                foreach (var child in dragNode.Children.ToList())
                {
                    AddChild(child, pos);
                    pos++;
                }
                if (Selected == -1 && Children.Count > 0)
                {
                    SetSelected(0);
                }
            }
            Model.SetActiveTabset(this, Parent!.LayoutId);
        }
        else
        {
            SizedNode moveNode;
            if (dragNode is TabNode draggedTab)
            {
                // TODO Phase 1: apply onCreateTabSet attributes to the new tabset.
                var newTabSet = new TabSetNode(Model);
                newTabSet.AddChild(draggedTab);
                moveNode = newTabSet;
            }
            else if (dragNode is RowNode draggedRow)
            {
                var parentForCheck = (RowNode)Parent!;
                // Turn round if same orientation unless docking the opposite direction.
                if (draggedRow.Orientation == parentForCheck.Orientation &&
                    (location.GetOrientation() == parentForCheck.Orientation || location == DockLocation.Center))
                {
                    var wrap = new RowNode(Model);
                    wrap.AddChild(draggedRow);
                    moveNode = wrap;
                }
                else
                {
                    moveNode = draggedRow;
                }
            }
            else
            {
                moveNode = (TabSetNode)dragNode;
            }

            var parentRow = Parent!;
            int pos = parentRow.IndexOfChild(this);

            if (parentRow.Orientation == dockLocation.GetOrientation())
            {
                moveNode.Weight = Weight / 2;
                Weight /= 2;
                parentRow.AddChild(moveNode, pos + dockLocation.IndexPlus());
            }
            else
            {
                // Create a new row to host the new tabset (it runs in the opposite direction).
                var newRow = new RowNode(Model);
                newRow.Weight = Weight;
                newRow.AddChild(this);
                Weight = 50;
                moveNode.Weight = 50;
                newRow.AddChild(moveNode, dockLocation.IndexPlus());
                parentRow.RemoveChild(this);
                parentRow.AddChild(newRow, pos);
            }

            if (moveNode is TabSetNode movedTabSet)
            {
                Model.SetActiveTabset(movedTabSet, LayoutId);
            }
        }

        Model.Tidy();
    }
}
