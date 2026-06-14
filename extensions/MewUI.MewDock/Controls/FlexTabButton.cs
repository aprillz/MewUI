using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// A single tab in a <see cref="FlexTabSetView"/> header (port of the golden DockTab). It is a real
/// <see cref="Button"/>: clicking selects the tab, an optional child × button closes it (handles its own click +
/// mouse capture, so clicking it never starts a tab drag or selects), and the tab itself is the drag source.
/// </summary>
internal sealed class FlexTabButton : Button
{
    private readonly TabNode _tab;
    private readonly TabSetNode _tabSet;
    private readonly FlexViewContext _context;
    private readonly TextBlock _label;
    private readonly Button? _closeButton;
    private readonly UIElement _normalContent;
    private TextBox? _renameBox;

    public FlexTabButton(TabNode tab, TabSetNode tabSet, FlexViewContext context)
    {
        _tab = tab;
        _tabSet = tabSet;
        _context = context;

        Padding = new Thickness(8, 2);
        MinHeight = 20;

        // Foreground is set explicitly (the label sits under the Panel-based header, which does not inherit it).
        _label = new TextBlock { Text = tab.Name ?? MewUIDockString.TitleUnnamedTab.Value };
        _label.WithTheme((theme, label) => label.Foreground = theme.Palette.WindowText);
        UIElement headerContent = context.Header?.Invoke(tab) ?? _label;

        // Pane tabs carry no per-tab close button - the caption owns close.
        if (tab.IsEnableClose && tabSet.IsDocument)
        {
            var buttonSize = 16;
            _closeButton = new Button 
            {
                Content = new GlyphElement { Kind = GlyphKind.Cross },
                StyleName = BuiltInStyles.FlatButton,
                Padding = new Thickness(0),
                MinWidth = buttonSize,
                MinHeight = buttonSize,
                Width = buttonSize,
                Height = buttonSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _closeButton.Click += OnCloseClick;
            _closeButton.ToolTip = new TextBlock().BindText(MewUIDockString.ToolTipClose);

            // DockPanel: close docked to the trailing edge, header fills the rest.
            var panel = new DockPanel { LastChildFill = true, Spacing = 4 };
            panel.Add(_closeButton.DockRight());
            panel.Add(headerContent);
            _normalContent = panel;
        }
        else
        {
            _normalContent = headerContent;
        }
        Content = _normalContent;

        // Background / BorderBrush / Padding / CornerRadius / BorderThickness come from the themed DockStyles rule.
        Click += () => _tabSet.Model.DoAction(DockAction.SelectTab(_tab.GetId()));
        CanDrag = tab.IsEnableDrag;
    }

    public TabNode Tab => _tab;

    public bool IsActive => ReferenceEquals(FlexTabSetView.EffectiveSelected(_tabSet), _tab);

    // The active tab reports Selected; it also reports Focused when its tabset is the active one, which the
    // style uses to highlight the tab border toward the accent. (base sets Focused from keyboard focus - strip it.)
    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        var flags = state.Flags & ~VisualStateFlags.Focused;
        if (IsActive)
        {
            flags |= VisualStateFlags.Selected;
            if (ReferenceEquals(_tabSet.Model.FocusedTabSet, _tabSet))
            {
                flags |= VisualStateFlags.Focused;
            }
        }
        return new VisualState { Flags = flags };
    }

