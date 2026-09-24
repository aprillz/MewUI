using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// Renders one <see cref="BorderNode"/>: a collapsed button strip along its edge plus, when a tab is selected,
/// an expanded panel whose content the <see cref="PaneLayer"/> shows, with a draggable splitter at the panel's inner
/// edge to resize it. It follows the border's tabs, selection and size in place. <see cref="FlexLayoutView"/>
/// reserves <see cref="Footprint"/> (bar + panel) at the edge.
/// </summary>
internal class FlexBorderBar : Control, IVisualTreeHost, IPaneContentOwner
{
    // protected so the Extended docking layer (ExtendedBorderBar) can subclass and reuse the strip/panel/splitter
    // plumbing while overriding the layout.
    protected const double BarThickness = 26;
    protected const double ButtonSpacing = 2;

    protected readonly BorderNode _border;
    protected readonly FlexViewContext _context;
    protected readonly List<FlexBorderButton> _buttons = new();
    protected readonly FlexSplitter _splitter;
    private readonly HashSet<TabNode> _observedTabs = new();
    private bool _released;

    public FlexBorderBar(BorderNode border, FlexViewContext context)
    {
        _border = border;
        _context = context;
        border.View = this;

        _splitter = new FlexSplitter
        {
            IsColumnAxis = border.Location is DockLocation.Top or DockLocation.Bottom,
            BarThickness = border.Model.SplitterSize,
        };
        _splitter.SplitterDragging += OnSplitterDragging;
        _splitter.SplitterDragCompleted += OnSplitterDragCompleted;
        AttachChild(_splitter);

        border.ChildInserted += OnTabsChanged;
        border.ChildRemoved += OnTabsChanged;
        border.PropertyChanged += OnBorderPropertyChanged;
        SyncButtons();
    }

    internal BorderNode Border => _border;

    public DockLocation Location => _border.Location;

    /// <summary>The size the revealed tab's content gets; set when the panel is measured.</summary>
    protected Size ContentSize { get; set; }

    /// <summary>Where the revealed tab's content goes; set when the panel is arranged.</summary>
    protected Rect ContentArea { get; set; } = Rect.Empty;

    Size IPaneContentOwner.ContentSize => ContentSize;

    Rect IPaneContentOwner.ContentArea => ContentArea;

    protected bool Horizontal => _border.Location is DockLocation.Top or DockLocation.Bottom;

    protected bool Expanded => _border.Selected != -1;

    protected double PanelSize => Expanded ? Math.Max(0, _border.GetSize()) : 0;

    // Always reserve the splitter-sized gap between the strip and the centre (the draggable grip only renders when
    // expanded; collapsed it is just empty spacing).
    protected double SplitterSize => _border.Model.SplitterSize;

    // Strip + panel + a splitter gap between the panel and the central content (port of FlexLayout's border layout).
    public virtual double Footprint => BarThickness + PanelSize + SplitterSize;

    // Extra extent (beyond Footprint, toward the centre) the bar paints OVER the document content without reserving
    // space. Default 0 (faithful borders push content); the Extended auto-hide reveal overlays its panel here.
    public virtual double OverlayExtent => 0;

    // Override point: the Extended layer creates a horizontal (non-rotated) button for the bottom strip.
    protected virtual FlexBorderButton CreateButton(TabNode tab) => new(tab, _border, _context);

