using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// Renders a <see cref="TabSetNode"/> as a tab pane: a tab-button strip on top (with a maximize control and an
/// overflow dropdown) and the selected tab's content in a framed body below. Sets the node's rects (Rect /
/// TabStripRect / ContentRect, plus each tab's TabRect) for drop hit-testing.
/// </summary>
internal sealed class FlexTabSetView : Control, IVisualTreeHost
{
    private const double MinHeaderHeight = 26;
    private const double TabSpacing = 2;

    private readonly TabSetNode _tabSet;
    private readonly FlexViewContext _context;
    private readonly List<FlexTabButton> _tabs = new();
    private readonly Button? _maximizeButton;
    private readonly Button _overflowButton;
    private readonly HashSet<FlexTabButton> _hiddenTabs = new();
    private bool _overflowActive;
    private UIElement? _content;
    private readonly UIElement? _toolCaption;
    private double _headerHeight = MinHeaderHeight;

    public FlexTabSetView(TabSetNode tabSet, FlexViewContext context)
    {
        _tabSet = tabSet;
        _context = context;
        tabSet.View = this;

        // A pane tabset wears a caption bar (title + pin/close/menu) above its content; the Extended layer supplies it.
        if (!tabSet.IsDocument && context.ToolHeader?.Invoke(tabSet) is UIElement caption)
        {
            _toolCaption = caption;
            AttachChild(_toolCaption);
        }

        BuildTabs();
        SyncContent();

        // The maximize/restore control is a real flat button (captures its own click, so pressing it never
        // starts the header drag). The glyph reflects the current state; a toggle rebuilds and recreates it.
        // Pane tabsets have no maximize button.
        if (tabSet.IsEnableMaximize && tabSet.IsDocument)
        {
            _maximizeButton = new Button
            {
                Content = new GlyphElement { Kind = tabSet.IsMaximized ? GlyphKind.WindowRestore : GlyphKind.WindowMaximize },
                StyleName = BuiltInStyles.FlatButton,
                Padding = new Thickness(0),
                MinWidth = 18,
                MinHeight = 18,
            };
            _maximizeButton.Click += () => _tabSet.Model.DoAction(DockAction.MaximizeToggle(_tabSet.GetId()));
            _maximizeButton.ToolTip = new TextBlock().BindText(
                tabSet.IsMaximized ? MewUIDockString.ToolTipRestore : MewUIDockString.ToolTipMaximize);
            AttachChild(_maximizeButton);
        }

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
    }

    // A pane tabset puts its tab strip at the bottom; document tabsets keep it on top.
    private bool HeaderAtBottom => !_tabSet.IsDocument;

    // A single-tool group hides its tab strip: the caption already names it, so the lone tab button is redundant
    // (matches VS). Document tabsets and multi-tab tool groups always show the strip.
    private bool ShowTabStrip => _tabSet.IsDocument || _tabSet.Children.Count > 1;

    private double CaptionHeight => _toolCaption?.DesiredSize.Height ?? 0;

    protected override void OnDragStarting(DragStartingEventArgs e)
    {
        base.OnDragStarting(e);

        // Only the empty header strip drags the whole tabset; a drag from the content area (the press bubbled up
        // here because the content is not itself a drag source) must NOT start a dock drag. The strip is at the
        // bottom for tool groups, so test its actual location rather than assuming the top.
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

        _tabSet.Model.SetDraggingNode(_tabSet); // tear-off: hide this whole tabset until drop / cancel
    }

    // Released over no drop target (empty space / outside all windows): pop the whole tabset out into a new window.
    protected override void OnDragCompleted(DragCompletedEventArgs e)
    {
        base.OnDragCompleted(e);
        _tabSet.Model.SetDraggingNode(null);
        if (!e.WasCanceled && e.FinalEffect != DragDropEffects.Move)
        {
            // Pass the PHYSICAL cursor position; SyncPopouts converts it to DIPs at placement (mixed-DPI safe).
            _tabSet.Model.DoAction(DockAction.PopoutTabset(_tabSet.GetId(), position: e.ScreenPosition));
        }
    }

