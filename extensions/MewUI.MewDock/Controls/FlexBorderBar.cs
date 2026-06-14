using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// Renders one <see cref="BorderNode"/>: a collapsed button strip along its edge plus, when a tab is selected,
/// an expanded panel hosting that tab's content, with a draggable splitter at the panel's inner edge to resize
/// it. <see cref="FlexLayoutView"/> reserves <see cref="Footprint"/> (bar + panel) at the edge.
/// </summary>
internal class FlexBorderBar : Control, IVisualTreeHost
{
    // protected so the Extended docking layer (ExtendedBorderBar) can subclass and reuse the strip/panel/splitter
    // plumbing while overriding the layout.
    protected const double BarThickness = 26;
    protected const double ButtonSpacing = 2;

    protected readonly BorderNode _border;
    protected readonly FlexViewContext _context;
    protected readonly List<FlexBorderButton> _buttons = new();
    protected readonly FlexSplitter _splitter;
    protected UIElement? _content;

    public FlexBorderBar(BorderNode border, FlexViewContext context)
    {
        _border = border;
        _context = context;
        border.View = this;
        BuildButtons();
        SyncContent();

        _splitter = new FlexSplitter
        {
            IsColumnAxis = border.Location is DockLocation.Top or DockLocation.Bottom,
            BarThickness = border.Model.SplitterSize,
        };
        _splitter.SplitterDragging += OnSplitterDragging;
        AttachChild(_splitter);
    }

    public override bool Focusable => false;

    internal BorderNode Border => _border;

    public DockLocation Location => _border.Location;

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

    protected void BuildButtons()
    {
        foreach (var button in _buttons)
        {
            DetachChild(button);
        }
        _buttons.Clear();
        foreach (var child in _border.Children)
        {
            var button = CreateButton((TabNode)child);
            _buttons.Add(button);
            AttachChild(button);
        }
    }

    protected void SyncContent()
    {
        var selected = _border.GetSelectedNode();
        var newContent = selected is not null ? _context.Content(selected) : null;
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

    /// <summary>Re-syncs the panel + button highlights and re-arranges (panel size may have changed) on selection.</summary>
    internal virtual void SyncSelection()
    {
        SyncContent();
        foreach (var button in _buttons)
        {
            button.InvalidateVisualState();
        }
        InvalidateArrange();
    }

    // Live border resize: compute the new size from the splitter position and re-arrange the layout (which
    // re-carves this border's footprint) without a rebuild, so the splitter keeps its mouse capture.
    protected void OnSplitterDragging(MouseEventArgs e)
    {
        if (FindVisualRoot() is not UIElement root)
        {
            return;
        }
        var position = e.GetPosition(root);
        double splitterPos = Horizontal ? position.Y : position.X;
        _border.SetSize(_border.CalculateSplit(splitterPos));
        FindLayoutView()?.InvalidateArrange();
    }

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
        if (!visitor(_splitter))
        {
            return false;
        }
        return _content is null || visitor(_content);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        foreach (var button in _buttons)
        {
            button.Measure(availableSize);
        }
        _splitter.Measure(availableSize);
        _content?.Measure(availableSize);
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

        if (Expanded)
        {
            _splitter.Arrange(splitterRect);
        }
    }

    // Arranges the expanded panel's contents within panelRect. Default = the hosted content inset inside the frame
    // border. The Extended layer overrides this to add a caption above the content.
    protected virtual void ArrangePanel(Rect panelRect)
    {
        if (_content is null)
        {
            return;
        }
        double border = Theme.Metrics.ControlBorderThickness;
        _content.Arrange(new Rect(
            panelRect.X + border,
            panelRect.Y + border,
            Math.Max(0, panelRect.Width - 2 * border),
            Math.Max(0, panelRect.Height - 2 * border)));
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
        for (int i = _buttons.Count - 1; i >= 0; i--)
        {
            if (_buttons[i].HitTest(point) is UIElement buttonHit)
            {
                return buttonHit;
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
            _content?.Render(context);
            _splitter.Render(context);
        }
    }
}