    /// <summary>Stops following the border; the bar is not used again.</summary>
    internal void Release()
    {
        if (_released)
        {
            return;
        }
        _released = true;
        _border.ChildInserted -= OnTabsChanged;
        _border.ChildRemoved -= OnTabsChanged;
        _border.PropertyChanged -= OnBorderPropertyChanged;
        foreach (var tab in _observedTabs)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
        }
        _observedTabs.Clear();
        foreach (var button in _buttons)
        {
            button.ReleaseHeader();
            DetachChild(button);
        }
        _buttons.Clear();
        if (ReferenceEquals(_border.View, this))
        {
            _border.View = null;
        }
    }

    private void OnTabsChanged(Node tab, int index) => SyncButtons();

    private void OnBorderPropertyChanged(Node node, NodeProperty property)
    {
        switch (property)
        {
            case NodeProperty.Selected:
                SyncSelection();
                // Opening or closing the panel changes the space the layout gives everything else.
                FindLayoutView()?.InvalidateMeasure();
                break;
            case NodeProperty.Size:
                FindLayoutView()?.InvalidateMeasure();
                break;
        }
    }

    private void OnTabPropertyChanged(Node tab, NodeProperty property)
    {
        if (property == NodeProperty.Name)
        {
            RefreshNames();
        }
    }

    /// <summary>Makes the strip hold one button per tab in the border's order, keeping the buttons of tabs that stay.</summary>
    private void SyncButtons()
    {
        if (_released)
        {
            return;
        }

        foreach (var tab in _observedTabs.ToList())
        {
            if (!ReferenceEquals(tab.Parent, _border))
            {
                tab.PropertyChanged -= OnTabPropertyChanged;
                _observedTabs.Remove(tab);
            }
        }

        var buttons = new List<FlexBorderButton>(_border.Children.Count);
        foreach (var child in _border.Children)
        {
            var tab = (TabNode)child;
            if (_observedTabs.Add(tab))
            {
                tab.PropertyChanged += OnTabPropertyChanged;
            }
            var button = _buttons.Find(candidate => ReferenceEquals(candidate.Tab, tab));
            if (button is null)
            {
                button = CreateButton(tab);
                AttachChild(button);
            }
            buttons.Add(button);
        }
        foreach (var button in _buttons)
        {
            if (!buttons.Contains(button))
            {
                button.ReleaseHeader();
                DetachChild(button);
            }
        }
        _buttons.Clear();
        _buttons.AddRange(buttons);
        SyncSelection();
    }

    /// <summary>Re-syncs the button highlights and re-lays out (the panel size may have changed) on selection.</summary>
    internal virtual void SyncSelection()
    {
        foreach (var button in _buttons)
        {
            button.InvalidateVisualState();
        }
        InvalidateMeasure();
    }

    /// <summary>Shows the tabs' current names on the strip and the panel, without recreating controls.</summary>
    internal virtual void RefreshNames()
    {
        foreach (var button in _buttons)
        {
            button.RefreshName();
        }
        InvalidateMeasure();
    }

    // Live border resize: the size changes as the splitter moves (each change re-lays out the layout, which re-carves
    // this border's footprint) and is committed once, when the drag ends.
    protected void OnSplitterDragging(MouseEventArgs e)
    {
        if (FindVisualRoot() is not UIElement root)
        {
            return;
        }
        var position = e.GetPosition(root);
        double splitterPos = Horizontal ? position.Y : position.X;
        _border.SetSize(_border.CalculateSplit(splitterPos));
    }

    private void OnSplitterDragCompleted() =>
        _border.Model.DoAction(DockAction.AdjustBorderSplit(_border.GetId(), _border.GetSize()));

    protected FlexLayoutView? FindLayoutView()
    {
        Element? node = Parent;
        while (node is not null)
        {
            if (node is FlexLayoutView layoutView)
            {
                return layoutView;
            }
            node = node.Parent;
        }
        return null;
    }

    // Explicit interface impl cannot be overridden; delegate to a protected virtual so the Extended layer can add
    // its caption + bottom strip to the visual-tree walk.
    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => VisitChildrenCore(visitor);

    protected virtual bool VisitChildrenCore(Func<Element, bool> visitor)
    {
        foreach (var button in _buttons)
        {
            if (!visitor(button))
            {
                return false;
            }
        }
        return visitor(_splitter);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // Buttons are measured with the strip they are arranged in.
        var strip = Horizontal ? new Size(availableSize.Width, BarThickness) : new Size(BarThickness, availableSize.Height);
        foreach (var button in _buttons)
        {
            button.Measure(strip);
        }
        _splitter.Measure(availableSize);
        double panel = PanelSize;
        MeasurePanel(Horizontal ? new Size(availableSize.Width, panel) : new Size(panel, availableSize.Height));
        return availableSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        double panel = PanelSize;
        double splitter = SplitterSize;
        Rect barRect;
        Rect panelRect;
        Rect splitterRect;
        switch (_border.Location)
        {
            case DockLocation.Top:
                barRect = new Rect(bounds.X, bounds.Y, bounds.Width, BarThickness);
                panelRect = new Rect(bounds.X, bounds.Y + BarThickness, bounds.Width, panel);
                splitterRect = new Rect(bounds.X, bounds.Y + BarThickness + panel, bounds.Width, splitter);
                break;
            case DockLocation.Bottom:
                barRect = new Rect(bounds.X, bounds.Bottom - BarThickness, bounds.Width, BarThickness);
                panelRect = new Rect(bounds.X, bounds.Bottom - BarThickness - panel, bounds.Width, panel);
                splitterRect = new Rect(bounds.X, bounds.Bottom - BarThickness - panel - splitter, bounds.Width, splitter);
                break;
            case DockLocation.Left:
                barRect = new Rect(bounds.X, bounds.Y, BarThickness, bounds.Height);
                panelRect = new Rect(bounds.X + BarThickness, bounds.Y, panel, bounds.Height);
                splitterRect = new Rect(bounds.X + BarThickness + panel, bounds.Y, splitter, bounds.Height);
                break;
            case DockLocation.Right:
                barRect = new Rect(bounds.Right - BarThickness, bounds.Y, BarThickness, bounds.Height);
                panelRect = new Rect(bounds.Right - BarThickness - panel, bounds.Y, panel, bounds.Height);
                splitterRect = new Rect(bounds.Right - BarThickness - panel - splitter, bounds.Y, splitter, bounds.Height);
                break;
            default:
                throw new ArgumentException();
        }

        _border.SetTabHeaderRect(barRect);
        _border.SetContentRect(panelRect);

        if (Horizontal)
        {
            double offset = barRect.X;
            foreach (var button in _buttons)
            {
                double width = button.DesiredSize.Width;
                var buttonRect = new Rect(offset, barRect.Y, width, barRect.Height);
                button.Arrange(buttonRect);
                button.Tab.TabRect = buttonRect; // drop-target insertion math reads each tab's TabRect
                offset += width + ButtonSpacing;
            }
        }
        else
        {
            double offset = barRect.Y;
            foreach (var button in _buttons)
            {
                double height = button.DesiredSize.Height;
                var buttonRect = new Rect(barRect.X, offset, barRect.Width, height);
                button.Arrange(buttonRect);
                button.Tab.TabRect = buttonRect;
                offset += height + ButtonSpacing;
            }
        }

        if (panel > 0)
        {
            ArrangePanel(panelRect);
        }
        else
        {
            ContentArea = Rect.Empty;
        }

        // The splitter only shows while the panel is open; closed, it takes no place.
        _splitter.Arrange(Expanded ? splitterRect : Rect.Empty);
    }

    /// <summary>Works out the size the revealed content gets, the one <see cref="ArrangePanel"/> places it in.</summary>
    protected virtual void MeasurePanel(Size panelSize)
    {
        double border = Theme.Metrics.ControlBorderThickness;
        ContentSize = new Size(Math.Max(0, panelSize.Width - 2 * border), Math.Max(0, panelSize.Height - 2 * border));
    }

    /// <summary>Works out where the revealed content goes: inside the panel's frame border.</summary>
    protected virtual void ArrangePanel(Rect panelRect)
    {
        double border = Theme.Metrics.ControlBorderThickness;
        ContentArea = new Rect(
            panelRect.X + border,
            panelRect.Y + border,
            Math.Max(0, panelRect.Width - 2 * border),
            Math.Max(0, panelRect.Height - 2 * border));
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }
        if (Expanded && _splitter.HitTest(point) is UIElement splitterHit)
        {
            return splitterHit;
        }
        for (int index = _buttons.Count - 1; index >= 0; index--)
        {
            if (_buttons[index].HitTest(point) is UIElement buttonHit)
            {
                return buttonHit;
            }
        }
        return Bounds.Contains(point) ? this : null;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        context.FillRectangle(_border.TabHeaderRect, Theme.Palette.ContainerBackground);

        if (Expanded)
        {
            var panelRect = _border.ContentRect;
            if (panelRect.Width > 0 && panelRect.Height > 0)
            {
                double r = Theme.Metrics.ControlCornerRadius;
                double t = Theme.Metrics.ControlBorderThickness;
                // Fully closed border on all sides; only the rounding differs by direction (round the centre-facing
                // corners, square on the bar side).
                var corner = _border.Location switch
                {
                    DockLocation.Bottom => new CornerRadius(r, r, 0, 0), // centre above -> round top
                    DockLocation.Top => new CornerRadius(0, 0, r, r),    // centre below -> round bottom
                    DockLocation.Left => new CornerRadius(0, r, r, 0),   // centre right -> round right
                    _ => new CornerRadius(r, 0, 0, r),                   // Right: centre left -> round left
                };
                // Pixel-snap the frame so the 1px border stays crisp; ContainerBackground matches the selected button.
                var snapped = GetSnappedBorderBounds(panelRect);
                DrawBackgroundAndBorder(context, snapped,
                    Theme.Palette.ContainerBackground, Theme.Palette.ControlBorder, new Thickness(t), corner);
                PierceSelectedTab(context, snapped, Theme.Palette.ContainerBackground);
            }
        }
    }

    // Erase the panel's bar-facing border under the selected button so it connects into the panel (port of the
    // tabset's PierceActiveTabBorder, adapted to each border edge).
    private void PierceSelectedTab(IGraphicsContext context, Rect panelRect, Color background)
    {
        int selected = _border.Selected;
        if (selected < 0 || selected >= _buttons.Count)
        {
            return;
        }
        double thickness = GetBorderVisualInset();
        if (thickness <= 0)
        {
            return;
        }
        var sel = _buttons[selected].Bounds;
        if (sel.Width <= 0 || sel.Height <= 0)
        {
            return;
        }

        Rect seam;
        if (_border.Location is DockLocation.Top or DockLocation.Bottom)
        {
            double gapLeft = Math.Clamp(sel.Left + thickness, panelRect.X, panelRect.Right);
            double gapRight = Math.Clamp(sel.Right - thickness, panelRect.X, panelRect.Right);
            if (gapRight <= gapLeft)
            {
                return;
            }
            double edgeY = _border.Location == DockLocation.Bottom ? panelRect.Bottom : panelRect.Y;
            // Centre the seam on the edge so it covers the border whether it sits inside (Top) or outside (Bottom) it.
            seam = new Rect(gapLeft, edgeY - thickness, gapRight - gapLeft, thickness * 2);
        }
        else
        {
            double gapTop = Math.Clamp(sel.Top + thickness, panelRect.Y, panelRect.Bottom);
            double gapBottom = Math.Clamp(sel.Bottom - thickness, panelRect.Y, panelRect.Bottom);
            if (gapBottom <= gapTop)
            {
                return;
            }
            double edgeX = _border.Location == DockLocation.Right ? panelRect.Right : panelRect.X;
            seam = new Rect(edgeX - thickness, gapTop, thickness * 2, gapBottom - gapTop);
        }
        context.FillRectangle(seam, background);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        foreach (var button in _buttons)
        {
            button.Render(context);
        }
        if (Expanded)
        {
            _splitter.Render(context);
        }
    }
}
