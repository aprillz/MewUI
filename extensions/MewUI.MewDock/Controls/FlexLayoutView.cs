using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

using DockModel = Aprillz.MewUI.MewDock.Model.Model;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// The root control that renders one layout space of a <see cref="DockModel"/> (port of the FlexLayout
/// <c>Layout</c> component). The main view (default layout id) also hosts popout windows for window-type
/// sub-layouts. It follows the model's changes in place: the node views come from the shared registry and follow their
/// own nodes, and two <see cref="PaneLayer"/>s show the visible tabs' content over them. It is the drop target for
/// tab/tabset drags.
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
    private readonly Dictionary<BorderNode, FlexBorderBar> _bars = new();
    private readonly HashSet<BorderNode> _observedBorders = new();
    private readonly Dictionary<string, (Window Window, FlexLayoutView View)> _popouts = new();
    // Content of the document tree and the pinned docks, drawn over them and under the border strips.
    private readonly PaneLayer _dockedLayer = new();
    // Content of a revealed auto-hide tool, drawn over the border strips.
    private readonly PaneLayer _revealLayer = new();
    private UIElement? _rootView;
    private UIElement? _content;
    private FlexDropTargetIndicator? _indicator;
    private EdgeDockIndicators? _edgeIndicators;
    private DockLocation? _revealedBorder;
    private DockLocation? _pendingDocumentEdge;
    private Rect _innerArea = Rect.Empty;
    private bool _released;

    internal FlexLayoutView(DockModel model, FlexViewContext context, string? layoutId)
    {
        _model = model;
        _context = context;
        _layoutId = layoutId ?? DockModel.MainLayoutId;
        _isMain = _layoutId == DockModel.MainLayoutId;
        StyleSheet = DockStyles.CreateStyleSheet();
        AllowDrop = true;

        Add(_dockedLayer);
        Add(_revealLayer);

        _model.RootRowChanged += OnLayoutTreeChanged;
        _model.MaximizedChanged += OnLayoutTreeChanged;
        _model.BorderAdded += OnBorderAdded;
        _model.ChangesCompleted += OnChangesCompleted;
        _model.DraggingChanged += OnDraggingChanged;
        _model.AttributesChanged += OnAttributesChanged;
    }

    /// <summary>Builds the view's content once the derived layer is ready; call right after construction.</summary>
    internal void Initialize()
    {
        SyncRoot();
        if (_isMain)
        {
            foreach (var border in _model.BorderSet.Borders)
            {
                ObserveBorder(border);
            }
            SyncEdgeRegions();
            SyncBorders();
        }
        SyncLayers();
        if (_isMain)
        {
            SyncPopouts();
        }
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
            SyncRoot();
            SyncLayers();
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
    // docks) hooks the sync / measure / arrange / drag pipeline here. Faithful: no regions, no-ops.
    private protected virtual void SyncEdgeRegions() { }

    private protected virtual void CollectEdgeRegionTabSets(List<TabSetNode> tabSets) { }

    private protected virtual Rect MeasureEdgeRegions(Rect remaining) => remaining;

    private protected virtual Rect ArrangeEdgeRegions(Rect remaining) => remaining;

    private protected virtual void ReleaseEdgeRegions() { }

    // Returns true when an edge-region drag target is active under the cursor (the override owns its pending
    // state and the drag-event flags); false lets the faithful pipeline continue.
    private protected virtual bool TryEdgeRegionDragTarget(DragEventArgs e, Node dragNode) => false;

    private protected virtual bool TryEdgeRegionDrop(DragEventArgs e, Node dragNode) => false;

    private protected virtual void HideEdgeRegionDragVisuals() { }

    private protected virtual void DismissEdgeRegionDragVisuals() { }

    /// <summary>Adds a piece of chrome (an edge region, its splitter) under the content layers.</summary>
    private protected void AddChrome(UIElement element) => Insert(IndexOfChild(_dockedLayer), element);

    private int IndexOfChild(Element child)
    {
        var children = Children;
        for (int index = 0; index < children.Count; index++)
        {
            if (ReferenceEquals(children[index], child))
            {
                return index;
            }
        }
        return children.Count;
    }

    /// <summary>Stops following the model and closes the popout windows; the view is not used again.</summary>
    internal void Release()
    {
        if (_released)
        {
            return;
        }
        _released = true;
        _model.RootRowChanged -= OnLayoutTreeChanged;
        _model.MaximizedChanged -= OnLayoutTreeChanged;
        _model.BorderAdded -= OnBorderAdded;
        _model.ChangesCompleted -= OnChangesCompleted;
        _model.DraggingChanged -= OnDraggingChanged;
        _model.AttributesChanged -= OnAttributesChanged;
        foreach (var border in _observedBorders)
        {
            border.ChildInserted -= OnBorderTabsChanged;
            border.ChildRemoved -= OnBorderTabsChanged;
        }
        _observedBorders.Clear();
        foreach (var bar in _bars.Values)
        {
            bar.Release();
        }
        _bars.Clear();
        ReleaseEdgeRegions();
        _dockedLayer.Show(Array.Empty<PaneHost>());
        _revealLayer.Show(Array.Empty<PaneHost>());
        foreach (var (window, view) in _popouts.Values.ToList())
        {
            view.Release();
            window.Close();
        }
        _popouts.Clear();
        Clear();
        if (_isMain)
        {
            _context.ReleaseAll();
        }
    }

    private void OnLayoutTreeChanged(Layout layout)
    {
        if (layout.LayoutId == _layoutId)
        {
            SyncRoot();
        }
    }

    private void OnBorderAdded(BorderNode border)
    {
        if (_isMain)
        {
            ObserveBorder(border);
            SyncBorders();
        }
    }

    private void ObserveBorder(BorderNode border)
    {
        if (_observedBorders.Add(border))
        {
            border.ChildInserted += OnBorderTabsChanged;
            border.ChildRemoved += OnBorderTabsChanged;
        }
    }

    /// <summary>An auto-hide border shows its strip only while it holds tabs.</summary>
    private void OnBorderTabsChanged(Node tab, int index) => SyncBorders();

    /// <summary>
    /// A whole action (and every action it set off) is done: brings the parts that follow the model as a whole up to
    /// date, then lets go of the views of nodes that left.
    /// </summary>
    private void OnChangesCompleted()
    {
        if (_released)
        {
            return;
        }
        SyncRoot();
        if (_isMain)
        {
            SyncEdgeRegions();
            SyncBorders();
            SyncPopouts();
        }
        SyncLayers();
        if (_isMain)
        {
            _context.Sweep(_model);
        }
    }

    /// <summary>Tear-off: hiding or showing the dragged node reflows the layout and takes its content out of the layer.</summary>
    private void OnDraggingChanged()
    {
        InvalidateMeasure();
        SyncLayers();
    }

    private void OnAttributesChanged() => InvalidateMeasure();

    /// <summary>
    /// Puts the right view at the root: the custom centre, the maximized tabset, or the layout's root row. A tabset
    /// that stops being maximized goes back into its row.
    /// </summary>
    private void SyncRoot()
    {
        if (_released)
        {
            return;
        }

        UIElement? desired = null;
        if (_model.Layouts.TryGetValue(_layoutId, out var layout))
        {
            if (_isMain && _content is not null)
            {
                desired = _content;
            }
            else if (layout.MaximizedTabSet is TabSetNode maximized)
            {
                desired = _context.ViewFor(maximized);
            }
            else if (layout.RootRow is RowNode root)
            {
                desired = _context.ViewFor(root);
            }
        }

        if (ReferenceEquals(desired, _rootView) && (desired is null || ReferenceEquals(desired.Parent, this)))
        {
            return;
        }

        var previous = _rootView;
        _rootView = desired;
        if (previous is not null && ReferenceEquals(previous.Parent, this))
        {
            Remove(previous);
        }
        if (desired is not null)
        {
            if (desired.Parent is Panel borrowedFrom && !ReferenceEquals(borrowedFrom, this))
            {
                // A maximized tabset is borrowed from its row while it fills the layout.
                borrowedFrom.Remove(desired);
            }
            Insert(0, desired);
        }
        // A tabset that was maximized returns to the row it belongs to.
        if (previous is INodeView { Node: TabSetNode formerlyMaximized } && formerlyMaximized.Parent?.View is FlexRowView row)
        {
            row.SyncChildren();
        }
        InvalidateMeasure();
    }

    /// <summary>Shows a strip for each border that should show one (main view only).</summary>
    private void SyncBorders()
    {
        if (!_isMain || _released)
        {
            return;
        }

        foreach (var border in _model.BorderSet.Borders)
        {
            ObserveBorder(border);
            // Show the strip unless it is an auto-hide border with no tabs (port of BorderContainer's condition). A
            // revealed location (set during a drag) forces an auto-hide empty border visible.
            bool show = !border.IsAutoHide || border.Children.Count > 0 || _revealedBorder == border.Location;
            border.IsShowing = show;
            if (show && !_bars.ContainsKey(border))
            {
                var bar = _context.BorderView?.Invoke(border, _context) ?? new FlexBorderBar(border, _context);
                _bars[border] = bar;
                // Border strips go over the docked content and under the revealed content.
                Insert(IndexOfChild(_revealLayer), bar);
                InvalidateMeasure();
            }
            else if (!show && _bars.Remove(border, out var hidden))
            {
                hidden.Release();
                Remove(hidden);
                InvalidateMeasure();
            }
        }
    }

    /// <summary>Makes the content layers show exactly the tabs that are visible in this layout now.</summary>
    private void SyncLayers()
    {
        if (_released)
        {
            return;
        }

        var dragging = _model.DraggingNode;
        var tabSets = new List<TabSetNode>();
        if (!(_isMain && _content is not null) && _model.Layouts.TryGetValue(_layoutId, out var layout))
        {
            if (layout.MaximizedTabSet is TabSetNode maximized)
            {
                tabSets.Add(maximized);
            }
            else
            {
                layout.RootRow?.ForEachNode((node, level) =>
                {
                    if (node is TabSetNode tabSet)
                    {
                        tabSets.Add(tabSet);
                    }
                }, 0);
            }
        }
        if (_isMain)
        {
            CollectEdgeRegionTabSets(tabSets);
        }

        var docked = new List<PaneHost>();
        foreach (var tabSet in tabSets)
        {
            // A tabset torn off by a drag (or left with only the dragged tab) shows nothing until the drag ends.
            if (dragging is not null && !ModelUtils.HasContent(tabSet, dragging))
            {
                continue;
            }
            if (FlexTabSetView.EffectiveSelected(tabSet) is TabNode tab && _context.Host(tab) is PaneHost host)
            {
                docked.Add(host);
            }
        }

        var revealed = new List<PaneHost>();
        foreach (var border in _bars.Keys)
        {
            if (border.GetSelectedNode() is TabNode tab && !ReferenceEquals(tab, dragging) && _context.Host(tab) is PaneHost host)
            {
                revealed.Add(host);
            }
        }

        _dockedLayer.Show(docked);
        _revealLayer.Show(revealed);
    }

    /// <summary>Opens a window for each window-type sub-layout and closes the windows whose layout is gone.</summary>
    private void SyncPopouts()
    {
        foreach (var (id, layout) in _model.Layouts)
        {
            if (id == DockModel.MainLayoutId || layout.Type != LayoutType.Window || _popouts.ContainsKey(id))
            {
                continue;
            }

            string capturedId = id;
            var childView = new FlexLayoutView(_model, _context, capturedId);
            childView.Initialize();
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
                uint targetDpi = DpiHelper.GetDpiForPoint(new Point(rect.X, rect.Y));
                double targetScale = targetDpi > 0 ? targetDpi / 96.0 : 1.0;
                window.StartManualPosition(rect.X / targetScale - 20, rect.Y / targetScale - 10);
            }

            window.Closed += () =>
            {
                // Closed by the user: dock the content back. Closed because the layout went away: nothing to do.
                if (!_released && _popouts.Remove(capturedId, out var closing))
                {
                    closing.View.Release();
                    if (_model.Layouts.ContainsKey(capturedId))
                    {
                        _model.DoAction(DockAction.ClosePopout(capturedId));
                    }
                }
            };
            _popouts[capturedId] = (window, childView);
            window.Show(FindVisualRoot() as Window);
        }

        bool closedAny = false;
        foreach (var id in _popouts.Keys.ToList())
        {
            if (_model.Layouts.ContainsKey(id))
            {
                continue;
            }
            var (window, view) = _popouts[id];
            _popouts.Remove(id);
            view.Release();
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
        var remaining = ReserveBorders(new Rect(0, 0, availableSize.Width, availableSize.Height), arrange: false);
        remaining = MeasureEdgeRegions(remaining);
        _rootView?.Measure(remaining.Size);

        // The layers measure each content with the size its group gave it just now, so they measure every time.
        _dockedLayer.InvalidateMeasure();
        _dockedLayer.Measure(availableSize);
        _revealLayer.InvalidateMeasure();
        _revealLayer.Measure(availableSize);
        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsPositiveInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var remaining = ReserveBorders(bounds, arrange: true);

        // Area left for the edge regions + the document, stored so feature previews can reserve against the same rect.
        _innerArea = remaining;

        remaining = ArrangeEdgeRegions(remaining);

        _rootView?.Arrange(remaining);

        // After the groups: each content goes where its group put its content area in this pass.
        _dockedLayer.InvalidateArrange();
        _dockedLayer.Arrange(bounds);
        _revealLayer.InvalidateArrange();
        _revealLayer.Arrange(bounds);
    }

    /// <summary>Carves a strip per border off <paramref name="bounds"/>, measures or arranges each bar in it, and returns the area left.</summary>
    private Rect ReserveBorders(Rect bounds, bool arrange)
    {
        var remaining = bounds;

        // Reserve an edge strip per border (top/bottom span the full width, then left/right between them).
        foreach (var location in BorderOrder)
        {
            FlexBorderBar? bar = null;
            foreach (var candidate in _bars.Values)
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
            if (arrange)
            {
                bar.Arrange(region);
            }
            else
            {
                bar.Measure(region.Size);
            }
        }
        return remaining;
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
        _model.SetDraggingNode(null); // stop hiding before the move, so the node shows at its new spot
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
        const double FRACTION = 0.25;
        return edge switch
        {
            DockLocation.Left => new Rect(area.X, area.Y, area.Width * FRACTION, area.Height),
            DockLocation.Right => new Rect(area.Right - area.Width * FRACTION, area.Y, area.Width * FRACTION, area.Height),
            DockLocation.Top => new Rect(area.X, area.Y, area.Width, area.Height * FRACTION),
            _ => new Rect(area.X, area.Bottom - area.Height * FRACTION, area.Width, area.Height * FRACTION),
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
        const double MARGIN = 12;
        var bounds = Bounds;
        if (pos.X <= bounds.X + MARGIN)
        {
            return DockLocation.Left;
        }
        if (pos.X >= bounds.Right - MARGIN)
        {
            return DockLocation.Right;
        }
        if (pos.Y <= bounds.Y + MARGIN)
        {
            return DockLocation.Top;
        }
        if (pos.Y >= bounds.Bottom - MARGIN)
        {
            return DockLocation.Bottom;
        }
        return null;
    }

    // During a drag near an edge, an empty auto-hide border shows its strip so it can take the drop.
    private void SetRevealedBorder(DockLocation? location)
    {
        if (location is DockLocation edge)
        {
            bool revealable = false;
            foreach (var candidate in _model.BorderSet.Borders)
            {
                if (candidate.Location == edge && candidate.IsAutoHide && candidate.Children.Count == 0)
                {
                    revealable = true;
                    break;
                }
            }
            if (!revealable)
            {
                location = null;
            }
        }

        if (location == _revealedBorder)
        {
            return;
        }
        _revealedBorder = location;
        SyncBorders();
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
