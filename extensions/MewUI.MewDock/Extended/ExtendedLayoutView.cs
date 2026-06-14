using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock.Extended;

/// <summary>
/// The main layout view for Extended (VS style) docking. Pairs with <see cref="ExtendedDockModel"/> and owns
/// everything dock-specific the faithful view does not have: the pinned dock edge regions (build / measure /
/// arrange / resize splitters), the explicit dock guides shown while dragging a pane, and the edge-dock drop.
/// Borders are auto-hide only here, so a drag near an edge never reveals an empty border.
/// </summary>
internal sealed class ExtendedLayoutView : FlexLayoutView
{
    private const double DefaultDockSize = 220;

    // A pinned dock rendered as an edge region (its sub-layout root row is a tree of pane tabsets).
    private sealed record DockView(DockLayout Layout, UIElement View, FlexSplitter Splitter)
    {
        public DockLocation Edge => Layout.Edge;
    }

    private readonly List<DockView> _dockViews = new();
    private DockGuides? _guides;
    private (DockLocation Edge, bool Outer)? _pendingEdgeDock;

    public ExtendedLayoutView(ExtendedDockModel model, FlexViewContext context)
        : base(model, context, layoutId: null)
    {
    }

    // Borders are auto-hide only: a drag near an edge never reveals an empty border (panes edge-dock instead).
    private protected override void UpdateBorderReveal(Point pos) { }

    private protected override void BuildEdgeRegions()
    {
        _dockViews.Clear();
        foreach (var (_, layout) in Model.Layouts)
        {
            if (layout is DockLayout dock && dock.RootRow is RowNode dockRoot && dockRoot.Children.Count > 0)
            {
                var view = FlexViewFactory.BuildNodeView(dockRoot, Context);
                Add(view);
                var splitter = CreateDockSplitter(dock);
                Add(splitter);
                _dockViews.Add(new DockView(dock, view, splitter));
            }
        }
    }

    private protected override void MeasureEdgeRegions(Size availableSize)
    {
        foreach (var dock in _dockViews)
        {
            dock.View.Measure(availableSize);
        }
    }

    private protected override Rect ArrangeEdgeRegions(Rect remaining)
    {
        // Reserve docks by nesting rank (low = outer, reserved first = full extent; high = inner, between the
        // outer docks). The rank is chosen by where the user dropped, so a preview running the same Carve sequence
        // lands exactly where the dock ends up.
        var ordered = new List<DockView>(_dockViews);
        ordered.Sort((left, right) => left.Layout.DockRank.CompareTo(right.Layout.DockRank));
        foreach (var dock in ordered)
        {
            // Tear-off: a dock whose entire content is the node being dragged collapses (no reservation), so the
            // document area reflows into its space instead of leaving an empty strip.
            if (DockHoldsOnlyDragged(dock))
            {
                dock.View.Arrange(Rect.Empty);
                dock.Splitter.Arrange(Rect.Empty);
                continue;
            }
            double size = dock.Layout.Size > 0 ? dock.Layout.Size : DefaultDockSize;
            Rect region;
            (region, remaining) = Carve(remaining, dock.Edge, size);
            dock.View.Arrange(region);
            // A resize splitter (also the visual gap) between the dock and the rest.
            Rect splitterRegion;
            (splitterRegion, remaining) = Carve(remaining, dock.Edge, Model.SplitterSize);
            dock.Splitter.Arrange(splitterRegion);
        }
        return remaining;
    }

    // A dock collapses during a tear-off when nothing in it survives the drag - whether the dragged node is the
    // whole tabset (caption drag) or the last remaining tab (single-tab drag).
    private bool DockHoldsOnlyDragged(DockView dock)
    {
        var dragging = Model.DraggingNode;
        return dragging is not null && dock.Layout.RootRow is RowNode root && !ModelUtils.HasContent(root, dragging);
    }

    // Carve a `size` strip off `remaining` at `loc`; returns the strip and the reduced remaining.
    private static (Rect Region, Rect Remaining) Carve(Rect remaining, DockLocation loc, double size) => loc switch
    {
        DockLocation.Top => (new Rect(remaining.X, remaining.Y, remaining.Width, size),
            new Rect(remaining.X, remaining.Y + size, remaining.Width, Math.Max(0, remaining.Height - size))),
        DockLocation.Bottom => (new Rect(remaining.X, remaining.Bottom - size, remaining.Width, size),
            new Rect(remaining.X, remaining.Y, remaining.Width, Math.Max(0, remaining.Height - size))),
        DockLocation.Left => (new Rect(remaining.X, remaining.Y, size, remaining.Height),
            new Rect(remaining.X + size, remaining.Y, Math.Max(0, remaining.Width - size), remaining.Height)),
        _ => (new Rect(remaining.Right - size, remaining.Y, size, remaining.Height),
            new Rect(remaining.X, remaining.Y, Math.Max(0, remaining.Width - size), remaining.Height)),
    };