    // Clicking anywhere in the tabset (header or content) makes it the active tabset (focus highlight). The
    // mouse-down bubbles up here from the content; selecting a tab already activates it via SelectTab.
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
            && !ReferenceEquals(_tabSet.Model.FocusedTabSet, _tabSet))
        {
            _tabSet.Model.DoAction(DockAction.SetActiveTabset(_tabSet.GetId(), _tabSet.LayoutId));
        }
    }

    // The empty tab-strip area (not on a tab/button) is the group's right-click target.
    private bool IsInHeaderStrip(Point local) =>
        _headerHeight > 0 && (HeaderAtBottom ? local.Y >= Bounds.Height - _headerHeight : local.Y <= _headerHeight);

    private void ShowGroupMenu(Point local)
    {
        var menu = new ContextMenu();
        BuildGroupMenu(menu);
        _context.ConfigureGroupMenu?.Invoke(_tabSet, menu); // host appends app commands
        menu.ShowAt(this, new Point(Bounds.X + local.X, Bounds.Y + local.Y));
    }

    private void BuildGroupMenu(ContextMenu menu)
    {
        var model = _tabSet.Model;
        string setId = _tabSet.GetId();
        menu.AddItem(MewUIDockString.MenuFloat.Value, () => model.DoAction(DockAction.PopoutTabset(setId)));
        if (_tabSet.IsDocument)
        {
            if (_tabSet.IsEnableMaximize)
            {
                var label = _tabSet.IsMaximized ? MewUIDockString.MenuRestore.Value : MewUIDockString.MenuMaximize.Value;
                menu.AddItem(label, () => model.DoAction(DockAction.MaximizeToggle(setId)));
            }
        }
        else
        {
            menu.AddItem(MewUIDockString.MenuAutoHide.Value, () => model.DoAction(DockAction.UnpinTool(setId)));
        }
        menu.AddSeparator();
        bool anyClosable = _tabSet.Children.Any(c => c is TabNode tab && tab.IsEnableClose);
        menu.AddItem(MewUIDockString.MenuCloseAll.Value, CloseClosableTabs, anyClosable);
    }

    // Close every closable tab in the group; a tab with enableClose=false stays (so the group survives if it holds one).
    private void CloseClosableTabs()
    {
        foreach (var child in _tabSet.Children.ToList())
        {
            if (child is TabNode tab && tab.IsEnableClose)
            {
                _tabSet.Model.DoAction(DockAction.DeleteTab(tab.GetId()));
            }
        }
    }

    // Report Focused when this tabset is the active one, so the themed frame highlights toward the accent.
    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        var flags = state.Flags & ~VisualStateFlags.Focused;
        if (ReferenceEquals(_tabSet.Model.FocusedTabSet, _tabSet))
        {
            flags |= VisualStateFlags.Focused;
        }
        return new VisualState { Flags = flags };
    }

    private void BuildTabs()
    {
        foreach (var tab in _tabs)
        {
            DetachChild(tab);
        }
        _tabs.Clear();
        foreach (var child in _tabSet.Children)
        {
            var button = new FlexTabButton((TabNode)child, _tabSet, _context);
            _tabs.Add(button);
            AttachChild(button);
        }
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

    private void SyncContent()
    {
        var activeTab = EffectiveSelected(_tabSet);
        var newContent = activeTab is not null ? _context.Content(activeTab) : null;
        if (ReferenceEquals(newContent, _content))
        {
            return;
        }
        if (_content is not null)
        {
            DetachChild(_content);
        }
        _content = newContent;
        if (_content is not null && _content.Parent is null)
        {
            AttachChild(_content);
        }
        InvalidateMeasure();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        if (newRoot is null)
        {
            ReleaseContent();
        }
    }

    private void ReleaseContent()
    {
        if (_content is null)
        {
            return;
        }

        DetachChild(_content);
        _content = null;
    }

    /// <summary>Re-syncs the hosted content and tab/frame highlights after a selection or active-tabset change,
    /// without recreating controls (so an in-flight drag source survives).</summary>
    internal void SyncSelection()
    {
        SyncContent();
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
        if (!visitor(_overflowButton))
        {
            return false;
        }
        return _content is null || visitor(_content);
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

        _toolCaption?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        _content?.Measure(new Size(availableSize.Width, Math.Max(0, availableSize.Height - _headerHeight - CaptionHeight)));
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
        }
        else
        {
            _overflowActive = false;
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
        double captionH = CaptionHeight;
        _toolCaption?.Arrange(new Rect(inner.X, inner.Y, inner.Width, Math.Min(captionH, inner.Height)));
        _content?.Arrange(new Rect(inner.X, inner.Y + captionH, inner.Width, Math.Max(0, inner.Height - captionH)));
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
            if (ReferenceEquals(tab.Tab, _tabSet.Model.DraggingNode))
            {
                _hiddenTabs.Add(tab);
            }
            else
            {
                active.Add(tab);
            }
        }

        double total = 0;
        for (int i = 0; i < active.Count; i++)
        {
            total += active[i].DesiredSize.Width + (i > 0 ? TabSpacing : 0);
        }

        if (active.Count <= 1 || total <= availableForTabs)
        {
            _overflowActive = false;
            return active;
        }

        availableForTabs -= _overflowButton.DesiredSize.Width + TabSpacing;

        int fitCount = 0;
        double accumulated = 0;
        for (int i = 0; i < active.Count; i++)
        {
            double step = active[i].DesiredSize.Width + (i > 0 ? TabSpacing : 0);
            if (accumulated + step > availableForTabs)
            {
                break;
            }
            accumulated += step;
            fitCount++;
        }

        int activeIndex = -1;
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].IsActive)
            {
                activeIndex = i;
                break;
            }
        }

        int leadCount = fitCount;
        if (activeIndex >= leadCount && leadCount > 0)
        {
            leadCount--;
        }

        var visible = new List<FlexTabButton>();
        for (int i = 0; i < active.Count; i++)
        {
            if (i < leadCount || i == activeIndex)
            {
                visible.Add(active[i]);
            }
            else
            {
                _hiddenTabs.Add(active[i]);
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
        foreach (var tab in _tabs)
        {
            if (!_hiddenTabs.Contains(tab))
            {
                continue;
            }
            var node = tab.Tab;
            menu.AddItem(node.Name ?? MewUIDockString.TitleUnnamedTab.Value, () => _tabSet.Model.DoAction(DockAction.SelectTab(node.GetId())));
        }
        var buttonBounds = _overflowButton.Bounds;
        menu.ShowAt(_overflowButton, new Point(buttonBounds.X, buttonBounds.Bottom));
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
            for (int i = _tabs.Count - 1; i >= 0; i--)
            {
                if (_hiddenTabs.Contains(_tabs[i]))
                {
                    continue;
                }
                if (_tabs[i].HitTest(point) is UIElement tabHit)
                {
                    return tabHit;
                }
            }
        }
        if (_content?.HitTest(point) is UIElement contentHit)
        {
            return contentHit;
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

        if (_content is not null)
        {
            // Clip to the content's own arranged rect (inside the frame border, below the caption) so it never bleeds
            // over the border or caption.
            context.Save();
            context.SetClip(_content.Bounds);
            try
            {
                _content.Render(context);
            }
            finally
            {
                context.Restore();
            }
        }
    }
}
