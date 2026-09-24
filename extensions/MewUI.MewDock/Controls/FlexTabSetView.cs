using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

using DockModel = Aprillz.MewUI.MewDock.Model.Model;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// Renders a <see cref="TabSetNode"/> as a tab pane: a tab-button strip on top (with a maximize control and an
/// overflow dropdown) and a framed body below, where the <see cref="PaneLayer"/> shows the selected tab's content. It
/// follows the tabset's tabs, selection and names in place, and sets the node's rects (Rect / TabStripRect /
/// ContentRect, plus each tab's TabRect) for drop hit-testing.
/// </summary>
internal sealed class FlexTabSetView : Control, IVisualTreeHost, INodeView, IPaneContentOwner
{
    private const double MinHeaderHeight = 26;
    private const double TabSpacing = 2;

    private readonly TabSetNode _tabSet;
    private readonly DockModel _model;
    private readonly FlexViewContext _context;
    private readonly List<FlexTabButton> _tabs = new();
    private readonly HashSet<TabNode> _observedTabs = new();
    private readonly Button _overflowButton;
    private readonly HashSet<FlexTabButton> _hiddenTabs = new();
    private Button? _maximizeButton;
    private UIElement? _toolCaption;
    private bool _overflowActive;
    private double _headerHeight = MinHeaderHeight;
    private Size _contentSize;
    private Rect _contentArea = Rect.Empty;
    private bool _released;

    public FlexTabSetView(TabSetNode tabSet, FlexViewContext context)
    {
        _tabSet = tabSet;
        _model = tabSet.Model;
        _context = context;
        tabSet.View = this;

        // The overflow dropdown: shown only when the tabs do not all fit; clicking it pops a menu of the hidden
        // tabs. Created once and kept; hidden via the render/hit-test overrides (not IsVisible, which thrashes layout).
        _overflowButton = new Button
        {
            Content = new GlyphElement { Kind = GlyphKind.ChevronDown },
            StyleName = BuiltInStyles.FlatButton,
            Padding = new Thickness(0),
            MinWidth = 18,
            MinHeight = 18,
        };
        _overflowButton.Click += ShowOverflowMenu;
        _overflowButton.ToolTip = new TextBlock().BindText(MewUIDockString.ToolTipHiddenTabs);
        AttachChild(_overflowButton);

        // Dragging the empty header strip (areas not on a tab/button) moves the whole tabset.
        CanDrag = tabSet.IsEnableDrag;

        tabSet.ChildInserted += OnTabsChanged;
        tabSet.ChildRemoved += OnTabsChanged;
        tabSet.PropertyChanged += OnTabSetPropertyChanged;
        _model.FocusedChanged += OnFocusChanged;
        _model.ActiveChanged += OnLayoutStateChanged;
        _model.MaximizedChanged += OnLayoutStateChanged;
        _model.DraggingChanged += OnDraggingChanged;

        SyncTabs();
    }

    public Node Node => _tabSet;

    Size IPaneContentOwner.ContentSize => _contentSize;

    Rect IPaneContentOwner.ContentArea => _contentArea;

