using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// A tab in a border's collapsed strip. A real <see cref="Button"/> whose content is a <see cref="DockPanel"/> (the
/// optional close button docked to the strip edge, the header filling the rest); on left/right borders the header is
/// wrapped in a <see cref="RotationDecorator"/> so it reads vertically while staying upright in layout. Clicking the body
/// toggles the border's selected tab; it is also a drag source.
/// </summary>
internal class FlexBorderButton : Button
{
    private const double CloseSize = 16;

    protected readonly TabNode _tab;
    protected readonly BorderNode _border;
    protected readonly FlexViewContext _context;
    protected readonly bool _rotated;

    // rotated == null derives from the border side (left/right rotate); the Extended bottom strip passes false to
    // render horizontally on every side.
    public FlexBorderButton(TabNode tab, BorderNode border, FlexViewContext context, bool? rotated = null)
    {
        _tab = tab;
        _border = border;
        _context = context;
        _rotated = rotated ?? border.Location is DockLocation.Left or DockLocation.Right;

        var headerContent = context.Header?.Invoke(tab) ?? CreateDefaultLabel(tab);
        // Left border reads bottom-to-top, right border top-to-bottom; the wrapper handles measure/render/hit-test.
        UIElement headerHost = _rotated
            ? new RotationDecorator
            {
                Child = headerContent,
                Rotation = border.Location == DockLocation.Left
                    ? Rotation.CounterClockwise90
                    : Rotation.Clockwise90,
            }
            : headerContent;

        var panel = new DockPanel { LastChildFill = true, Spacing = 4 };
        // Faithful border tabs show a per-tab close; the Extended auto-hide strip removes it (the caption owns close).
        if (tab.IsEnableClose && ShowsCloseButton)
        {
            var close = new Button
            {
                Content = new GlyphElement { Kind = GlyphKind.Cross },
                StyleName = BuiltInStyles.FlatButton,
                Padding = new Thickness(0),
                MinWidth = CloseSize,
                MinHeight = CloseSize,
                Width = CloseSize,
                Height = CloseSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            close.Click += () => _border.Model.DoAction(DockAction.DeleteTab(_tab.GetId()));
            close.ToolTip = new TextBlock().BindText(MewUIDockString.ToolTipClose);
            panel.Add(_rotated ? close.DockBottom() : close.DockRight());
        }
        panel.Add(headerHost);
        Content = panel;

        // On a rotated (narrow strip) button, swap the style padding so it is small across the strip (it would
        // otherwise squeeze the rotated header) and larger along it.
        Padding = _rotated ? new Thickness(2, 8) : new Thickness(8, 2);
        MinHeight = 20;

        Click += OnSelectClick;
        CanDrag = tab.IsEnableDrag && IsTabDragSource;
    }

    // Faithful border tabs close and drag; the Extended auto-hide strip (ExtendedBorderButton) disables both
    // (the caption owns close, clicking just reveals/toggles).
    protected virtual bool ShowsCloseButton => true;

    protected virtual bool IsTabDragSource => true;

    // SelectTab on a border toggles (re-selecting the current tab collapses it). The Extended layer overrides this
    // so that, while expanded, clicking a tab only switches selection and never auto-hides.
    protected virtual void OnSelectClick() => _border.Model.DoAction(DockAction.SelectTab(_tab.GetId()));

    public TabNode Tab => _tab;

    public bool IsSelected => ReferenceEquals(_border.GetSelectedNode(), _tab);


    private static TextBlock CreateDefaultLabel(TabNode tab)
    {
        var label = new TextBlock
        {
            Text = tab.Name ?? MewUIDockString.TitleUnnamedTab.Value,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.WithTheme((theme, l) => l.Foreground = theme.Palette.WindowText);
        return label;
    }

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        var flags = state.Flags & ~VisualStateFlags.Focused;
        if (IsSelected)
        {
            flags |= VisualStateFlags.Selected;
        }
        return new VisualState { Flags = flags };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButton.Middle && _tab.IsEnableClose)
        {
            _border.Model.DoAction(DockAction.DeleteTab(_tab.GetId()));
            e.Handled = true;
            return;
        }
        if (e.Button == MouseButton.Right)
        {
            ShowContextMenu(e.GetPosition(this));
            e.Handled = true;
            return;
        }
        base.OnMouseDown(e);
    }

