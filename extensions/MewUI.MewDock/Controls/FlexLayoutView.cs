using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

using DockModel = Aprillz.MewUI.MewDock.Model.Model;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// The root control that renders one layout space of a <see cref="DockModel"/> (port of the FlexLayout
/// <c>Layout</c> component). The main view (default layout id) also hosts popout windows for window-type
/// sub-layouts. Rebuilds when the model changes and is the drop target for tab/tabset drags.
/// </summary>
internal class FlexLayoutView : Panel
{
    /// <summary>Data-object format carrying the dragged <see cref="Node"/> (in-process reference).</summary>
    public const string DragFormat = "application/x-mewdock.flexnode";

    private static readonly DockLocation[] BorderOrder = { DockLocation.Top, DockLocation.Bottom, DockLocation.Left, DockLocation.Right };

    private readonly DockModel _model;
    private readonly FlexViewContext _context;
    private readonly string _layoutId;
    private readonly bool _isMain;
    private readonly List<FlexBorderBar> _borderBars = new();
    private readonly Dictionary<string, Window> _popoutWindows = new();
    private UIElement? _rootView;
    private UIElement? _content;
    private FlexDropTargetIndicator? _indicator;
    private EdgeDockIndicators? _edgeIndicators;
    private DockLocation? _revealedBorder;
    private DockLocation? _pendingDocumentEdge;
    private Rect _innerArea = Rect.Empty;
    private FlexBorderBar? _revealBar;

    public FlexLayoutView(DockModel model, Func<TabNode, UIElement?> factory,
        Func<TabNode, UIElement?>? header = null, string? layoutId = null)
        : this(model, new FlexViewContext(factory, header), layoutId)
    {
    }

    internal FlexLayoutView(DockModel model, FlexViewContext context, string? layoutId)
    {
        _model = model;
        _context = context;
        _layoutId = layoutId ?? DockModel.MainLayoutId;
        _isMain = _layoutId == DockModel.MainLayoutId;
        StyleSheet = DockStyles.CreateStyleSheet();
        AllowDrop = true;
        Rebuild();
        if (_isMain)
        {
            SyncPopouts();
        }
        _model.AddChangeListener(OnModelChanged);
        // Tear-off: hiding/showing the dragged node only reflows (no rebuild), so the drag source view survives.
        _model.DraggingChanged += OnDraggingChanged;
    }