    // A resize handle between a dock and the rest; dragging it grows/shrinks the dock's size along its edge.
    private FlexSplitter CreateDockSplitter(DockLayout layout)
    {
        DockLocation edge = layout.Edge;
        bool horizontal = edge is DockLocation.Left or DockLocation.Right;
        var splitter = new FlexSplitter { IsColumnAxis = !horizontal, BarThickness = Model.SplitterSize };
        double dragStart = 0;
        double startSize = 0;
        splitter.SplitterDragStarted += e =>
        {
            var p = e.GetPosition(this);
            dragStart = horizontal ? p.X : p.Y;
            startSize = layout.Size > 0 ? layout.Size : DefaultDockSize;
        };
        splitter.SplitterDragging += e =>
        {
            var p = e.GetPosition(this);
            double current = horizontal ? p.X : p.Y;
            // Left/top docks grow when the splitter moves toward the centre (+); right/bottom grow the other way.
            double sign = edge is DockLocation.Left or DockLocation.Top ? 1 : -1;
            layout.Size = Math.Max(80, startSize + sign * (current - dragStart));
            InvalidateArrange();
        };
        return splitter;
    }

    private protected override bool TryEdgeRegionDragTarget(DragEventArgs e, Node dragNode)
    {
        // Guide squares are the edge-dock targets, so hovering one picks outer/inner docking with no
        // cursor-position guessing. Off a guide (or for a document drag), the faithful pipeline takes over.
        if (IsMain && ModelUtils.IsPane(dragNode))
        {
            EnsureGuides();
            _guides?.Update(InnerArea, RootView?.Bounds ?? InnerArea);
            if (_guides?.HitGuide(e.Position) is (DockLocation Edge, bool Outer) guide)
            {
                _pendingEdgeDock = (guide.Edge, guide.Outer);
                Indicator?.HighlightArea(BuildEdgeDockPreview(guide.Edge, guide.Outer), 0);
                e.Accepted = true;
                e.Effect = DragDropEffects.Move;
                return true;
            }
        }
        else
        {
            _guides?.HideGuides();
        }
        _pendingEdgeDock = null;
        return false;
    }

    private protected override bool TryEdgeRegionDrop(DragEventArgs e, Node dragNode)
    {
        if (!_pendingEdgeDock.HasValue)
        {
            return false;
        }
        var pending = _pendingEdgeDock.Value;
        Model.DoAction(DockAction.EdgeDockTool(dragNode.GetId(), pending.Edge, pending.Outer));
        _pendingEdgeDock = null;
        e.Accepted = true;
        e.Effect = DragDropEffects.Move;
        e.Handled = true;
        return true;
    }

    private protected override void HideEdgeRegionDragVisuals() => _guides?.HideGuides();

    private protected override void DismissEdgeRegionDragVisuals()
    {
        _pendingEdgeDock = null;
        _guides?.Dismiss();
        _guides = null;
    }

    private void EnsureGuides()
    {
        // Added after the drop indicator so the guide squares paint on top of the preview highlight.
        if (_guides is null && FindVisualRoot() is Window window)
        {
            _guides = new DockGuides(window.OverlayLayer);
            window.OverlayLayer.Add(_guides);
        }
    }

    // Replay the real rank-ordered Carve reservation with a new dock inserted at its rank, so the preview rect is
    // exactly where the dock lands (outer = reserved first = further out; inner = reserved last = further in).
    private Rect BuildEdgeDockPreview(DockLocation edge, bool outer)
    {
        double rank = NextDockRankForPreview(outer);
        var entries = new List<(DockLocation Edge, double Size, double Rank, bool IsNew)>();
        foreach (var dock in _dockViews)
        {
            if (DockHoldsOnlyDragged(dock))
            {
                continue; // a dock collapsing under the drag frees its space; don't reserve it in the preview
            }
            double existingSize = dock.Layout.Size > 0 ? dock.Layout.Size : DefaultDockSize;
            entries.Add((dock.Edge, existingSize, dock.Layout.DockRank, false));
        }
        entries.Add((edge, DefaultDockSize, rank, true));
        entries.Sort((left, right) => left.Rank.CompareTo(right.Rank));

        var remaining = InnerArea;
        Rect preview = Rect.Empty;
        foreach (var entry in entries)
        {
            Rect region;
            (region, remaining) = Carve(remaining, entry.Edge, entry.Size);
            if (entry.IsNew)
            {
                preview = region;
            }
            // Each dock also reserves a splitter strip, so reserve it here too to keep the preview aligned.
            (_, remaining) = Carve(remaining, entry.Edge, Model.SplitterSize);
        }
        return preview;
    }

    // Rank a hypothetical new dock would get: below all docks (outer) or above them (inner).
    private double NextDockRankForPreview(bool outer)
    {
        bool any = false;
        double min = 0, max = 0;
        foreach (var dock in _dockViews)
        {
            double rank = dock.Layout.DockRank;
            if (!any)
            {
                min = max = rank;
                any = true;
            }
            else
            {
                min = Math.Min(min, rank);
                max = Math.Max(max, rank);
            }
        }
        if (!any)
        {
            return 0;
        }
        return outer ? min - 1 : max + 1;
    }
}
