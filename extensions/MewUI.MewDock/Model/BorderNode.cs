namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// An auto-hide edge panel holding tabs (port of FlexLayout model/BorderNode.ts). Lives in a
/// <see cref="BorderSet"/>, not the main tree. Per-node attribute overrides arrive with JSON (size/flags resolve
/// to the model globals until then). The header/content rects are populated by the view during layout.
/// </summary>
internal sealed class BorderNode : Node
{
    internal BorderNode(Model model, DockLocation location) : base(model)
    {
        Location = location;
        SetId($"border_{location.GetName()}");
        Selected = -1;
        model.AddNode(this);
    }

    public override string Type => "border";

    public DockLocation Location { get; }

    /// <summary>Index of the visible tab, or -1 when the border is collapsed.</summary>
    public int Selected { get; internal set; }

    /// <summary>Whether this border is shown at all.</summary>
    public bool IsShowing { get; internal set; } = true;

    /// <summary>This border's own panel size (port of the per-border <c>size</c> attribute); null falls back to
    /// the global <see cref="Model.BorderSize"/>. Kept per-border so resizing one border does not affect others.</summary>
    internal double? Size { get; set; }

    /// <summary>Per-border <c>enableAutoHide</c> override (null inherits the global <see cref="Model.BorderEnableAutoHide"/>).</summary>
    internal bool? EnableAutoHideOverride { get; set; }

    /// <summary>When true, this border is hidden while it has no tabs (revealed only during a drag near its edge).
    /// Port of FlexLayout <c>isAutoHide()</c> - NOT a focus/blur collapse; the panel never closes on losing focus.</summary>
    public bool IsAutoHide => EnableAutoHideOverride ?? Model.BorderEnableAutoHide;

    public Rect TabHeaderRect { get; internal set; } = Rect.Empty;

    public Rect ContentRect { get; internal set; } = Rect.Empty;

    internal void SetSelected(int index) => Selected = index;

    internal void SetTabHeaderRect(Rect rect) => TabHeaderRect = rect;

    internal void SetContentRect(Rect rect) => ContentRect = rect;

    public bool IsHorizontal() => Location.GetOrientation() == Orientation.Horizontal;

    public Orientation GetOrientation() => Location.GetOrientation();

    public override bool IsEnableDrop => Model.BorderEnableDrop;

    public TabNode? GetSelectedNode() =>
        Selected != -1 && Selected < Children.Count ? (TabNode)Children[Selected] : null;

    public double GetSize()
    {
        double defaultSize = Size ?? Model.BorderSize;
        if (Selected == -1)
        {
            return defaultSize;
        }
        var tabNode = (TabNode)Children[Selected];
        double tabBorderSize = IsHorizontal() ? tabNode.BorderWidth : tabNode.BorderHeight;
        return tabBorderSize == -1 ? defaultSize : tabBorderSize;
    }

    public double GetMinSize()
    {
        double min = Model.BorderMinSize;
        if (GetSelectedNode() is TabNode selectedNode)
        {
            double nodeMin = IsHorizontal() ? selectedNode.GetMinWidth() : selectedNode.GetMinHeight();
            min = Math.Max(min, nodeMin);
        }
        return min;
    }

    public double GetMaxSize()
    {
        double max = Model.BorderMaxSize;
        if (GetSelectedNode() is TabNode selectedNode)
        {
            double nodeMax = IsHorizontal() ? selectedNode.GetMaxWidth() : selectedNode.GetMaxHeight();
            max = Math.Min(max, nodeMax);
        }
        return max;
    }

    internal bool IsAutoSelectTab(bool? whenOpen = null)
    {
        whenOpen ??= Selected != -1;
        return whenOpen.Value ? Model.BorderAutoSelectTabWhenOpen : Model.BorderAutoSelectTabWhenClosed;
    }

    internal void SetSize(double pos)
    {
        if (Selected == -1)
        {
            Size = pos;
            return;
        }
        var tabNode = (TabNode)Children[Selected];
        double tabBorderSize = IsHorizontal() ? tabNode.BorderWidth : tabNode.BorderHeight;
        if (tabBorderSize == -1)
        {
            Size = pos;
        }
        else if (IsHorizontal())
        {
            tabNode.SetBorderWidth(pos);
        }
        else
        {
            tabNode.SetBorderHeight(pos);
        }
    }

    internal void Remove(TabNode node)
    {
        int removedIndex = RemoveChild(node);
        if (Selected != -1)
        {
            ModelUtils.AdjustSelectedIndex(this, removedIndex);
        }
    }

