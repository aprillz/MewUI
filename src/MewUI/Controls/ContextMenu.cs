using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A context menu popup control for displaying menu items.
/// </summary>
public sealed partial class ContextMenu : Control, IPopupOwner, ICommandSource, IVisualTreeHost
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ContextMenu>(DefaultStyles.CreateContextMenuStyle);

    // Owner context captured at Show (or inherited from the parent menu / preset by MenuBar):
    // command items resolve CanExecute, execution and shortcut labels against it so popup focus
    // never changes the semantic target.
    private CommandTarget _capturedCommandTarget;
    private CommandTarget? _presetCommandTarget;

    // Operand captured with the target: typed handlers act on the item the menu opened over even
    // after the container it came from is recycled to another item.
    private object? _capturedCommandArgument;

    internal const double SubMenuGlyphAreaWidth = 14;
    internal const double ShortcutColumnGap = 12;
    internal const double IconTextGap = 8;
    private readonly ScrollBar _vBar;
    private readonly ScrollController _scroll = new();
    private readonly MenuTextLayouts _textLayouts = new();
    private double _extentHeight;
    private double _viewportHeight;
    private double _verticalOffset;
    private int _hotIndex = -1;
    private ContextMenu? _openSubMenu;
    private int _openSubMenuIndex = -1;
    private ContextMenu? _parentMenu;
    private double _maxTextWidth;

    /// <summary>The caption width measure asked for; the render pass must not grant less.</summary>
    internal double MeasuredCaptionWidth => _maxTextWidth;

    /// <summary>The narrowest caption box the last render granted. Reset before a probe render.</summary>
    internal double LastCaptionWidth { get; set; } = double.PositiveInfinity;
    private double _maxShortcutWidth;
    private bool _hasAnyShortcut;
    private bool _hasAnyIcon;

    // Icons are built when their row is first realized and kept across scrolling until the size changes.
    private readonly Dictionary<MenuItem, FrameworkElement> _icons = new();

    // The rows of the entries in view, by entry index, and the rows released from view for reuse.
    private readonly SortedDictionary<int, MenuRow> _rows = new();
    private readonly Stack<MenuRow> _rowPool = new();

    internal MenuTextLayouts TextLayouts => _textLayouts;

    /// <summary>
    /// Gets the menu model.
    /// </summary>
    public Menu Menu { get; }

    /// <summary>
    /// Gets the menu items collection.
    /// </summary>
    public IList<MenuEntry> Items => Menu.Items;

    /// <summary>
    /// Gets or sets the height of menu items.
    /// </summary>
    public static readonly MewProperty<double> ItemHeightProperty =
        MewProperty<double>.Register<ContextMenu>(nameof(ItemHeight), double.NaN, MewPropertyOptions.AffectsLayout);

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding around menu items.
    /// </summary>
    public static readonly MewProperty<Thickness> ItemPaddingProperty =
        MewProperty<Thickness>.Register<ContextMenu>(nameof(ItemPadding), default, MewPropertyOptions.AffectsLayout);

    public Thickness ItemPadding
    {
        get => GetValue(ItemPaddingProperty);
        set => SetValue(ItemPaddingProperty, value);
    }

    public static readonly MewProperty<double> MaxMenuHeightProperty =
        MewProperty<double>.Register<ContextMenu>(nameof(MaxMenuHeight), double.PositiveInfinity, MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// Gets or sets an upper bound on the menu height. The default is no bound; the menu is never taller
    /// than the room on the side it opens toward, and scrolls beyond either limit.
    /// </summary>
    public double MaxMenuHeight
    {
        get => GetValue(MaxMenuHeightProperty);
        set => SetValue(MaxMenuHeightProperty, value);
    }

    public static readonly MewProperty<MenuPlacement> PlacementProperty =
        MewProperty<MenuPlacement>.Register<ContextMenu>(nameof(Placement), MenuPlacement.Pointer);

    /// <summary>
    /// Gets or sets where the menu opens relative to its placement target.
    /// </summary>
    public MenuPlacement Placement
    {
        get => GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    public static readonly MewProperty<Point> PlacementOffsetProperty =
        MewProperty<Point>.Register<ContextMenu>(nameof(PlacementOffset), default);

    /// <summary>
    /// Gets or sets a DIP offset applied to the placement, mirrored on the flipped side.
    /// </summary>
    public Point PlacementOffset
    {
        get => GetValue(PlacementOffsetProperty);
        set => SetValue(PlacementOffsetProperty, value);
    }

    private static readonly MewPropertyKey<UIElement?> PlacementTargetPropertyKey =
        MewProperty<UIElement?>.RegisterReadOnly<ContextMenu>(nameof(PlacementTarget), null);

    /// <summary>The element the last <see cref="Show(UIElement)"/> opened on.</summary>
    public static readonly MewProperty<UIElement?> PlacementTargetProperty = PlacementTargetPropertyKey.Property;

    /// <summary>
    /// Gets the element the menu last opened on. Set by <see cref="Show(UIElement)"/> and kept
    /// until the next show, so a handler that runs after the menu closed can still read it.
    /// </summary>
    public UIElement? PlacementTarget => GetValue(PlacementTargetProperty);

    static ContextMenu()
    {
        FocusableProperty.OverrideDefaultValue<ContextMenu>(true);
    }

    /// <summary>
    /// Initializes a new instance of the ContextMenu class.
    /// </summary>
    public ContextMenu()
        : this(new Menu())
    {
    }

    /// <summary>
    /// Initializes a new instance of the ContextMenu class with a menu model.
    /// </summary>
    /// <param name="menu">The menu model.</param>
    public ContextMenu(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        Menu = menu;
        Menu.Changed += OnMenuChanged;
        if (!double.IsNaN(menu.ItemHeight) && menu.ItemHeight > 0)
        {
            ItemHeight = menu.ItemHeight;
        }
        if (menu.ItemPadding is Thickness itemPadding)
        {
            ItemPadding = itemPadding;
        }
        else
        {
            ItemPadding = Theme.Metrics.ItemPadding;
        }
        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false, Parent = this };
        _vBar.ValueChanged += v =>
        {
            UpdateScrollFromBar(v);
        };
    }

    private void OnMenuChanged(MenuItem? item, MenuModelChange change)
    {
        if ((change & MenuModelChange.Structure) != 0)
        {
            if (FindVisualRoot() is Window structureWindow)
            {
                CloseDescendants(structureWindow);
            }

            _hotIndex = -1;
            ReleaseRows();
        }

        if ((change & (MenuModelChange.Structure | MenuModelChange.Text |
            MenuModelChange.Command | MenuModelChange.Shortcut)) != 0)
        {
            _textLayouts.Invalidate();
            InvalidateMeasure();
        }

        if ((change & (MenuModelChange.Structure | MenuModelChange.Icon |
            MenuModelChange.Command)) != 0 && FindVisualRoot() is Window window)
        {
            if ((change & (MenuModelChange.Structure | MenuModelChange.Command)) != 0 &&
                !_capturedCommandTarget.IsEmpty)
            {
                UpdateCommandPresentation(window);
            }

            ResetIcons();
            if (HasCommandItems()) window.RegisterCommandSource(this);
            else window.UnregisterCommandSource(this);
            InvalidateMeasure();
        }

        if (item != null && FindRow(item) is MenuRow row)
        {
            row.OnEntryChanged(change);
        }
    }

    private MenuRow? FindRow(MenuItem item)
    {
        foreach (var row in _rows.Values)
        {
            if (ReferenceEquals(row.Entry, item))
            {
                return row;
            }
        }

        return null;
    }

    public void AddItem(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Menu.Items.Add(new MenuItem(command));
        _textLayouts.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void AddItem(string text, bool isEnabled = true)
    {
        Menu.Items.Add(new MenuItem(text) { IsEnabled = isEnabled });
        _textLayouts.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void AddItem(string text, Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Menu.Items.Add(new MenuItem(text, command));
        _textLayouts.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Adds a command item that passes <paramref name="data"/> as the invocation argument.
    /// </summary>
    public void AddItem(string text, Command command, object? data)
    {
        ArgumentNullException.ThrowIfNull(command);
        Menu.Items.Add(new MenuItem(text, command, data));
        _textLayouts.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void AddSubMenu(string text, Menu subMenu, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(subMenu);
        Menu.SubMenu(text, subMenu, isEnabled);
        _textLayouts.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void AddEntry(MenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Menu.Add(entry);
        _textLayouts.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetItems(params MenuEntry[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            AddEntry(items[i]);
        }
    }

    public void AddSeparator()
    {
        Menu.Separator();
        _textLayouts.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Presets the command target snapshot the next <see cref="ShowAt"/> resolves against.
    /// Use this for menus whose commands are bound to a standalone scope rather than the visual owner.
    /// </summary>
    public void SetCommandTarget(CommandTarget target)
    {
        if (target.IsEmpty)
            throw new ArgumentException("The command target cannot be empty.", nameof(target));

        _presetCommandTarget = target;
    }

    private void UpdateCommandPresentation(Window window)
    {
        foreach (var entry in Menu.Items)
        {
            if (entry is not MenuItem item)
            {
                continue;
            }

            // Predicates answer for rows with no command too, so this runs outside the command branch.
            item.ReevaluateCanClick();

            if (item.Command is Command command)
            {
                bool enabled = window.CommandRouter.CanExecute(command, _capturedCommandTarget, ArgumentFor(item));
                string? shortcutText = InputMapResolver.GetEffectiveGestureText(
                    window, command, _capturedCommandTarget.OriginElement, item.CommandData);
                item.ApplyCommandState(enabled, shortcutText);
            }
        }
    }

    /// <summary>
    /// The argument an item invokes with: the value it declares, else the one captured when the
    /// menu opened.
    /// </summary>
    private object? ArgumentFor(MenuItem item) => item.CommandData ?? _capturedCommandArgument;

    private bool HasCommandItems()
    {
        foreach (var entry in Menu.Items)
        {
            if (entry is MenuItem item && item.Command != null)
                return true;
        }

        return false;
    }

    private double ResolveIconSize()
    {
        double size = Theme.Metrics.CommandIconSize;
        return double.IsFinite(size) && size > 0 ? size : 16;
    }

    private bool HasAnyIcon()
    {
        foreach (var entry in Items)
        {
            if (entry is MenuItem item && item.ResolveIconTemplate() != null)
            {
                return true;
            }
        }

        return false;
    }

    private FrameworkElement? IconFor(MenuEntry entry)
    {
        if (entry is not MenuItem item || item.ResolveIconTemplate() is not IconTemplate template)
        {
            return null;
        }

        if (!_icons.TryGetValue(item, out var icon))
        {
            var size = IconTemplate.ResolveSize(ResolveIconSize(), GetDpi() / 96.0);
            icon = template.Build(size);
            icon.Width = size.Dip;
            icon.Height = size.Dip;
            icon.IsHitTestVisible = false;
            _icons.Add(item, icon);
        }

        return icon;
    }

    /// <summary>Drops the built icons, for a new size or template, and gives the rows in view new ones.</summary>
    private void ResetIcons()
    {
        _icons.Clear();
        foreach (var row in _rows.Values)
        {
            row.Bind(row.Entry, row.Entry is MenuEntry entry ? IconFor(entry) : null);
        }
    }

    private void ReleaseRows()
    {
        foreach (var row in _rows.Values)
        {
            ReleaseRow(row);
        }

        _rows.Clear();
    }

    private void ReleaseRow(MenuRow row)
    {
        row.Bind(null, null);
        row.SetIsHighlighted(false);
        row.Parent = null;
        _rowPool.Push(row);
    }

    /// <summary>
    /// Opens the menu on the target as <see cref="Placement"/> directs. Pointer placement opens at
    /// the current pointer position.
    /// </summary>
    public void Show(UIElement placementTarget)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);

        if (Placement == MenuPlacement.Pointer)
        {
            if (placementTarget.FindVisualRoot() is not Window window)
            {
                return;
            }

            var pointer = window.LastMousePositionDip;
            Show(placementTarget, pointer);
        }
        else
        {
            ShowCore(placementTarget, placementTarget.Bounds, Placement);
        }
    }

    /// <summary>
    /// Opens the menu at an explicit window position (a caret, a stored press point), regardless of
    /// <see cref="Placement"/>.
    /// </summary>
    public void Show(UIElement placementTarget, Point positionInWindow)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);
        ShowCore(placementTarget, new Rect(positionInWindow.X, positionInWindow.Y, 0, 0), MenuPlacement.Pointer);
    }

    /// <summary>
    /// Opens the menu against an anchor rectangle that is not the target's own bounds (a MenuBar
    /// item cell). Placement follows <see cref="Placement"/>.
    /// </summary>
    internal void Show(UIElement placementTarget, Rect anchorInWindow)
        => ShowCore(placementTarget, anchorInWindow, Placement);

    [Obsolete("Use Show(placementTarget) with Placement = MenuPlacement.Below for target-anchored menus, or Show(placementTarget, positionInWindow) for an explicit position. Side placements derive the flip anchor that anchorTopY carried by hand.")]
    public void ShowAt(UIElement owner, Point positionInWindow, double? anchorTopY = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (anchorTopY is double anchorTop)
        {
            // The legacy pair (open point, flip anchor) is a Below placement against the rect the
            // caller derived both values from.
            var anchor = new Rect(
                positionInWindow.X,
                anchorTop,
                0,
                Math.Max(0, positionInWindow.Y - anchorTop));
            ShowCore(owner, anchor, MenuPlacement.Below);
        }
        else
        {
            Show(owner, positionInWindow);
        }
    }

    private void ShowCore(UIElement placementTarget, Rect anchorInWindow, MenuPlacement placement)
    {
        var root = placementTarget.FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        SetValue(PlacementTargetPropertyKey, placementTarget);
        _capturedCommandTarget = _presetCommandTarget ?? CommandTarget.From(placementTarget);
        _capturedCommandArgument = CommandRouter.ResolveArgument(placementTarget);

        UpdateCommandPresentation(window);
        ResetIcons();
        CloseDescendants(window);
        _parentMenu = null;

        // Placement is measured inside ShowPopup, after the menu is rooted and its style resolves.
        // Measuring here saw an unstyled zero border, so the width came out short by the border the
        // arrange pass then deflated - and the caption is the only elastic column, so the whole loss
        // landed on it and trimmed the last glyph.
        window.ShowPopup(placementTarget, this, w => MeasurePlacement(w, anchorInWindow, placement));
        window.FocusManager.SetFocus(this);
    }

    private Rect MeasurePlacement(Window window, Rect anchor, MenuPlacement placement)
    {
        // Measure without passing infinity into backends that may convert widths to ints.
        var region = window.GetPopupPlacementRegion(anchor);
        Measure(new Size(Math.Max(0, region.Width), Math.Max(0, region.Height)));
        var desired = DesiredSize;

        double width = Math.Max(0, desired.Width);
        double height = Math.Max(0, desired.Height);

        double maxH = Math.Max(0, MaxMenuHeight);
        if (maxH > 0)
        {
            height = Math.Min(height, maxH);
        }

        var offset = PlacementOffset;
        double x;
        double y;

        switch (placement)
        {
            case MenuPlacement.Below:
                x = PopupPlacement.ClampHorizontal(anchor.X + offset.X, width, region, floorToLeftEdge: false);
                (y, height) = FitVertically(height, anchor.Bottom + offset.Y, anchor.Y - offset.Y, preferBelow: true, region.Y, region.Bottom);
                break;
            case MenuPlacement.Above:
                x = PopupPlacement.ClampHorizontal(anchor.X + offset.X, width, region, floorToLeftEdge: false);
                (y, height) = FitVertically(height, anchor.Bottom + offset.Y, anchor.Y - offset.Y, preferBelow: false, region.Y, region.Bottom);
                break;
            case MenuPlacement.Right:
                height = Math.Min(height, Math.Max(0, region.Height));
                x = ResolveMainAxis(anchor.Right + offset.X, anchor.X - offset.X - width, width, region.X, region.Right);
                y = ClampCrossAxis(anchor.Y + offset.Y, height, region.Y, region.Bottom);
                break;
            case MenuPlacement.Left:
                height = Math.Min(height, Math.Max(0, region.Height));
                x = ResolveMainAxis(anchor.X - offset.X - width, anchor.Right + offset.X, width, region.X, region.Right);
                y = ClampCrossAxis(anchor.Y + offset.Y, height, region.Y, region.Bottom);
                break;
            default:
                // At the pointer: below it, flipped above it when there is no room below.
                x = PopupPlacement.ClampHorizontal(anchor.X + offset.X, width, region, floorToLeftEdge: false);
                (y, height) = FitVertically(height, anchor.Y + offset.Y, anchor.Y - offset.Y, preferBelow: true, region.Y, region.Bottom);
                break;
        }

        return new Rect(x, y, width, height);
    }

    // The preferred start along the placement axis, the flipped start when the extent runs past the
    // far edge, and the far-edge fallback when the flip runs past the near edge.
    private static double ResolveMainAxis(double preferred, double flipped, double extent, double nearEdge, double farEdge)
    {
        if (preferred + extent <= farEdge)
        {
            return Math.Max(nearEdge, preferred);
        }

        return flipped >= nearEdge ? flipped : Math.Max(nearEdge, farEdge - extent);
    }

    /// <summary>
    /// Places a menu of <paramref name="height"/> below <paramref name="belowStart"/> or above
    /// <paramref name="aboveEnd"/>: the preferred side when it fits, the other side when that fits, and
    /// otherwise the roomier side with the height cut to its room.
    /// </summary>
    private static (double Y, double Height) FitVertically(double height, double belowStart, double aboveEnd, bool preferBelow, double top, double bottom)
    {
        belowStart = Math.Max(top, belowStart);
        aboveEnd = Math.Min(bottom, aboveEnd);
        double roomBelow = Math.Max(0, bottom - belowStart);
        double roomAbove = Math.Max(0, aboveEnd - top);

        bool below;
        if (height <= (preferBelow ? roomBelow : roomAbove))
        {
            below = preferBelow;
        }
        else if (height <= (preferBelow ? roomAbove : roomBelow))
        {
            below = !preferBelow;
        }
        else
        {
            below = preferBelow ? roomBelow >= roomAbove : roomBelow > roomAbove;
            height = below ? roomBelow : roomAbove;
        }

        if (below)
        {
            return (belowStart, height);
        }
        else
        {
            return (aboveEnd - height, height);
        }
    }

    private static double ClampCrossAxis(double preferred, double extent, double nearEdge, double farEdge)
    {
        if (preferred + extent > farEdge)
        {
            preferred = farEdge - extent;
        }

        return Math.Max(nearEdge, preferred);
    }

    // Whole device pixels, like ResolveSeparatorHeight: a row height that covers a fractional pixel
    // puts successive row boundaries on half-pixels, so rows come out a pixel apart from each other
    // and the last one stops short of the content box.
    private double ResolveItemHeight()
    {
        double height = !double.IsNaN(ItemHeight) && ItemHeight > 0
            ? ItemHeight
            : Math.Max(18, Theme.Metrics.BaseControlHeight - 2);

        double dpiScale = GetDpi() / 96.0;
        return Math.Max(1, LayoutRounding.RoundToPixelInt(height, dpiScale)) / dpiScale;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        // While shown (attached to a window's popup layer), an open menu with command items is a
        // tracked command source so state changes refresh its enabled visuals.
        (oldRoot as Window)?.UnregisterCommandSource(this);
        if (newRoot is Window window && HasCommandItems())
        {
            window.RegisterCommandSource(this);
        }

        if (oldRoot != null && newRoot == null)
        {
            ReleaseRows();
            _icons.Clear();
        }
    }

    void ICommandSource.EvaluateCommandState()
    {
        if (FindVisualRoot() is not Window window)
        {
            return;
        }

        // A row whose item moved hears of it through the model's change notification.
        foreach (var entry in Menu.Items)
        {
            if (entry is not MenuItem item)
            {
                continue;
            }

            item.ReevaluateCanClick();

            if (item.Command is Command command)
            {
                bool enabled = window.CommandRouter.CanExecute(command, _capturedCommandTarget, ArgumentFor(item));
                string? shortcutText = InputMapResolver.GetEffectiveGestureText(
                    window, command, _capturedCommandTarget.OriginElement, item.CommandData);
                item.ApplyCommandState(enabled, shortcutText);
            }
        }
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _textLayouts.Invalidate();

        if (ItemPadding == oldTheme.Metrics.ItemPadding)
        {
            ItemPadding = newTheme.Metrics.ItemPadding;
        }

        if (oldTheme.Metrics.CommandIconSize != newTheme.Metrics.CommandIconSize &&
            FindVisualRoot() is Window)
        {
            ResetIcons();
            InvalidateMeasure();
        }
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _textLayouts.Invalidate();
        if (FindVisualRoot() is Window)
        {
            ResetIcons();
            InvalidateMeasure();
        }
    }

    private double GetEntryHeight(MenuEntry entry)
    {
        if (entry is MenuSeparator)
        {
            return ResolveSeparatorHeight();
        }

        return ResolveItemHeight();
    }

    // Odd device-pixel height so a centered 1px separator line keeps equal top and bottom margins at any
    // scale: an even band cannot center a single pixel symmetrically (e.g. 2/1/1 at 125%).
    private double ResolveSeparatorHeight()
    {
        double dpiScale = GetDpi() / 96.0;
        int px = Math.Max(1, LayoutRounding.RoundToPixelInt(MenuSeparator.MenuSeparatorHeight, dpiScale));
        if ((px & 1) == 0)
        {
            px = Math.Max(1, px - 1);
        }

        return px / dpiScale;
    }

    private void UpdateScrollFromBar(double valueDip)
    {
        if (!_vBar.IsVisible)
        {
            return;
        }

        var dpiScale = GetDpi() / 96.0;
        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(1, _extentHeight, _viewportHeight);
        if (_scroll.SetOffsetDip(1, valueDip))
        {
            _verticalOffset = _scroll.GetOffsetDip(1);
            ArrangeRows();
            CloseSubMenu();
        }
    }

    private Rect GetContentViewportBounds()
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        var borderInset = GetBorderVisualInset();
        var innerBounds = bounds.Deflate(new Thickness(borderInset));
        // Viewport/clip rect should not shrink due to edge rounding; snap outward.
        return LayoutRounding.SnapViewportRectToPixels(innerBounds.Deflate(Padding), dpiScale);
    }

    private Rect GetItemViewportBounds() => GetContentViewportBounds();

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();

        double height = 0;
        double itemHeight = ResolveItemHeight();


        var factory = GetGraphicsFactory();
        var style = GetTextRunStyle();
        uint dpi = GetDpi();

        _maxTextWidth = 0;
        _maxShortcutWidth = 0;
        _hasAnyShortcut = false;
        bool hasAnySubMenu = false;
        _hasAnyIcon = HasAnyIcon();

        foreach (var entry in Items)
        {
            if (entry is MenuSeparator)
            {
                height += ResolveSeparatorHeight();
                continue;
            }

            if (entry is MenuItem item)
            {
                var text = GetDisplayText(item);
                var size = _textLayouts.Measure(factory, text, dpi, in style);
                _maxTextWidth = Math.Max(_maxTextWidth, size.Width);

                var shortcutText = item.GetShortcutDisplayText();
                if (!string.IsNullOrEmpty(shortcutText))
                {
                    _hasAnyShortcut = true;
                    var shortcutSize = _textLayouts.Measure(
                        factory, shortcutText, dpi, in style, TextAlignment.Right);
                    _maxShortcutWidth = Math.Max(_maxShortcutWidth, shortcutSize.Width);
                }

                hasAnySubMenu |= item.SubMenu != null;

                height += itemHeight;
            }
        }

        double maxWidth = Math.Ceiling(_maxTextWidth) + ItemPadding.HorizontalThickness;

        if (_hasAnyIcon)
        {
            maxWidth += ResolveIconSize() + IconTextGap;
        }

        if (_hasAnyShortcut)
        {
            maxWidth += ShortcutColumnGap + Math.Ceiling(_maxShortcutWidth);
        }

        if (hasAnySubMenu)
        {
            maxWidth += SubMenuGlyphAreaWidth;
        }

        double contentW = maxWidth + Padding.HorizontalThickness;
        double contentH = height + Padding.VerticalThickness;

        _extentHeight = height;

        // Placement also cuts the height to the room on the opening side; the rest scrolls.
        double maxH = Math.Max(0, MaxMenuHeight);
        if (maxH > 0)
        {
            contentH = Math.Min(contentH, maxH);
        }

        _viewportHeight = Math.Max(0, contentH - Padding.VerticalThickness);

        var desired = new Size(contentW, contentH).Inflate(new Thickness(borderInset));

        // Popup placement snaps the origin to a device pixel, so a fractional width puts the right
        // edge off the grid and the arrange-time border snap rounds it back inward. The caption is the
        // only elastic column, so it would pay that pixel; a whole-pixel width makes the snap a no-op.
        double dpiScale = GetDpi() / 96.0;
        double snappedW = LayoutRounding.CeilToPixelInt(desired.Width, dpiScale) / dpiScale;
        return new Size(snappedW, desired.Height);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);


        var snapped = GetSnappedBorderBounds(bounds);
        var borderInset = GetBorderVisualInset();
        var dpiScale = GetDpi() / 96.0;
        var innerBounds = snapped.Deflate(new Thickness(borderInset));
        // Viewport/clip rect should not shrink due to edge rounding; snap outward.
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(innerBounds.Deflate(Padding), dpiScale);
        _viewportHeight = Math.Max(0, contentBounds.Height);

        // The viewport was snapped outward to whole pixels while the extent is the raw measured sum,
        // so the two have to be brought onto the same pixel grid before they are compared. Comparing
        // across grids leaves a residue in DIPs that does not shrink as the scale factor grows, and
        // past roughly 2x it exceeds the one pixel of slack and a menu that fits reports a scroll bar.
        int extentPx = LayoutRounding.CeilToPixelInt(_extentHeight, dpiScale);
        int viewportPx = LayoutRounding.CeilToPixelInt(_viewportHeight, dpiScale);
        bool needV = extentPx > viewportPx + 1;
        _vBar.IsVisible = needV;

        if (!needV)
        {
            _verticalOffset = 0;
            _vBar.Value = 0;
            _vBar.Arrange(Rect.Empty);
            ArrangeRows();
            return;
        }

        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(1, _extentHeight, _viewportHeight);
        _scroll.SetOffsetDip(1, _verticalOffset);
        _verticalOffset = _scroll.GetOffsetDip(1);

        _vBar.Minimum = 0;
        _vBar.Maximum = _scroll.GetMaxDip(1);
        _vBar.ViewportSize = _viewportHeight;
        _vBar.SmallChange = Theme.Metrics.ScrollBarSmallChange;
        _vBar.LargeChange = Theme.Metrics.ScrollBarLargeChange;
        _vBar.Value = _verticalOffset;

        // Overlay: scrollbar sits on top of content at the right edge.
        double t = Theme.Metrics.ScrollBarHitThickness;
        _vBar.Arrange(new Rect(
            contentBounds.Right - t,
            contentBounds.Y,
            t,
            contentBounds.Height));
        ArrangeRows();
    }

    /// <summary>
    /// Gives each entry in view a row and lays it out, and releases the rows of entries that left the view.
    /// </summary>
    private void ArrangeRows()
    {
        if (Bounds.IsEmpty)
        {
            return;
        }

        var contentBounds = GetItemViewportBounds();
        double dpiScale = GetDpi() / 96.0;
        double itemRadius = Math.Max(0, LayoutRounding.RoundToPixel(CornerRadius, dpiScale) - GetBorderVisualInset());
        var columns = new MenuRowColumns(
            ResolveIconSize(), _hasAnyIcon, _maxShortcutWidth, _hasAnyShortcut, ItemPadding, itemRadius);

        int first = -1;
        int last = -2;
        double y = contentBounds.Y - _verticalOffset;
        for (int index = 0; index < Items.Count; index++)
        {
            double height = GetEntryHeight(Items[index]);
            double rowTop = LayoutRounding.RoundToPixel(y, dpiScale);
            double rowBottom = LayoutRounding.RoundToPixel(y + height, dpiScale);
            if (rowTop >= contentBounds.Bottom)
            {
                break;
            }

            if (rowBottom > contentBounds.Y)
            {
                if (first < 0)
                {
                    first = index;
                }

                last = index;
            }

            y += height;
        }

        List<int>? leaving = null;
        foreach (var (index, row) in _rows)
        {
            if (index < first || index > last || !ReferenceEquals(row.Entry, Items[index]))
            {
                (leaving ??= []).Add(index);
            }
        }

        if (leaving != null)
        {
            foreach (int index in leaving)
            {
                ReleaseRow(_rows[index]);
                _rows.Remove(index);
            }
        }

        y = contentBounds.Y - _verticalOffset;
        for (int index = 0; index <= last; index++)
        {
            var entry = Items[index];
            double height = GetEntryHeight(entry);
            if (index >= first)
            {
                // Snapped as the menu once snapped the rows it drew itself, so they tile without a seam.
                double rowTop = LayoutRounding.RoundToPixel(y, dpiScale);
                double rowBottom = LayoutRounding.RoundToPixel(y + height, dpiScale);
                if (!_rows.TryGetValue(index, out var row))
                {
                    row = _rowPool.Count > 0 ? _rowPool.Pop() : new MenuRow();
                    row.Parent = this;
                    row.Bind(entry, IconFor(entry));
                    _rows.Add(index, row);
                }

                row.Columns = columns;
                row.SetIsHighlighted(IsHighlighted(index));
                var rect = new Rect(contentBounds.X, rowTop, contentBounds.Width, rowBottom - rowTop);
                row.Measure(rect.Size);
                row.Arrange(rect);
            }

            y += height;
        }
    }

    private bool IsHighlighted(int index) => index >= 0 && (index == _hotIndex || index == _openSubMenuIndex);

    private void UpdateRowHighlight(int index)
    {
        if (index >= 0 && _rows.TryGetValue(index, out var row))
        {
            row.SetIsHighlighted(IsHighlighted(index));
        }
    }

    private void SetHotIndex(int index)
    {
        if (_hotIndex != index)
        {
            int previous = _hotIndex;
            _hotIndex = index;
            UpdateRowHighlight(previous);
            UpdateRowHighlight(index);
        }
    }

    private void SetOpenSubMenuIndex(int index)
    {
        if (_openSubMenuIndex != index)
        {
            int previous = _openSubMenuIndex;
            _openSubMenuIndex = index;
            UpdateRowHighlight(previous);
            UpdateRowHighlight(index);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!e.Handled)
        {
            // Prevent bubbling to the popup owner (e.g. text inputs capture the mouse on left-click,
            // which would swallow the subsequent mouse-up that activates the menu item).
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        SetHotIndex(-1);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !_vBar.IsVisible)
        {
            return;
        }

        double scrollDip = -e.Delta.Y * Theme.Metrics.ScrollWheelStep;
        if (Math.Abs(scrollDip) < 0.5)
        {
            return;
        }

        var dpiScale = GetDpi() / 96.0;
        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(1, _extentHeight, _viewportHeight);
        if (_scroll.ScrollByDip(1, scrollDip))
        {
            _verticalOffset = _scroll.GetOffsetDip(1);
            _vBar.Value = _verticalOffset;
            ArrangeRows();
            CloseSubMenu();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Handled)
        {
            return;
        }

        int index = HitTestEntryIndex(e.Position);
        SetHotIndex(index);

        if (index >= 0 && index < Items.Count && Items[index] is MenuItem item && item.SubMenu != null && item.IsEffectivelyEnabled)
        {
            if (_openSubMenuIndex != index)
            {
                if (TryGetEntryRowBounds(index, out var rowBounds))
                {
                    OpenSubMenu(index, item.SubMenu, rowBounds);
                }
            }
        }
        else
        {
            // If the user hovers a non-submenu item inside this menu, close the currently open submenu.
            if (index != -1)
            {
                CloseSubMenu();
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!IsEffectivelyEnabled || e.Handled || e.Button != MouseButton.Left)
        {
            return;
        }

        int index = HitTestEntryIndex(e.Position);
        if (index < 0 || index >= Items.Count)
        {
            return;
        }

        if (Items[index] is MenuItem item && item.IsEffectivelyEnabled)
        {
            if (item.SubMenu != null)
            {
                if (TryGetEntryRowBounds(index, out var rowBounds))
                {
                    OpenSubMenu(index, item.SubMenu, rowBounds);
                    e.Handled = true;
                }

                return;
            }

            InvokeItem(item);

            var root = FindVisualRoot();
            if (root is Window window)
            {
                CloseHierarchy(window);
            }

            e.Handled = true;
        }
    }

    private void InvokeItem(MenuItem item)
    {
        if (item.Command is Command command)
        {
            if (FindVisualRoot() is Window window)
            {
                window.CommandRouter.TryExecuteFromInput(command, _capturedCommandTarget, this, ArgumentFor(item));
            }

        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            var root = FindVisualRoot();
            if (root is Window window)
            {
                // Close only this menu; parent menus remain open.
                CloseSubMenu();
                window.ClosePopup(this);
                e.Handled = true;
            }

            return;
        }

        // Access key matching - character only, no modifiers (except Shift for uppercase)
        if (!e.AltKey && !e.ControlKey && !e.MetaKey)
        {
            TryActivateByAccessKey(e);
        }
    }

    private static string GetDisplayText(MenuItem item)
        => item.GetParsedText().displayText;

    private void TryActivateByAccessKey(KeyEventArgs e)
    {
        var ch = e.Key switch
        {
            >= Key.A and <= Key.Z => (char)('A' + (e.Key - Key.A)),
            >= Key.D0 and <= Key.D9 => (char)('0' + (e.Key - Key.D0)),
            _ => default,
        };

        if (ch == default) return;
        ch = char.ToUpperInvariant(ch);

        foreach (var entry in Menu.Items)
        {
            if (entry is not MenuItem item || !item.IsEffectivelyEnabled)
                continue;

            var parsed = item.GetParsedText();
            if (parsed.accessKey == default)
                continue;

            if (char.ToUpperInvariant(parsed.accessKey) != ch)
                continue;

            if (item.SubMenu != null)
            {
                int index = Menu.Items.IndexOf(item);
                if (index >= 0 && TryGetEntryRowBounds(index, out var rowBounds))
                    OpenSubMenu(index, item.SubMenu, rowBounds);
            }
            else
            {
                InvokeItem(item);
                var root = FindVisualRoot();
                if (root is Window window)
                    CloseHierarchy(window);
            }

            e.Handled = true;
            return;
        }
    }

    void IPopupOwner.OnPopupClosed(UIElement popup, PopupCloseKind kind)
    {
        if (_openSubMenu != null && popup == _openSubMenu)
        {
            _openSubMenu = null;
            SetOpenSubMenuIndex(-1);
        }
    }

    private void OpenSubMenu(int index, Menu subMenu, Rect ownerRowBounds)
    {
        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        CloseSubMenu();

        var subMenuPopup = new ContextMenu(subMenu)
        {
            ItemHeight = ItemHeight,
            MaxMenuHeight = MaxMenuHeight,
            Foreground = Foreground,
            ItemPadding = ItemPadding,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
        };
        if (!double.IsNaN(subMenu.ItemHeight) && subMenu.ItemHeight > 0)
        {
            subMenuPopup.ItemHeight = subMenu.ItemHeight;
        }
        if (subMenu.ItemPadding is Thickness subPadding)
        {
            subMenuPopup.ItemPadding = subPadding;
        }
        subMenuPopup._parentMenu = this;

        // Sub-menus inherit the same target snapshot so nesting never re-targets commands.
        subMenuPopup._capturedCommandTarget = _capturedCommandTarget;
        subMenuPopup._capturedCommandArgument = _capturedCommandArgument;
        subMenuPopup.SetValue(PlacementTargetPropertyKey, PlacementTarget);
        subMenuPopup.UpdateCommandPresentation(window);

        // Deferred for the same reason as ShowAt: an unstyled pre-attach measure loses the border.
        window.ShowPopup(this, subMenuPopup, w => MeasureSubMenuPlacement(w, subMenuPopup, ownerRowBounds));
        _openSubMenu = subMenuPopup;
        SetOpenSubMenuIndex(index);
    }

    private Rect MeasureSubMenuPlacement(Window window, ContextMenu subMenuPopup, Rect ownerRowBounds)
    {
        var region = window.GetPopupPlacementRegion(ownerRowBounds);
        subMenuPopup.Measure(new Size(Math.Max(0, region.Width), Math.Max(0, region.Height)));
        var desired = subMenuPopup.DesiredSize;

        double width = Math.Max(0, desired.Width);
        double height = Math.Max(0, desired.Height);
        double maxH = Math.Max(0, subMenuPopup.MaxMenuHeight);
        if (maxH > 0)
        {
            height = Math.Min(height, maxH);
        }
        height = Math.Min(height, Math.Max(0, region.Height));

        // Place to the right of the row (WPF-like), clamped to the placement region.
        const double horizontalOffset = 2;
        double verticalOffset = -(BorderThickness + Padding.Top);
        double x = ownerRowBounds.Right + horizontalOffset;
        double y = ownerRowBounds.Y + verticalOffset;

        if (x + width > region.Right)
        {
            x = Math.Max(region.X, ownerRowBounds.X - horizontalOffset - width);
        }

        if (y + height > region.Bottom)
        {
            y = Math.Max(region.Y, region.Bottom - height);
        }

        return new Rect(x, y, width, height);
    }

    private void CloseSubMenu()
    {
        if (_openSubMenu == null)
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is Window window)
        {
            _openSubMenu.CloseDescendants(window);
            window.ClosePopup(_openSubMenu);
        }

        _openSubMenu = null;
        SetOpenSubMenuIndex(-1);
    }

    private void CloseDescendants(Window window)
    {
        if (_openSubMenu == null)
        {
            return;
        }

        _openSubMenu.CloseDescendants(window);
        window.ClosePopup(_openSubMenu);
        _openSubMenu = null;
        SetOpenSubMenuIndex(-1);
    }

    internal void CloseTree(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        CloseDescendants(window);
        window.ClosePopup(this);
    }

    private void CloseHierarchy(Window window)
    {
        for (ContextMenu? current = this; current != null; current = current._parentMenu)
        {
            current.CloseDescendants(window);
            window.ClosePopup(current);
        }
    }

    private int HitTestEntryIndex(Point position)
    {
        if (_vBar.IsVisible && _vBar.Bounds.Contains(position))
        {
            return -1;
        }

        var contentBounds = GetItemViewportBounds();

        if (!contentBounds.Contains(position))
        {
            return -1;
        }

        double y = (position.Y - contentBounds.Y) + _verticalOffset;
        double acc = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            double h = GetEntryHeight(Items[i]);
            if (y >= acc && y < acc + h)
            {
                return i;
            }
            acc += h;
        }

        return -1;
    }

    private bool TryGetEntryRowBounds(int index, out Rect rowBounds)
    {
        rowBounds = Rect.Empty;

        if (index < 0 || index >= Items.Count)
        {
            return false;
        }

        var contentBounds = GetItemViewportBounds();

        double y = contentBounds.Y - _verticalOffset;
        for (int i = 0; i < Items.Count; i++)
        {
            double h = GetEntryHeight(Items[i]);
            if (i == index)
            {
                rowBounds = new Rect(contentBounds.X, y, contentBounds.Width, h);
                return true;
            }

            y += h;
        }

        return false;
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
        {
            return _vBar;
        }

        return base.OnHitTest(point);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // The rows draw the entries; the menu draws only what surrounds them.
        var bounds = GetSnappedBorderBounds(Bounds);
        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, CornerRadius);
    }

    /// <summary>The clip the rows are drawn under, or null when the viewport has no area.</summary>
    private Rect? GetRowClip()
    {
        var contentBounds = GetContentViewportBounds();
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
        {
            return null;
        }

        return LayoutRounding.MakeClipRect(contentBounds, GetDpi() / 96.0);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (GetRowClip() is not Rect clip)
        {
            return;
        }

        context.Save();
        context.SetClip(clip);
        foreach (var row in _rows.Values)
        {
            row.Render(context);
        }

        context.Restore();
        _vBar.Render(context);
    }

    internal override void WriteComposition(Rendering.Retained.CompositionPlanBuilder builder)
    {
        builder.Content(0);
        if (GetRowClip() is not Rect clip)
        {
            return;
        }

        builder.PushClipRect(clip);
        foreach (var row in _rows.Values)
        {
            builder.Child(row);
        }

        builder.Pop();
        builder.Child(_vBar);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        foreach (var row in _rows.Values)
        {
            if (!visitor(row))
            {
                return false;
            }
        }

        return visitor(_vBar);
    }

    protected override void OnDispose()
    {
        Menu.Changed -= OnMenuChanged;
        ReleaseRows();
        _icons.Clear();
        base.OnDispose();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        if (property.Id == FontFamilyProperty.Id ||
            property.Id == FontSizeProperty.Id ||
            property.Id == FontWeightProperty.Id)
        {
            _textLayouts.Invalidate();
        }

        base.OnMewPropertyChanged(property);
    }

    protected override void OnFontCacheInvalidated(MewProperty property)
    {
        base.OnFontCacheInvalidated(property);
        _textLayouts.Invalidate();
    }
}