    // An auto-hidden tool's menu mirrors the ForBorder caption: dock (pin) / float / close.
    private void ShowContextMenu(Point localPosition)
    {
        var model = _border.Model;
        string tabId = _tab.GetId();
        var menu = new ContextMenu();
        menu.AddItem(MewUIDockString.MemuDock.Value, () => model.DoAction(DockAction.PinTool(tabId)));
        menu.AddItem(MewUIDockString.MenuFloat.Value, () => model.DoAction(DockAction.PopoutTab(tabId)));
        menu.AddItem(MewUIDockString.MenuClose.Value, () => model.DoAction(DockAction.DeleteTab(tabId)), _tab.IsEnableClose);
        _context.ConfigureTabMenu?.Invoke(_tab, menu); // host appends app commands
        menu.ShowAt(this, new Point(Bounds.X + localPosition.X, Bounds.Y + localPosition.Y));
    }

    protected override void OnDragStarting(DragStartingEventArgs e)
    {
        base.OnDragStarting(e);
        var data = new DataObject();
        data.SetData(FlexLayoutView.DragFormat, _tab);
        e.Data = data;
        e.AllowedEffects = DragDropEffects.Move;
        e.Preview = new DragPreviewContent
        {
            Scope = DragPreviewScope.CrossWindow,
            Element = FlexDragChip.Build(_tab.Name ?? MewUIDockString.TitleUnnamedTab.Value),
            MaxWidth = 240,
            Hotspot = new Point(14, 12),
            Opacity = 0.9,
        };
    }

    protected override void OnDragCompleted(DragCompletedEventArgs e)
    {
        base.OnDragCompleted(e);
        if (!e.WasCanceled && e.FinalEffect != DragDropEffects.Move)
        {
            OnDragOutPopout(e.ScreenPosition);
        }
    }

    // Released over no drop target: by default pop this single tab out into a window. The Extended docking layer
    // overrides this so dragging the tab area pops the whole border out as a group.
    protected virtual void OnDragOutPopout(Point screenPosition)
        => _border.Model.DoAction(DockAction.PopoutTab(_tab.GetId(), position: screenPosition));

    protected override void OnRender(IGraphicsContext context)
    {
        var (corner, thickness) = ButtonChrome();
        DrawBackgroundAndBorder(context, GetSnappedBorderBounds(Bounds),
            GetValue(BackgroundProperty), GetValue(BorderBrushProperty), thickness, corner);
        Content?.Render(context);
    }

    // Faithful: when the border is EXPANDED the buttons open toward the panel (the active tab connects to it).
    // ExtendedBorderButton overrides this with an always-closed shape (its reveal is an overlay, not connected).
    protected virtual (CornerRadius Corner, Thickness Thickness) ButtonChrome()
    {
        double r = CornerRadius;
        double t = BorderThickness;
        if (_border.Selected == -1)
        {
            return (new CornerRadius(r), new Thickness(t));
        }
        return _border.Location switch
        {
            DockLocation.Top => (new CornerRadius(r, r, 0, 0), new Thickness(t, t, t, 0)),
            DockLocation.Bottom => (new CornerRadius(0, 0, r, r), new Thickness(t, 0, t, t)),
            DockLocation.Left => (new CornerRadius(r, 0, 0, r), new Thickness(t, t, 0, t)),
            _ => (new CornerRadius(0, r, r, 0), new Thickness(0, t, t, t)),
        };
    }
}