    /// <summary>
    /// Optional custom element for the centre (main view only). When set it REPLACES the document tabset tree, so the
    /// host can put an arbitrary workspace in the middle while tools still dock around the edges. Null (the default)
    /// uses the built-in document host - the model's document layout.
    /// </summary>
    public UIElement? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(value, _content))
            {
                return;
            }
            _content = value;
            Rebuild();
        }
    }

    private Node? _lastDragging;

    // Re-arrange the container that hides/restores the dragged node. Its own rect is unchanged, so invalidating the
    // root would be skipped - we target the node's parent view (the tabset for a tab, the row for a tabset) directly.
    private void OnDraggingChanged()
    {
        RefreshDragOwner(_lastDragging);
        _lastDragging = _model.DraggingNode;
        RefreshDragOwner(_lastDragging);
        InvalidateArrange(); // re-run the dock reservation so a fully-dragged tool dock collapses / restores
    }

    private static void RefreshDragOwner(Node? node)
    {
        // Walk up every ancestor: the dragged node's tabset reflows its strip (and falls its content/highlight off
        // the dragged tab), its row reflows away a now-empty tabset, and so on up. Each ancestor view's own rect is
        // unchanged, so a single root re-arrange would skip it - invalidate them directly.
        for (var ancestor = node?.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.View is not UIElement view)
            {
                continue;
            }
            if (view is FlexTabSetView tabSetView)
            {
                tabSetView.SyncSelection();
            }
            else
            {
                view.InvalidateArrange();
            }
        }
    }

    public DockModel Model => _model;

    public string LayoutId => _layoutId;

    // Members the Extended layer (ExtendedLayoutView, same assembly) builds on. The faithful view holds no feature
    // flags - which view type is instantiated decides the behavior, mirroring the Model/ExtendedDockModel split.
    private protected FlexViewContext Context => _context;

    private protected bool IsMain => _isMain;

    /// <summary>The area inside the reserved border strips (edge regions and the document share it).</summary>
    private protected Rect InnerArea => _innerArea;

    private protected UIElement? RootView => _rootView;

    private protected FlexDropTargetIndicator? Indicator => _indicator;

    // Edge-region seams: a feature layer that reserves edge regions around the document (the Extended pinned
    // docks) hooks the rebuild / measure / arrange / drag pipeline here. Faithful: no regions, no-ops.
    private protected virtual void BuildEdgeRegions() { }

    private protected virtual void MeasureEdgeRegions(Size availableSize) { }

    private protected virtual Rect ArrangeEdgeRegions(Rect remaining) => remaining;

    // Returns true when an edge-region drag target is active under the cursor (the override owns its pending
    // state and the drag-event flags); false lets the faithful pipeline continue.
    private protected virtual bool TryEdgeRegionDragTarget(DragEventArgs e, Node dragNode) => false;

    private protected virtual bool TryEdgeRegionDrop(DragEventArgs e, Node dragNode) => false;

    private protected virtual void HideEdgeRegionDragVisuals() { }

    private protected virtual void DismissEdgeRegionDragVisuals() { }

    // Selection / active-tabset changes update the existing views in place (so an in-flight drag source is not
    // destroyed mid-gesture); structural changes rebuild the whole tree and (main view only) re-sync popouts.
    private void OnModelChanged(DockAction action)
    {
        if (action is SelectTabAction or SetActiveTabsetAction)
        {
            _model.VisitNodes((node, level) =>
            {
                if (node.View is FlexTabSetView tabSetView)
                {
                    tabSetView.SyncSelection();
                }
                else if (node.View is FlexBorderBar borderBar)
                {
                    borderBar.SyncSelection();
                }
            });
            InvalidateArrange();
        }
        else
        {
            Rebuild();
            if (_isMain)
            {
                SyncPopouts();
            }
        }
    }

    private void Rebuild()
    {
        Clear();
        _borderBars.Clear();

        // Document area (root) is added FIRST so the pinned tool docks and (overlaying) auto-hide borders render on
        // top of it.
        if (!_model.Layouts.ContainsKey(_layoutId))
        {
            _rootView = null;
        }
        else if (_isMain && _content is not null)
        {
            // Host-supplied custom centre: render it instead of the document tabset tree (tools still dock around it).
            _rootView = _content;
            Add(_rootView);
        }
        else
        {
            // Empty document area renders as a blank pane; the host fills it via CenterContent if it wants a start page.
            var maximized = _model.GetMaximizedTabset(_layoutId);
            Node rootNode = maximized is not null ? maximized : _model.GetRootRow(_layoutId);
            _rootView = FlexViewFactory.BuildNodeView(rootNode, _context);
            Add(_rootView);
        }

        // Borders + edge regions belong to the main layout only; added after the root so they render above it.
        if (_isMain)
        {
            // Feature edge regions (Extended pinned docks) sit between the root and the borders in z-order.
            BuildEdgeRegions();

            // Auto-hide borders last (their revealed panel overlays the document content).
            foreach (var border in _model.BorderSet.Borders)
            {
                // Show the strip unless it is an auto-hide border with no tabs (port of BorderContainer's
                // condition). A revealed location (set during a drag) forces an auto-hide empty border visible.
                bool show = !border.IsAutoHide || border.Children.Count > 0 || _revealedBorder == border.Location;
                border.IsShowing = show;
                if (show)
                {
                    var bar = _context.BorderView?.Invoke(border, _context) ?? new FlexBorderBar(border, _context);
                    _borderBars.Add(bar);
                    Add(bar);
                }
            }
        }

        InvalidateMeasure();
    }

    private void SyncPopouts()
    {
        foreach (var (id, layout) in _model.Layouts)
        {
            if (id == DockModel.MainLayoutId || layout.Type != LayoutType.Window || _popoutWindows.ContainsKey(id))
            {
                continue;
            }

            string capturedId = id;
            var childView = new FlexLayoutView(_model, _context, capturedId);
            var rect = layout.Rect;
            double width = rect.Width > 0 ? rect.Width : 640;
            double height = rect.Height > 0 ? rect.Height : 440;
            var window = new Window()
                .Title(string.Empty)
                .Resizable(width, height)
                .Content(childView);

            // rect.X/Y is the PHYSICAL drop cursor (device px). Convert it to startup DIPs using the TARGET
            // monitor's scale (the monitor under the drop point), so the create-time placement is exact on every
            // mixed-DPI monitor in one shot - no create-then-move flicker.
            if (rect.X != 0 || rect.Y != 0)
            {
                uint targetDpi = Application.Current.PlatformHost.GetDpiForPoint(new Point(rect.X, rect.Y));
                double targetScale = targetDpi > 0 ? targetDpi / 96.0 : 1.0;
                window.StartManualPosition(rect.X / targetScale - 20, rect.Y / targetScale - 10);
            }

            window.Closed += () =>
            {
                if (_popoutWindows.Remove(capturedId) && _model.Layouts.ContainsKey(capturedId))
                {
                    _model.DoAction(DockAction.ClosePopout(capturedId));
                }
            };
            _popoutWindows[capturedId] = window;
            window.Show(FindVisualRoot() as Window);
        }

        bool closedAny = false;
        foreach (var id in _popoutWindows.Keys.ToList())
        {
            bool gone = !_model.Layouts.ContainsKey(id);
            bool empty = !gone && (_model.Layouts[id].RootRow is not RowNode root || root.Children.Count == 0);
            if (!gone && !empty)
            {
                continue;
            }

            var window = _popoutWindows[id];
            _popoutWindows.Remove(id);
            if (empty)
            {
                // All content was dragged out: drop the now-empty sub-layout so it does not linger.
                _model.Layouts.Remove(id);
            }
            window.Close();
            closedAny = true;
        }

        // Closing a popout lets the OS hand focus to an arbitrary next window; pull it back to the dock so the
        // main window (the drop target the user just released over) stays focused.
        if (closedAny && FindVisualRoot() is Window mainWindow)
        {
            mainWindow.Activate();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        foreach (var bar in _borderBars)
        {
            bar.Measure(availableSize);
        }
        MeasureEdgeRegions(availableSize);
        _rootView?.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var remaining = bounds;

        // Reserve an edge strip per border (top/bottom span the full width, then left/right between them).
        foreach (var location in BorderOrder)
        {
            FlexBorderBar? bar = null;
            foreach (var candidate in _borderBars)
            {
                if (candidate.Location == location)
                {
                    bar = candidate;
                    break;
                }
            }
            if (bar is null)
            {
                continue;
            }

            // Only Footprint is reserved from the centre; OverlayExtent (the auto-hide reveal panel) is painted on
            // top of the content, so the bar is arranged Footprint+OverlayExtent wide but only Footprint is carved.
            double footprint = bar.Footprint;
            double total = footprint + bar.OverlayExtent;
            Rect region;
            switch (location)
            {
                case DockLocation.Top:
                    region = new Rect(remaining.X, remaining.Y, remaining.Width, total);
                    remaining = new Rect(remaining.X, remaining.Y + footprint, remaining.Width, Math.Max(0, remaining.Height - footprint));
                    break;
                case DockLocation.Bottom:
                    region = new Rect(remaining.X, remaining.Bottom - total, remaining.Width, total);
                    remaining = new Rect(remaining.X, remaining.Y, remaining.Width, Math.Max(0, remaining.Height - footprint));
                    break;
                case DockLocation.Left:
                    region = new Rect(remaining.X, remaining.Y, total, remaining.Height);
                    remaining = new Rect(remaining.X + footprint, remaining.Y, Math.Max(0, remaining.Width - footprint), remaining.Height);
                    break;
                default: // Right
                    region = new Rect(remaining.Right - total, remaining.Y, total, remaining.Height);
                    remaining = new Rect(remaining.X, remaining.Y, Math.Max(0, remaining.Width - footprint), remaining.Height);
                    break;
            }
            bar.Arrange(region);
        }

        // Area left for the edge regions + the document, stored so feature previews can reserve against the same rect.
        _innerArea = remaining;

        remaining = ArrangeEdgeRegions(remaining);

        _rootView?.Arrange(remaining);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        UpdateBorderReveal(e.Position);
        UpdateDragTarget(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        UpdateBorderReveal(e.Position);
        UpdateDragTarget(e);
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        SetRevealedBorder(null);
        _indicator?.Hide();
        HideEdgeRegionDragVisuals();
        _edgeIndicators?.HideIndicators();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        SetRevealedBorder(null);
        _model.SetDraggingNode(null); // stop hiding before the move rebuilds, so the node shows at its new spot
        if (e.Data.TryGetData<Node>(DragFormat, out var dragNode) && dragNode is not null)
        {
            // Match UpdateDragTarget's priority: an active edge-region target wins over the faithful drop (otherwise
            // an outer region that overlaps an existing one would fall through to a join).
            if (TryEdgeRegionDrop(e, dragNode))
            {
                // Consumed by the edge-region feature layer (it set the drag-event flags itself).
            }
            else if (_pendingDocumentEdge is DockLocation docEdge)
            {
                // Document edge-dock marker: dock to that edge of the root row (outer, full-extent).
                _model.DoAction(DockAction.MoveNode(dragNode.GetId(), _model.GetRootRow(_layoutId).GetId(), docEdge, -1));
                e.Accepted = true;
                e.Effect = DragDropEffects.Move;
                e.Handled = true;
            }
            else if (_model.FindDropTargetNode(_layoutId, dragNode, e.Position.X, e.Position.Y) is DropInfo dropInfo)
            {
                _model.DoAction(DockAction.MoveNode(dragNode.GetId(), dropInfo.Node.GetId(), dropInfo.Location, dropInfo.Index));
                e.Accepted = true;
                e.Effect = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        _pendingDocumentEdge = null;
        _indicator?.Dismiss();
        _indicator = null;
        DismissEdgeRegionDragVisuals();
        _edgeIndicators?.Dismiss();
        _edgeIndicators = null;
    }

    private void UpdateDragTarget(DragEventArgs e)
    {
        if (!e.Data.TryGetData<Node>(DragFormat, out var dragNode) || dragNode is null)
        {
            return;
        }

        EnsureIndicator();

        // An edge-region target (Extended dock guides) wins first; off one, the faithful pipeline takes over.
        if (TryEdgeRegionDragTarget(e, dragNode))
        {
            return;
        }

        // Document edge-dock indicators: explicit markers at the document-area edges. Hovering a marker IS the
        // outer edge-dock target (the thin faithful edge band is hard to find), so dropping on it docks to that edge.
        bool showEdges = _isMain && !ModelUtils.IsPane(dragNode) && _model.EnableEdgeDock && _model.EnableEdgeDockIndicators;
        if (showEdges)
        {
            EnsureEdgeIndicators();
            var documentArea = _rootView?.Bounds ?? _innerArea;
            _edgeIndicators?.Update(documentArea);
            if (_edgeIndicators?.HitIndicator(e.Position) is DockLocation edge)
            {
                _pendingDocumentEdge = edge;
                // Snap the preview to the device-pixel grid so it lines up with where the docked content actually
                // lands (the views snap their own bounds), instead of a fractional, slightly-off outline.
                _indicator?.HighlightArea(GetSnappedBorderBounds(EdgePreview(documentArea, edge)), 0);
                e.Accepted = true;
                e.Effect = DragDropEffects.Move;
                return;
            }
        }
        else
        {
            _edgeIndicators?.HideIndicators();
        }
        _pendingDocumentEdge = null;

        // The faithful drop: a thin insertion line for a border strip, the content area + split halves for a tabset,
        // an edge half for a ground edge.
        var dropInfo = _model.FindDropTargetNode(_layoutId, dragNode, e.Position.X, e.Position.Y);
        if (dropInfo is not null)
        {
            _indicator?.HighlightArea(dropInfo.Rect, 0);
            e.Accepted = true;
            e.Effect = DragDropEffects.Move;
            return;
        }

        _indicator?.Hide();
    }

    // The outline an edge-dock marker actually docks into: RowNode.Drop gives the new tabset weight 25 of 100, so
    // the dock takes a QUARTER of the document area (not half) - the preview shows the real landing size.
    private static Rect EdgePreview(Rect area, DockLocation edge)
    {
        const double frac = 0.25;
        return edge switch
        {
            DockLocation.Left => new Rect(area.X, area.Y, area.Width * frac, area.Height),
            DockLocation.Right => new Rect(area.Right - area.Width * frac, area.Y, area.Width * frac, area.Height),
            DockLocation.Top => new Rect(area.X, area.Y, area.Width, area.Height * frac),
            _ => new Rect(area.X, area.Bottom - area.Height * frac, area.Width, area.Height * frac),
        };
    }

    // Virtual: when borders are not drop targets (the Extended layer - panes edge-dock instead), the override
    // suppresses the reveal entirely.
    private protected virtual void UpdateBorderReveal(Point pos)
    {
        if (_isMain)
        {
            SetRevealedBorder(ComputeRevealEdge(pos));
        }
    }

    private DockLocation? ComputeRevealEdge(Point pos)
    {
        const double margin = 12;
        var bounds = Bounds;
        if (pos.X <= bounds.X + margin)
        {
            return DockLocation.Left;
        }
        if (pos.X >= bounds.Right - margin)
        {
            return DockLocation.Right;
        }
        if (pos.Y <= bounds.Y + margin)
        {
            return DockLocation.Top;
        }
        if (pos.Y >= bounds.Bottom - margin)
        {
            return DockLocation.Bottom;
        }
        return null;
    }

    private void SetRevealedBorder(DockLocation? location)
    {
        if (location == _revealedBorder)
        {
            return;
        }

        if (_revealBar is not null)
        {
            _revealBar.Border.IsShowing = false;
            _borderBars.Remove(_revealBar);
            Remove(_revealBar);
            _revealBar = null;
        }

        _revealedBorder = location;

        if (location is DockLocation loc)
        {
            BorderNode? border = null;
            foreach (var candidate in _model.BorderSet.Borders)
            {
                if (candidate.Location == loc && candidate.IsAutoHide && candidate.Children.Count == 0)
                {
                    border = candidate;
                    break;
                }
            }

            if (border is not null)
            {
                border.IsShowing = true;
                _revealBar = _context.BorderView?.Invoke(border, _context) ?? new FlexBorderBar(border, _context);
                _borderBars.Add(_revealBar);
                Add(_revealBar);
            }
            else
            {
                _revealedBorder = null;
            }
        }

        InvalidateMeasure();
    }

    private void EnsureIndicator()
    {
        if (_indicator is null && FindVisualRoot() is Window window)
        {
            _indicator = new FlexDropTargetIndicator(window.OverlayLayer);
            window.OverlayLayer.Add(_indicator);
        }
    }

    private void EnsureEdgeIndicators()
    {
        if (_edgeIndicators is null && FindVisualRoot() is Window window)
        {
            _edgeIndicators = new EdgeDockIndicators(window.OverlayLayer);
            window.OverlayLayer.Add(_edgeIndicators);
        }
    }
}