    private void OnCloseClick() => _tabSet.Model.DoAction(DockAction.DeleteTab(_tab.GetId()));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // Middle-click closes a closable tab (port of the golden tab middle-button close). Left click/drag is
        // handled by the base Button (Click) + the framework drag-and-drop (CanDrag).
        if (e.Button == MouseButton.Middle && _tab.IsEnableClose)
        {
            OnCloseClick();
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

    private void ShowContextMenu(Point localPosition)
    {
        var menu = new ContextMenu();
        BuildDefaultMenu(menu);
        _context.ConfigureTabMenu?.Invoke(_tab, menu); // host appends app commands
        menu.ShowAt(this, new Point(Bounds.X + localPosition.X, Bounds.Y + localPosition.Y));
    }

    // Default items differ by kind: a document tab gets close-variants / split / maximize; a (pinned) tool tab
    // mirrors the caption menu (float / auto-hide / close).
    private void BuildDefaultMenu(ContextMenu menu)
    {
        var model = _tabSet.Model;
        string tabId = _tab.GetId();
        string setId = _tabSet.GetId();
        int tabCount = _tabSet.Children.Count;

        if (_tabSet.IsDocument)
        {
            bool othersClosable = _tabSet.Children.Any(c => c is TabNode other && !ReferenceEquals(other, _tab) && other.IsEnableClose);
            bool anyClosable = _tabSet.Children.Any(c => c is TabNode closable && closable.IsEnableClose);
            menu.AddItem(MewUIDockString.MenuClose.Value, OnCloseClick, _tab.IsEnableClose);
            menu.AddItem(MewUIDockString.MenuCloseOthers.Value, CloseOthers, othersClosable);
            menu.AddItem(MewUIDockString.MenuCloseAll.Value, CloseAll, anyClosable);
            menu.AddSeparator();
            menu.AddItem(MewUIDockString.MenuFloat.Value, () => model.DoAction(DockAction.PopoutTab(tabId)), _tab.IsEnablePopout);
            // Vertical group = a side-by-side split (vertical splitter); horizontal group = a stacked split.
            menu.AddItem(MewUIDockString.MenuNewVerticalTabGroup.Value,
                () => model.DoAction(DockAction.MoveNode(tabId, setId, DockLocation.Right, -1)), tabCount > 1);
            menu.AddItem(MewUIDockString.MenuNewHorizontalTabGroup.Value,
                () => model.DoAction(DockAction.MoveNode(tabId, setId, DockLocation.Bottom, -1)), tabCount > 1);
            menu.AddItem(MewUIDockString.MenuMoveToNextTabGroup.Value,
                () => MoveToAdjacentGroup(1), AdjacentDocumentTabSet(1) is not null);
            menu.AddItem(MewUIDockString.MenuMoveToPreviousTabGroup.Value,
                () => MoveToAdjacentGroup(-1), AdjacentDocumentTabSet(-1) is not null);
            if (_tabSet.IsEnableMaximize)
            {
                menu.AddSeparator();
                var label = _tabSet.IsMaximized ? MewUIDockString.MenuRestore.Value : MewUIDockString.MenuMaximize.Value;
                menu.AddItem(label, () => model.DoAction(DockAction.MaximizeToggle(setId)));
            }
        }
        else
        {
            menu.AddItem(MewUIDockString.MenuFloat.Value, () => model.DoAction(DockAction.PopoutTabset(setId)));
            menu.AddItem(MewUIDockString.MenuAutoHide.Value, () => model.DoAction(DockAction.UnpinTool(setId)));
            menu.AddItem(MewUIDockString.MenuClose.Value, OnCloseClick, _tab.IsEnableClose);
        }
    }

    private void CloseOthers()
    {
        foreach (var child in _tabSet.Children.ToList())
        {
            if (child is TabNode other && !ReferenceEquals(other, _tab) && other.IsEnableClose)
            {
                _tabSet.Model.DoAction(DockAction.DeleteTab(other.GetId()));
            }
        }
    }

    private void CloseAll()
    {
        foreach (var child in _tabSet.Children.ToList())
        {
            if (child is TabNode tab && tab.IsEnableClose)
            {
                _tabSet.Model.DoAction(DockAction.DeleteTab(tab.GetId()));
            }
        }
    }

    // The next / previous document group in traversal (spatial) order, or null at the ends.
    private TabSetNode? AdjacentDocumentTabSet(int direction)
    {
        var groups = new List<TabSetNode>();
        _tabSet.Model.GetRootRow(_tabSet.LayoutId).ForEachNode((node, level) =>
        {
            if (node is TabSetNode tabSet && tabSet.IsDocument)
            {
                groups.Add(tabSet);
            }
        }, 0);
        int index = groups.IndexOf(_tabSet);
        int target = index + direction;
        return index >= 0 && target >= 0 && target < groups.Count ? groups[target] : null;
    }

    private void MoveToAdjacentGroup(int direction)
    {
        if (AdjacentDocumentTabSet(direction) is TabSetNode target)
        {
            _tabSet.Model.DoAction(DockAction.MoveNode(_tab.GetId(), target.GetId(), DockLocation.Center, -1));
        }
    }

    // Double-click the tab to rename it inline (port of the FlexLayout tab rename gesture).
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButton.Left && _tab.IsEnableRename && _renameBox is null)
        {
            BeginRename();
            e.Handled = true;
        }
    }

    private void BeginRename()
    {
        _renameBox = new TextBox { Text = _tab.Name ?? string.Empty, MinWidth = 80 };
        _renameBox.KeyDown += OnRenameKeyDown;
        _renameBox.LostFocus += CommitRename;
        Content = _renameBox;
        _renameBox.Focus();
        _renameBox.SelectAll();
    }

    private void OnRenameKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void CommitRename()
    {
        if (_renameBox is null)
        {
            return;
        }
        var name = _renameBox.Text;
        _renameBox = null;
        // RenameTab is structural: it rebuilds the tabset view, recreating this button with the new name.
        _tabSet.Model.DoAction(DockAction.RenameTab(_tab.GetId(), name));
    }

    private void CancelRename()
    {
        if (_renameBox is null)
        {
            return;
        }
        _renameBox = null;
        Content = _normalContent;
    }

    protected override void OnDragStarting(DragStartingEventArgs e)
    {
        base.OnDragStarting(e);

        var data = new DataObject();
        data.SetData(FlexLayoutView.DragFormat, _tab);
        e.Data = data;
        e.AllowedEffects = DragDropEffects.Move;

        // Grip the chip where the tab was grabbed, clamped into the chip (which is narrower than a wide tab).
        const double maxWidth = 240;
        var chip = FlexDragChip.Build(_tab.Name ?? MewUIDockString.TitleUnnamedTab.Value);
        chip.Measure(new Size(maxWidth, double.PositiveInfinity));
        var grab = e.StartPositionInElement;
        var hotspot = new Point(
            Math.Clamp(grab.X, 0, chip.DesiredSize.Width),
            Math.Clamp(grab.Y, 0, chip.DesiredSize.Height));

        e.Preview = new DragPreviewContent
        {
            Scope = DragPreviewScope.CrossWindow,
            Element = chip,
            MaxWidth = maxWidth,
            Hotspot = hotspot,
            Opacity = 0.9,
        };

        _tabSet.Model.SetDraggingNode(_tab); // tear-off: hide this tab from the strip until drop / cancel
    }

    // Released over no drop target (empty space / outside all windows): pop the tab out into a new window
    // (port of golden DockTab.OnDragCompleted's PopOutDetachedComponent path).
    protected override void OnDragCompleted(DragCompletedEventArgs e)
    {
        base.OnDragCompleted(e);
        _tabSet.Model.SetDraggingNode(null); // commit/popout/ESC all stop hiding (cancel restores the tab)
        if (!e.WasCanceled && e.FinalEffect != DragDropEffects.Move)
        {
            // Pass the PHYSICAL cursor position; SyncPopouts converts it to DIPs at placement (mixed-DPI safe).
            _tabSet.Model.DoAction(DockAction.PopoutTab(_tab.GetId(), position: e.ScreenPosition));
        }
    }

    // Document tab (top strip) opens DOWN into the content: top-rounded, no bottom border. A tool tab (bottom
    // strip) is the vertical mirror: bottom-rounded, no top border. Snapped to pixels; the Button renders its
    // content in OnRender, so call Content.Render too.
    protected override void OnRender(IGraphicsContext context)
    {
        var background = GetValue(BackgroundProperty);
        var borderBrush = GetValue(BorderBrushProperty);
        double r = CornerRadius;
        double t = BorderThickness;

        var (thickness, corner) = _tabSet.IsDocument
            ? (new Thickness(t, t, t, 0), new CornerRadius(r, r, 0, 0))
            : (new Thickness(t, 0, t, t), new CornerRadius(0, 0, r, r));
        DrawBackgroundAndBorder(context, GetSnappedBorderBounds(Bounds), background, borderBrush, thickness, corner);

        Content?.Render(context);
    }
}
