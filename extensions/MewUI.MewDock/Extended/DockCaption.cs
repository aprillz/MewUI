using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Platform;

using DockModel = Aprillz.MewUI.MewDock.Model.Model;

namespace Aprillz.MewUI.MewDock.Extended;

/// <summary>
/// The single tool-group title bar used everywhere a tool is shown (pinned/floating tool tabset, and the auto-hide
/// reveal). One flat bar - title + dropdown menu + pin + close, draggable - parameterised by delegates so the tool
/// and border variants share the exact same control and height instead of duplicating it.
/// </summary>
internal sealed class DockCaption : ContentControl, IToolHeader
{
    private const double BarHeight = 26;

    private readonly DockModel _model;
    private readonly Func<string?> _title;
    private readonly Func<Node?> _dragNode;
    private readonly Action<Point> _floatOut;
    private readonly Action<ContextMenu> _buildMenu;
    private readonly TextBlock _label;
    private readonly Button _menuButton;

    private DockCaption(
        DockModel model,
        Func<string?> title,
        Func<Node?> dragNode,
        GlyphKind pinGlyph,
        ObservableValue<string> pinToolTip,
        Action onPin,
        Action onClose,
        Action<ContextMenu> buildMenu,
        Action<Point> floatOut)
    {
        _model = model;
        _title = title;
        _dragNode = dragNode;
        _floatOut = floatOut;
        _buildMenu = buildMenu;

        _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _label.WithTheme((theme, label) => label.Foreground = theme.Palette.WindowText);

        _menuButton = GlyphButton(new GlyphElement { Kind = GlyphKind.ChevronDown });
        _menuButton.Click += ShowMenu;
        _menuButton.ToolTip = ToolTipLabel(MewUIDockString.ToolTipOptions);
        var pin = GlyphButton(new GlyphElement { Kind = pinGlyph });
        pin.Click += onPin;
        pin.ToolTip = ToolTipLabel(pinToolTip);
        var close = GlyphButton(new GlyphElement { Kind = GlyphKind.Cross });
        close.Click += onClose;
        close.ToolTip = ToolTipLabel(MewUIDockString.ToolTipClose);

        var dock = new DockPanel { LastChildFill = true, Spacing = 2 };
        dock.Add(close.DockRight());
        dock.Add(pin.DockRight());
        dock.Add(_menuButton.DockRight());
        dock.Add(_label);
        Content = dock;

        // A flat, fixed-height title bar with NO background of its own (the pane frame fills behind it), no rounding,
        // no border. Focus shows on the pane frame, not the caption.
        Padding = new Thickness(6, 0, 2, 0);
        CornerRadius = 0;
        BorderThickness = 0;
        Height = BarHeight;
        CanDrag = true;
        Refresh();
    }

    public override bool Focusable => false;

    public void Refresh() => _label.Text = _title() ?? MewUIDockString.TitleUnnamedTab.Value;

    private static TextBlock ToolTipLabel(ObservableValue<string> source) => new TextBlock().BindText(source);

    // A pinned/floating tool group: the title bar drags the WHOLE group; pin auto-hides; menu floats/auto-hides/closes.
    public static DockCaption ForTool(TabSetNode tabSet) => new(
        tabSet.Model,
        () => tabSet.GetSelectedNode()?.Name,
        () => tabSet,
        GlyphKind.Minus, // unpin (auto-hide)
        MewUIDockString.ToolTipAutoHide,
        () => tabSet.Model.DoAction(DockAction.UnpinTool(tabSet.GetId())),
        () => CloseSelected(tabSet.Model, tabSet.GetSelectedNode()),
        menu =>
        {
            menu.AddItem(MewUIDockString.MenuFloat.Value, () => tabSet.Model.DoAction(DockAction.PopoutTabset(tabSet.GetId())));
            menu.AddItem(MewUIDockString.MenuAutoHide.Value, () => tabSet.Model.DoAction(DockAction.UnpinTool(tabSet.GetId())));
            menu.AddItem(MewUIDockString.MenuClose.Value, () => CloseSelected(tabSet.Model, tabSet.GetSelectedNode()));
        },
        pos => tabSet.Model.DoAction(DockAction.PopoutTabset(tabSet.GetId(), position: pos)));

    // An auto-hidden tool reveal: the title bar drags the SELECTED tool; pin docks it; menu docks/floats/closes.
    public static DockCaption ForBorder(BorderNode border) => new(
        border.Model,
        () => border.GetSelectedNode()?.Name,
        () => border.GetSelectedNode(),
        GlyphKind.Plus, // dock (pin)
        MewUIDockString.ToolTipDock,
        () => Pin(border),
        () => CloseSelected(border.Model, border.GetSelectedNode()),
        menu =>
        {
            menu.AddItem(MewUIDockString.MemuDock.Value, () => Pin(border));
            menu.AddItem(MewUIDockString.MenuFloat.Value, () => Float(border));
            menu.AddItem(MewUIDockString.MenuClose.Value, () => CloseSelected(border.Model, border.GetSelectedNode()));
        },
        pos => { if (border.GetSelectedNode() is TabNode sel) border.Model.DoAction(DockAction.PopoutTab(sel.GetId(), position: pos)); });

    private static void Pin(BorderNode border)
    {
        if (border.GetSelectedNode() is TabNode sel)
        {
            border.Model.DoAction(DockAction.PinTool(sel.GetId()));
        }
    }

    private static void Float(BorderNode border)
    {
        if (border.GetSelectedNode() is TabNode sel)
        {
            border.Model.DoAction(DockAction.PopoutTab(sel.GetId()));
        }
    }

    private static void CloseSelected(DockModel model, TabNode? selected)
    {
        if (selected is not null)
        {
            model.DoAction(DockAction.DeleteTab(selected.GetId()));
        }
    }

    private static Button GlyphButton(UIElement content) => new()
    {
        Content = content,
        StyleName = BuiltInStyles.FlatButton,
        Padding = new Thickness(0),
        MinWidth = 18,
        MinHeight = 18,
        Width = 18,
        Height = 18,
        VerticalAlignment = VerticalAlignment.Center,
    };

    protected override void OnDragStarting(DragStartingEventArgs e)
    {
        base.OnDragStarting(e);
        if (_dragNode() is not Node node)
        {
            e.Cancel = true;
            return;
        }
        string title = _title() ?? MewUIDockString.TitleUnnamedTab.Value;
        _model.SetDraggingNode(node); // tear-off: hide the dragged group/tool in place for the duration of the drag
        var data = new DataObject();
        data.SetData(FlexLayoutView.DragFormat, node);
        e.Data = data;
        e.AllowedEffects = DragDropEffects.Move;
        e.Preview = new DragPreviewContent
        {
            Scope = DragPreviewScope.CrossWindow,
            // A whole tool group (the drag node is a tabset) shows its tab count; a single revealed tool shows 1.
            Element = FlexDragChip.BuildGroup(title, node is TabSetNode tabSet ? tabSet.Children.Count : 1),
            MaxWidth = 240,
            Hotspot = new Point(14, 12),
            Opacity = 0.9,
        };
    }

    protected override void OnDragCompleted(DragCompletedEventArgs e)
    {
        base.OnDragCompleted(e);
        _model.SetDraggingNode(null); // stop hiding before any move rebuilds, so the node shows at its new spot
        if (!e.WasCanceled && e.FinalEffect != DragDropEffects.Move)
        {
            _floatOut(e.ScreenPosition);
        }
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        _buildMenu(menu);
        var bounds = _menuButton.Bounds;
        menu.ShowAt(_menuButton, new Point(bounds.X, bounds.Bottom));
    }
}