    internal override DropInfo? CanDrop(Node dragNode, double x, double y)
    {
        if (dragNode is not TabNode || !ModelUtils.IsPane(dragNode))
        {
            return null; // borders only accept panes (no document <-> pane docking)
        }

        DropInfo? dropInfo = null;
        const DockLocation dockLocation = DockLocation.Center;

        if (TabHeaderRect.ContainsInclusive(x, y))
        {
            // Insertion axis follows the actual tab-strip shape (wide = horizontal tabs), not the border side, so
            // a horizontal strip (Top/Bottom faithful, or any side under the Extended bottom strip) inserts by X
            // and a vertical strip (Left/Right faithful) inserts by Y. Behaviour matches the faithful port.
            if (TabHeaderRect.Width >= TabHeaderRect.Height)
            {
                if (Children.Count > 0)
                {
                    var childRect = ((TabNode)Children[0]).TabRect;
                    double childY = childRect.Y;
                    double childHeight = childRect.Height;
                    double pos = TabHeaderRect.X;
                    for (int i = 0; i < Children.Count; i++)
                    {
                        childRect = ((TabNode)Children[i]).TabRect;
                        double childCenter = childRect.X + childRect.Width / 2;
                        if (x >= pos && x < childCenter)
                        {
                            var outline = new Rect(childRect.X - 2, childY, 3, childHeight);
                            dropInfo = new DropInfo(this, outline, dockLocation, i, DropOutlineKind.Standard);
                            break;
                        }
                        pos = childCenter;
                    }
                    if (dropInfo is null)
                    {
                        var outline = new Rect(childRect.Right - 2, childY, 3, childHeight);
                        dropInfo = new DropInfo(this, outline, dockLocation, Children.Count, DropOutlineKind.Standard);
                    }
                }
                else
                {
                    var outline = new Rect(TabHeaderRect.X + 1, TabHeaderRect.Y + 2, 3, 18);
                    dropInfo = new DropInfo(this, outline, dockLocation, 0, DropOutlineKind.Standard);
                }
            }
            else
            {
                if (Children.Count > 0)
                {
                    var childRect = ((TabNode)Children[0]).TabRect;
                    double childX = childRect.X;
                    double childWidth = childRect.Width;
                    double pos = TabHeaderRect.Y;
                    for (int i = 0; i < Children.Count; i++)
                    {
                        childRect = ((TabNode)Children[i]).TabRect;
                        double childCenter = childRect.Y + childRect.Height / 2;
                        if (y >= pos && y < childCenter)
                        {
                            var outline = new Rect(childX, childRect.Y - 2, childWidth, 3);
                            dropInfo = new DropInfo(this, outline, dockLocation, i, DropOutlineKind.Standard);
                            break;
                        }
                        pos = childCenter;
                    }
                    if (dropInfo is null)
                    {
                        var outline = new Rect(childX, childRect.Bottom - 2, childWidth, 3);
                        dropInfo = new DropInfo(this, outline, dockLocation, Children.Count, DropOutlineKind.Standard);
                    }
                }
                else
                {
                    var outline = new Rect(TabHeaderRect.X + 2, TabHeaderRect.Y + 1, 18, 3);
                    dropInfo = new DropInfo(this, outline, dockLocation, 0, DropOutlineKind.Standard);
                }
            }

            if (!dragNode.CanDockInto(dragNode, dropInfo))
            {
                return null;
            }
        }
        else if (Selected != -1 && ContentRect.ContainsInclusive(x, y))
        {
            dropInfo = new DropInfo(this, ContentRect, dockLocation, -1, DropOutlineKind.Standard);
            if (!dragNode.CanDockInto(dragNode, dropInfo))
            {
                return null;
            }
        }

        return dropInfo;
    }

    internal override void Drop(Node dragNode, DockLocation location, int index, bool? select = null)
    {
        int fromIndex = 0;
        var dragParent = dragNode.Parent;
        if (dragParent is not null)
        {
            fromIndex = dragParent.RemoveChild(dragNode);
            // If a selected border tab is docked into a different border, deselect the source border tabs.
            if (!ReferenceEquals(dragParent, this) && dragParent is BorderNode sourceBorder && sourceBorder.Selected == fromIndex)
            {
                sourceBorder.SetSelected(-1);
            }
            else
            {
                ModelUtils.AdjustSelectedIndex(dragParent, fromIndex);
            }
        }

        // Dropping a tab back into the same border forward of its old position reduces the insert index.
        if (dragNode is TabNode && ReferenceEquals(dragParent, this) && fromIndex < index && index > 0)
        {
            index--;
        }

        int insertPos = index == -1 ? Children.Count : index;

        if (dragNode is TabNode tab)
        {
            AddChild(tab, insertPos);
        }

        if (select == true || (select != false && IsAutoSelectTab()))
        {
            SetSelected(insertPos);
        }

        Model.Tidy();
    }

    /// <summary>The allowable [min, max] travel for the border splitter (index/useMinSize were vestigial upstream).</summary>
    internal (double Min, double Max) GetSplitterBounds(bool useMinSize = false)
    {
        double low = 0;
        double high = 0;
        double minSize = useMinSize ? GetMinSize() : 0;
        double maxSize = useMinSize ? GetMaxSize() : 99999;
        var rootRow = Model.GetRootRow(Model.MainLayoutId);
        var innerRect = rootRow.Rect;
        double splitterSize = Model.SplitterSize;

        if (Location == DockLocation.Top)
        {
            low = TabHeaderRect.Bottom + minSize;
            double maxPos = TabHeaderRect.Bottom + maxSize;
            high = Math.Max(low, innerRect.Bottom - rootRow.MinHeight - splitterSize);
            high = Math.Min(high, maxPos);
        }
        else if (Location == DockLocation.Left)
        {
            low = TabHeaderRect.Right + minSize;
            double maxPos = TabHeaderRect.Right + maxSize;
            high = Math.Max(low, innerRect.Right - rootRow.MinWidth - splitterSize);
            high = Math.Min(high, maxPos);
        }
        else if (Location == DockLocation.Bottom)
        {
            high = TabHeaderRect.Y - minSize - splitterSize;
            double maxPos = TabHeaderRect.Y - maxSize - splitterSize;
            low = Math.Min(high, innerRect.Y + rootRow.MinHeight);
            low = Math.Max(low, maxPos);
        }
        else if (Location == DockLocation.Right)
        {
            high = TabHeaderRect.X - minSize - splitterSize;
            double maxPos = TabHeaderRect.X - maxSize - splitterSize;
            low = Math.Min(high, innerRect.X + rootRow.MinWidth);
            low = Math.Max(low, maxPos);
        }

        return (low, high);
    }

    internal double CalculateSplit(double splitterPos)
    {
        var (low, high) = GetSplitterBounds();
        if (Location == DockLocation.Bottom || Location == DockLocation.Right)
        {
            return Math.Max(0, high - splitterPos);
        }
        return Math.Max(0, splitterPos - low);
    }
}