    public void Release()
    {
        if (_released)
        {
            return;
        }
        _released = true;
        _tabSet.ChildInserted -= OnTabsChanged;
        _tabSet.ChildRemoved -= OnTabsChanged;
        _tabSet.PropertyChanged -= OnTabSetPropertyChanged;
        _model.FocusedChanged -= OnFocusChanged;
        _model.ActiveChanged -= OnLayoutStateChanged;
        _model.MaximizedChanged -= OnLayoutStateChanged;
        _model.DraggingChanged -= OnDraggingChanged;
        foreach (var tab in _observedTabs)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
        }
        _observedTabs.Clear();
        foreach (var button in _tabs)
        {
            button.ReleaseHeader();
            DetachChild(button);
        }
        _tabs.Clear();
        if (ReferenceEquals(_tabSet.View, this))
        {
            _tabSet.View = null;
        }
    }

    // A pane tabset puts its tab strip at the bottom; document tabsets keep it on top.
    private bool HeaderAtBottom => !_tabSet.IsDocument;

    // A single-tool group hides its tab strip: the caption already names it, so the lone tab button is redundant
    // (matches VS). Document tabsets and multi-tab tool groups always show the strip.
    private bool ShowTabStrip => _tabSet.IsDocument || _tabSet.Children.Count > 1;

    private double CaptionHeight => _toolCaption?.DesiredSize.Height ?? 0;

    private void OnTabsChanged(Node tab, int index) => SyncTabs();

    private void OnTabSetPropertyChanged(Node node, NodeProperty property)
    {
        if (property == NodeProperty.Selected)
        {
            SyncSelection();
        }
    }

    private void OnTabPropertyChanged(Node tab, NodeProperty property)
    {
        if (property == NodeProperty.Name)
        {
            foreach (var button in _tabs)
            {
                if (ReferenceEquals(button.Tab, tab))
                {
                    button.RefreshName();
                }
            }
            (_toolCaption as IToolHeader)?.Refresh();
            InvalidateMeasure();
        }
    }

    private void OnFocusChanged()
    {
        InvalidateVisualState();
        foreach (var tab in _tabs)
        {
            tab.InvalidateVisualState();
        }
    }

    private void OnLayoutStateChanged(Layout layout)
    {
        RefreshMaximizeButton();
        OnFocusChanged();
    }

    /// <summary>The dragged tab leaves the strip (and its content the layer) until the drag ends.</summary>
    private void OnDraggingChanged()
    {
        SyncSelection();
        InvalidateMeasure();
    }

    /// <summary>
    /// Makes the strip hold one button per tab in the tabset's order. Buttons of tabs that stay are kept; a tab that
    /// arrives gets a new button, which takes over the tab's header.
    /// </summary>
    private void SyncTabs()
    {
        if (_released)
        {
            return;
        }

        foreach (var tab in _observedTabs.ToList())
        {
            if (!ReferenceEquals(tab.Parent, _tabSet))
            {
                tab.PropertyChanged -= OnTabPropertyChanged;
                _observedTabs.Remove(tab);
            }
        }

        var buttons = new List<FlexTabButton>(_tabSet.Children.Count);
        foreach (var child in _tabSet.Children)
        {
            var tab = (TabNode)child;
            if (_observedTabs.Add(tab))
            {
                tab.PropertyChanged += OnTabPropertyChanged;
            }
            var button = _tabs.Find(candidate => ReferenceEquals(candidate.Tab, tab));
            if (button is null)
            {
                button = new FlexTabButton(tab, _tabSet, _context);
                AttachChild(button);
            }
            buttons.Add(button);
        }
        foreach (var button in _tabs)
        {
            if (!buttons.Contains(button))
            {
                button.ReleaseHeader();
                DetachChild(button);
            }
        }
        _tabs.Clear();
        _tabs.AddRange(buttons);

        EnsureChrome();
        SyncSelection();
        InvalidateMeasure();
    }

    /// <summary>Creates the chrome that depends on whether the group holds documents or tools, which the first tab decides.</summary>
    private void EnsureChrome()
    {
        if (!_tabSet.IsDocument && _toolCaption is null && _context.ToolHeader?.Invoke(_tabSet) is UIElement caption)
        {
            // A pane tabset wears a caption bar (title + pin/close/menu) above its content; the Extended layer supplies it.
            _toolCaption = caption;
            AttachChild(_toolCaption);
        }
        else if (_tabSet.IsDocument && _toolCaption is not null)
        {
            DetachChild(_toolCaption);
            _toolCaption = null;
        }

        bool wantsMaximize = _tabSet.IsEnableMaximize && _tabSet.IsDocument;
        if (wantsMaximize && _maximizeButton is null)
        {
            // The maximize/restore control is a real flat button (captures its own click, so pressing it never
            // starts the header drag). Pane tabsets have no maximize button.
            _maximizeButton = new Button
            {
                StyleName = BuiltInStyles.FlatButton,
                Padding = new Thickness(0),
                MinWidth = 18,
                MinHeight = 18,
            };
            _maximizeButton.Click += () => _model.DoAction(DockAction.MaximizeToggle(_tabSet.GetId(), _tabSet.LayoutId));
            AttachChild(_maximizeButton);
            RefreshMaximizeButton();
        }
        else if (!wantsMaximize && _maximizeButton is not null)
        {
            DetachChild(_maximizeButton);
            _maximizeButton = null;
        }
    }

    private void RefreshMaximizeButton()
    {
        if (_maximizeButton is null)
        {
            return;
        }
        bool maximized = _tabSet.IsMaximized;
        _maximizeButton.Content = new GlyphElement { Kind = maximized ? GlyphKind.WindowRestore : GlyphKind.WindowMaximize };
        _maximizeButton.ToolTip = new TextBlock().BindText(maximized ? MewUIDockString.ToolTipRestore : MewUIDockString.ToolTipMaximize);
    }

    protected override void OnDragStarting(DragStartingEventArgs e)
    {
        base.OnDragStarting(e);

        // Only the empty header strip drags the whole tabset. The strip is at the bottom for tool groups, so test its
        // actual location rather than assuming the top.
        if (!IsInHeaderStrip(e.StartPositionInElement))
        {
            e.Cancel = true;
            return;
        }

        // Nothing selected = nothing to drag (an empty tabset is tidied away anyway); cancel rather than chase a
        // null label.
        if (_tabSet.GetSelectedNode() is not TabNode selected)
        {
            e.Cancel = true;
            return;
        }

        var data = new DataObject();
        data.SetData(FlexLayoutView.DragFormat, _tabSet);
        e.Data = data;
        e.AllowedEffects = DragDropEffects.Move;
        e.Preview = new DragPreviewContent
        {
            Scope = DragPreviewScope.CrossWindow,
            Element = FlexDragChip.BuildGroup(selected.Name ?? MewUIDockString.TitleUnnamedTab.Value, _tabSet.Children.Count),
            MaxWidth = 240,
            Hotspot = new Point(14, 12),
            Opacity = 0.9,
        };

        _model.SetDraggingNode(_tabSet); // tear-off: hide this whole tabset until drop / cancel
    }

    // Released over no drop target (empty space / outside all windows): pop the whole tabset out into a new window.
    protected override void OnDragCompleted(DragCompletedEventArgs e)
    {
        base.OnDragCompleted(e);
        _model.SetDraggingNode(null);
        if (!e.WasCanceled && e.FinalEffect != DragDropEffects.Move)
        {
            // Pass the PHYSICAL cursor position; SyncPopouts converts it to DIPs at placement (mixed-DPI safe).
            _model.DoAction(DockAction.PopoutTabset(_tabSet.GetId(), position: e.ScreenPosition));
        }
    }

    // Clicking the tabset's chrome makes it the active tabset (focus highlight); the content, shown over it by the
    // pane layer, activates it through its host. Selecting a tab already activates it via SelectTab.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButton.Right && IsInHeaderStrip(e.GetPosition(this)))
        {
            ShowGroupMenu(e.GetPosition(this));
            e.Handled = true;
            return;
        }
        base.OnMouseDown(e);
        if (e.Button == MouseButton.Left
            && !ReferenceEquals(_model.FocusedTabSet, _tabSet))
        {
            _model.DoAction(DockAction.SetActiveTabset(_tabSet.GetId(), _tabSet.LayoutId));
        }
    }

    // The empty tab-strip area (not on a tab/button) is the group's right-click target.
    private bool IsInHeaderStrip(Point local) =>
        _headerHeight > 0 && (HeaderAtBottom ? local.Y >= Bounds.Height - _headerHeight : local.Y <= _headerHeight);

    private void ShowGroupMenu(Point local)
    {
        var menu = new ContextMenu();
        var commands = new CommandScope();
        BuildGroupMenu(menu, commands);
        _context.ConfigureGroupMenu?.Invoke(_tabSet, menu, commands); // host appends app commands
        menu.SetCommandTarget(CommandTarget.From(commands));
        menu.Show(this, new Point(Bounds.X + local.X, Bounds.Y + local.Y));
    }

    private void BuildGroupMenu(ContextMenu menu, CommandScope commands)
    {
        string setId = _tabSet.GetId();
        DockMenuCommands.Add(menu, commands, "floatGroup", MewUIDockString.MenuFloat.Value, () => _model.DoAction(DockAction.PopoutTabset(setId)));
        if (_tabSet.IsDocument)
        {
            if (_tabSet.IsEnableMaximize)
            {
                var label = _tabSet.IsMaximized ? MewUIDockString.MenuRestore.Value : MewUIDockString.MenuMaximize.Value;
                DockMenuCommands.Add(menu, commands, "toggleMaximize", label, () => _model.DoAction(DockAction.MaximizeToggle(setId, _tabSet.LayoutId)));
            }
        }
        else
        {
            DockMenuCommands.Add(menu, commands, "autoHide", MewUIDockString.MenuAutoHide.Value, () => _model.DoAction(DockAction.UnpinTool(setId)));
        }
        menu.AddSeparator();
        bool anyClosable = _tabSet.Children.Any(child => child is TabNode tab && tab.IsEnableClose);
        DockMenuCommands.Add(menu, commands, "closeAll", MewUIDockString.MenuCloseAll.Value, CloseClosableTabs, anyClosable);
    }

    // Close every closable tab in the group; a tab with enableClose=false stays (so the group survives if it holds one).
    private void CloseClosableTabs()
    {
        foreach (var child in _tabSet.Children.ToList())
        {
            if (child is TabNode tab && tab.IsEnableClose)
            {
                _context.Close(tab);
            }
        }
    }

    // Report Focused when this tabset is the active one, so the themed frame highlights toward the accent.
    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        var flags = state.Flags & ~VisualStateFlags.Focused;
        if (ReferenceEquals(_model.FocusedTabSet, _tabSet))
        {
            flags |= VisualStateFlags.Focused;
        }
        return new VisualState { Flags = flags };
    }

    // The tab whose content + highlight the view shows. Normally the model's selected tab, but while that tab is
    // being torn off it falls back to the first other tab so the dragged tab fully disappears - WITHOUT touching the
    // model selection (ESC restores it). Returns null when only the dragged tab remains.
    internal static TabNode? EffectiveSelected(TabSetNode tabSet)
    {
        var selected = tabSet.GetSelectedNode();
        var dragging = tabSet.Model.DraggingNode;
        if (selected is null || !ReferenceEquals(selected, dragging))
        {
            return selected;
        }
        foreach (var child in tabSet.Children)
        {
            if (child is TabNode tab && !ReferenceEquals(tab, dragging))
            {
                return tab;
            }
        }
        return null;
    }

    /// <summary>Re-syncs the tab and frame highlights and the caption after a selection change, in place.</summary>
    internal void SyncSelection()
    {
        (_toolCaption as IToolHeader)?.Refresh();
        foreach (var tab in _tabs)
        {
            tab.InvalidateVisualState();
        }
        InvalidateVisualState();
        InvalidateArrange(); // the active tab is always kept visible, which can change the overflow set
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        if (_toolCaption is not null && !visitor(_toolCaption))
        {
            return false;
        }
        foreach (var tab in _tabs)
        {
            if (!visitor(tab))
            {
                return false;
            }
        }
        if (_maximizeButton is not null && !visitor(_maximizeButton))
        {
            return false;
        }
        return visitor(_overflowButton);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double headerHeight = 0;
        if (ShowTabStrip)
        {
            headerHeight = MinHeaderHeight;
            foreach (var tab in _tabs)
            {
                tab.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                headerHeight = Math.Max(headerHeight, tab.DesiredSize.Height);
            }
            if (_maximizeButton is not null)
            {
                _maximizeButton.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                headerHeight = Math.Max(headerHeight, _maximizeButton.DesiredSize.Height);
            }
            _overflowButton.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            headerHeight = Math.Max(headerHeight, _overflowButton.DesiredSize.Height);
        }
        _headerHeight = headerHeight;

        // The same frame inset ArrangeContent gives the caption and the content.
        double border = GetBorderVisualInset();
        double innerWidth = Math.Max(0, availableSize.Width - 2 * border);
        _toolCaption?.Measure(new Size(innerWidth, double.PositiveInfinity));
        _contentSize = new Size(innerWidth, Math.Max(0, availableSize.Height - _headerHeight - 2 * border - CaptionHeight));
        return availableSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        _tabSet.Rect = bounds;
        double headerY = HeaderAtBottom ? bounds.Bottom - _headerHeight : bounds.Y;
        _tabSet.SetTabStripRect(new Rect(bounds.X, headerY, bounds.Width, _headerHeight));

        if (ShowTabStrip)
        {
            double rightControlsWidth = _maximizeButton?.DesiredSize.Width + 2 ?? 0;
            var visible = ResolveVisibleTabs(bounds.Width - rightControlsWidth);

            double offset = bounds.X;
            foreach (var tab in visible)
            {
                double width = tab.DesiredSize.Width;
                var tabRect = new Rect(offset, headerY, width, _headerHeight);
                tab.Arrange(tabRect);
                tab.Tab.TabRect = tabRect;
                offset += width + TabSpacing;
            }
            // A tab left out of the strip has no place in it: not where it was, for drawing, hit tests or drops.
            foreach (var hidden in _hiddenTabs)
            {
                hidden.Arrange(Rect.Empty);
                hidden.Tab.TabRect = Rect.Empty;
            }

            // Right-aligned controls: maximize rightmost, overflow dropdown to its left (only when overflow is active).
            double rightEdge = bounds.Right;
            if (_maximizeButton is not null)
            {
                double width = _maximizeButton.DesiredSize.Width;
                double height = _maximizeButton.DesiredSize.Height;
                _maximizeButton.Arrange(new Rect(rightEdge - width - 2, headerY + (_headerHeight - height) / 2, width, height));
                rightEdge -= width + 2;
            }
            if (_overflowActive)
            {
                double width = _overflowButton.DesiredSize.Width;
                double height = _overflowButton.DesiredSize.Height;
                _overflowButton.Arrange(new Rect(rightEdge - width - 2, headerY + (_headerHeight - height) / 2, width, height));
            }
            else
            {
                _overflowButton.Arrange(Rect.Empty);
            }
        }
        else
        {
            // A lone tool's group shows no strip: its tab and buttons take no place.
            _overflowActive = false;
            foreach (var tab in _tabs)
            {
                tab.Arrange(Rect.Empty);
                tab.Tab.TabRect = Rect.Empty;
            }
            _maximizeButton?.Arrange(Rect.Empty);
            _overflowButton.Arrange(Rect.Empty);
        }

        double contentTop = HeaderAtBottom ? bounds.Y : bounds.Y + _headerHeight;
        var contentRect = new Rect(bounds.X, contentTop, bounds.Width, Math.Max(0, bounds.Height - _headerHeight));
        _tabSet.SetContentRect(contentRect);

        // Inset the hosted content inside the frame border on ALL sides so the 1px border is not covered by it; the
        // active tab still connects because the strip-side border is erased under it by the pierce. Use the visual
        // (device-snapped) border inset, not the logical thickness, so the content lines up with the snapped frame
        // at fractional DPI (e.g. 150%) instead of leaving a 1px gap.
        double border = GetBorderVisualInset();
        var snapped = GetSnappedBorderBounds(bounds);
        double innerTop = HeaderAtBottom ? snapped.Y + border : snapped.Y + _headerHeight + border;
        var inner = new Rect(
            snapped.X + border,
            innerTop,
            Math.Max(0, snapped.Width - 2 * border),
            Math.Max(0, snapped.Height - _headerHeight - 2 * border));

        // Tool caption bar sits at the top of the framed area; the content fills below it.
        double captionHeight = CaptionHeight;
        _toolCaption?.Arrange(new Rect(inner.X, inner.Y, inner.Width, Math.Min(captionHeight, inner.Height)));
        _contentArea = new Rect(inner.X, inner.Y + captionHeight, inner.Width, Math.Max(0, inner.Height - captionHeight));
    }

    // Computes which tabs fit in availableForTabs; the leading run is shown and the active tab is always kept
    // visible. Populates _hiddenTabs / _overflowActive for the render + hit-test overrides (port of golden-layout).
    private List<FlexTabButton> ResolveVisibleTabs(double availableForTabs)
    {
        _hiddenTabs.Clear();

        // Tear-off: the tab being dragged is hidden from the strip (its button stays alive for the drag); the rest
        // reflow to close the gap. ESC/cancel clears DraggingNode and the tab reappears.
        var active = new List<FlexTabButton>();
        foreach (var tab in _tabs)
        {
            if (ReferenceEquals(tab.Tab, _model.DraggingNode))
            {
                _hiddenTabs.Add(tab);
            }
            else
            {
                active.Add(tab);
            }
        }

        double total = 0;
        for (int index = 0; index < active.Count; index++)
        {
            total += active[index].DesiredSize.Width + (index > 0 ? TabSpacing : 0);
        }

        if (active.Count <= 1 || total <= availableForTabs)
        {
            _overflowActive = false;
            return active;
        }

        availableForTabs -= _overflowButton.DesiredSize.Width + TabSpacing;

        int fitCount = 0;
        double accumulated = 0;
        for (int index = 0; index < active.Count; index++)
        {
            double step = active[index].DesiredSize.Width + (index > 0 ? TabSpacing : 0);
            if (accumulated + step > availableForTabs)
            {
                break;
            }
            accumulated += step;
            fitCount++;
        }

        int activeIndex = -1;
        for (int index = 0; index < active.Count; index++)
        {
            if (active[index].IsActive)
            {
                activeIndex = index;
                break;
            }
        }

        int leadCount = fitCount;
        if (activeIndex >= leadCount && leadCount > 0)
        {
            leadCount--;
        }

        var visible = new List<FlexTabButton>();
        for (int index = 0; index < active.Count; index++)
        {
            if (index < leadCount || index == activeIndex)
            {
                visible.Add(active[index]);
            }
            else
            {
                _hiddenTabs.Add(active[index]);
            }
        }

        if (visible.Count == 0 && active.Count > 0)
        {
            var fallback = activeIndex >= 0 ? active[activeIndex] : active[0];
            visible.Add(fallback);
            _hiddenTabs.Remove(fallback);
        }

        _overflowActive = _hiddenTabs.Count > 0;
        return visible;
    }

    private void ShowOverflowMenu()
    {
        if (_hiddenTabs.Count == 0)
        {
            return;
        }
        var menu = new ContextMenu();
        var commands = new CommandScope();
        foreach (var tab in _tabs)
        {
            if (!_hiddenTabs.Contains(tab))
            {
                continue;
            }
            var node = tab.Tab;
            DockMenuCommands.Add(menu, commands, "selectTab", node.Name ?? MewUIDockString.TitleUnnamedTab.Value,
                () => _model.DoAction(DockAction.SelectTab(node.GetId())));
        }
        menu.SetCommandTarget(CommandTarget.From(commands));
        menu.Placement = MenuPlacement.Below;
        menu.Show(_overflowButton);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }
        if (_toolCaption?.HitTest(point) is UIElement captionHit)
        {
            return captionHit;
        }
        if (ShowTabStrip)
        {
            if (_maximizeButton?.HitTest(point) is UIElement maximizeHit)
            {
                return maximizeHit;
            }
            if (_overflowActive && _overflowButton.HitTest(point) is UIElement overflowHit)
            {
                return overflowHit;
            }
            for (int index = _tabs.Count - 1; index >= 0; index--)
            {
                if (_hiddenTabs.Contains(_tabs[index]))
                {
                    continue;
                }
                if (_tabs[index].HitTest(point) is UIElement tabHit)
                {
                    return tabHit;
                }
            }
        }
        return Bounds.Contains(point) ? this : null;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Header strip background (the selected tab takes the same container colour so it reads as continuous).
        double headerY = HeaderAtBottom ? Bounds.Bottom - _headerHeight : Bounds.Y;
        context.FillRectangle(new Rect(Bounds.X, headerY, Bounds.Width, _headerHeight), Theme.Palette.ContainerBackground);

        var snapped = GetSnappedBorderBounds(Bounds);
        double bodyY = HeaderAtBottom ? snapped.Y : snapped.Y + _headerHeight;
        var body = new Rect(snapped.X, bodyY, snapped.Width, Math.Max(0, snapped.Height - _headerHeight));
        if (body.Width <= 0 || body.Height <= 0)
        {
            return;
        }

        var background = GetValue(BackgroundProperty);
        var outline = GetValue(BorderBrushProperty);
        double radius = CornerRadius;

        // Round away from the tab strip (tabs on top -> bottom rounded; tabs on bottom -> top rounded); snapped crisp.
        var corner = HeaderAtBottom ? new CornerRadius(radius, radius, 0, 0) : new CornerRadius(0, 0, radius, radius);
        DrawBackgroundAndBorder(context, body, background, outline, new Thickness(BorderThickness), corner);

        if (ShowTabStrip)
        {
            PierceActiveTabBorder(context, body, background);
        }
    }

    // Erase the content frame's TOP border under the active tab (fill it with the frame background) so the
    // active tab opens into the content area instead of being boxed off (port of StackView.PierceActiveTabBorder).
    private void PierceActiveTabBorder(IGraphicsContext context, Rect body, Color background)
    {
        double thickness = GetBorderVisualInset();
        if (thickness <= 0)
        {
            return;
        }

        FlexTabButton? activeTab = null;
        foreach (var tab in _tabs)
        {
            if (tab.IsActive && !_hiddenTabs.Contains(tab))
            {
                activeTab = tab;
                break;
            }
        }

        if (activeTab is null || activeTab.Bounds.Width <= 0)
        {
            return;
        }

        double gapLeft = Math.Clamp(activeTab.Bounds.Left + thickness, body.X, body.Right);
        double gapRight = Math.Clamp(activeTab.Bounds.Right - thickness, body.X, body.Right);
        if (gapRight <= gapLeft)
        {
            return;
        }

        // Pierce the strip-side edge (top for top tabs, bottom for bottom/tool tabs); seam centred on the edge.
        double edge = HeaderAtBottom ? body.Bottom : body.Y;
        var seam = new Rect(gapLeft, edge - thickness, gapRight - gapLeft, thickness * 2);
        context.FillRectangle(seam, background);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        _toolCaption?.Render(context);

        if (ShowTabStrip)
        {
            foreach (var tab in _tabs)
            {
                if (!_hiddenTabs.Contains(tab))
                {
                    tab.Render(context);
                }
            }

            _maximizeButton?.Render(context);
            if (_overflowActive)
            {
                _overflowButton.Render(context);
            }
        }
    }
}
