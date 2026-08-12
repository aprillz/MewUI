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

    // Owner context captured at ShowAt (or inherited from the parent menu / preset by MenuBar):
    // command items resolve CanExecute, execution and shortcut labels against it so popup focus
    // never changes the semantic target.
    private CommandTarget _capturedCommandTarget;
    private CommandTarget? _presetCommandTarget;

    private const double SubMenuGlyphAreaWidth = 14;
    private const double ShortcutColumnGap = 12;
    private const double IconTextGap = 8;
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
    private double _maxShortcutWidth;
    private bool _hasAnyShortcut;
    private readonly Dictionary<MenuItem, FrameworkElement> _materializedIcons = new();
    private bool _hasAnyIcon;

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
        MewProperty<double>.Register<ContextMenu>(nameof(MaxMenuHeight), 320.0, MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// Gets or sets the maximum height of the menu.
    /// </summary>
    public double MaxMenuHeight
    {
        get => GetValue(MaxMenuHeightProperty);
        set => SetValue(MaxMenuHeightProperty, value);
    }

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

    private void OnMenuChanged(MenuModelChange change)
    {
        if ((change & MenuModelChange.Structure) != 0 && FindVisualRoot() is Window structureWindow)
        {
            CloseDescendants(structureWindow);
            _hotIndex = -1;
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

            PrepareMaterializedIcons();
            if (HasCommandItems()) window.RegisterCommandSource(this);
            else window.UnregisterCommandSource(this);
            InvalidateMeasure();
        }

        InvalidateVisual();
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
            if (entry is MenuItem item && item.Command is Command command)
            {
                bool enabled = window.CommandRouter.CanExecute(command, _capturedCommandTarget);
                string? shortcutText =
                    InputMapResolver.TryGetEffectiveGesture(window, command, _capturedCommandTarget.OriginElement, out var gesture)
                        ? gesture.ToDisplayString()
                        : null;
                item.ApplyCommandState(enabled, shortcutText);
            }
        }
    }

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
        double size = Theme.Metrics.ContextMenuIconSize;
        return double.IsFinite(size) && size > 0 ? size : 16;
    }

    private void PrepareMaterializedIcons()
    {
        ClearMaterializedIcons();

        var size = IconTemplate.ResolveSize(ResolveIconSize(), GetDpi() / 96.0);
        foreach (var entry in Items)
        {
            if (entry is not MenuItem item || item.ResolveIconTemplate() is not IconTemplate template)
            {
                continue;
            }

            var icon = template.Build(size);
            icon.Width = size.Dip;
            icon.Height = size.Dip;
            icon.IsHitTestVisible = false;
            icon.Parent = this;
            _materializedIcons.Add(item, icon);
        }

        _hasAnyIcon = _materializedIcons.Count > 0;
    }

    private void ClearMaterializedIcons()
    {
        foreach (var icon in _materializedIcons.Values)
        {
            if (ReferenceEquals(icon.Parent, this))
            {
                icon.Parent = null;
            }
        }

        _materializedIcons.Clear();
        _hasAnyIcon = false;
    }

    public void ShowAt(UIElement owner, Point positionInWindow, double? anchorTopY = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var root = owner.FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        _capturedCommandTarget = _presetCommandTarget ?? CommandTarget.From(owner);

        UpdateCommandPresentation(window);
        PrepareMaterializedIcons();
        CloseDescendants(window);
        _parentMenu = null;

        // Measure without passing infinity into backends that may convert widths to ints.
        var region = window.GetPopupPlacementRegion(new Rect(positionInWindow.X, positionInWindow.Y, 0, 0));
        Measure(new Size(Math.Max(0, region.Width), Math.Max(0, region.Height)));
        var desired = DesiredSize;

        double width = Math.Max(0, desired.Width);
        double height = Math.Max(0, desired.Height);

        double maxH = Math.Max(0, MaxMenuHeight);
        if (maxH > 0)
        {
            height = Math.Min(height, maxH);
        }

        double x = PopupPlacement.ClampHorizontal(positionInWindow.X, width, region, floorToLeftEdge: false);
        double y = positionInWindow.Y;

        if (y + height > region.Bottom)
        {
            // Flip above the anchor point (anchorTopY for MenuBar items, or the click Y for context menus).
            double flipAnchor = anchorTopY ?? positionInWindow.Y;
            double flippedY = flipAnchor - height;
            y = flippedY >= region.Y ? flippedY : Math.Max(region.Y, region.Bottom - height);
        }

        window.ShowPopup(owner, this, new Rect(x, y, width, height));
        window.FocusManager.SetFocus(this);
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
            ClearMaterializedIcons();
        }
    }

    void ICommandSource.EvaluateCommandState()
    {
        if (FindVisualRoot() is not Window window)
        {
            return;
        }

        bool changed = false;
        foreach (var entry in Menu.Items)
        {
            if (entry is MenuItem item && item.Command is Command command)
            {
                bool enabled = window.CommandRouter.CanExecute(command, _capturedCommandTarget);
                string? shortcutText =
                    InputMapResolver.TryGetEffectiveGesture(window, command, _capturedCommandTarget.OriginElement, out var gesture)
                        ? gesture.ToDisplayString()
                        : null;
                changed |= item.ApplyCommandState(enabled, shortcutText);
            }
        }

        if (changed)
        {
            InvalidateVisual();
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

        if (oldTheme.Metrics.ContextMenuIconSize != newTheme.Metrics.ContextMenuIconSize &&
            FindVisualRoot() is Window)
        {
            PrepareMaterializedIcons();
            InvalidateMeasure();
        }
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _textLayouts.Invalidate();
        if (FindVisualRoot() is Window)
        {
            PrepareMaterializedIcons();
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
            ArrangeMaterializedIcons();
            CloseSubMenu();
            InvalidateVisual();
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

        // Cap height (scrolling can come later).
        double maxH = Math.Max(0, MaxMenuHeight);
        if (maxH > 0)
        {
            contentH = Math.Min(contentH, maxH);
        }

        _viewportHeight = Math.Max(0, contentH - Padding.VerticalThickness);

        return new Size(contentW, contentH).Inflate(new Thickness(borderInset));
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

        double onePx = dpiScale > 0 ? 1.0 / dpiScale : 1;
        bool needV = _extentHeight > _viewportHeight + onePx;
        _vBar.IsVisible = needV;

        if (!needV)
        {
            _verticalOffset = 0;
            _vBar.Value = 0;
            _vBar.Arrange(Rect.Empty);
            ArrangeMaterializedIcons();
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
        ArrangeMaterializedIcons();
    }

    private void ArrangeMaterializedIcons()
    {
        if (!_hasAnyIcon || Bounds.IsEmpty)
        {
            return;
        }

        var contentBounds = GetItemViewportBounds();
        double size = ResolveIconSize();
        double y = contentBounds.Y - _verticalOffset;
        foreach (var entry in Items)
        {
            double height = GetEntryHeight(entry);
            if (entry is MenuItem item && _materializedIcons.TryGetValue(item, out var icon))
            {
                var paddedRow = new Rect(contentBounds.X, y, contentBounds.Width, height).Deflate(ItemPadding);
                icon.Arrange(new Rect(
                    paddedRow.X,
                    paddedRow.Y + Math.Max(0, (paddedRow.Height - size) / 2),
                    size,
                    size));
            }

            y += height;
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
        if (_hotIndex != -1)
        {
            _hotIndex = -1;
            InvalidateVisual();
        }
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
            ArrangeMaterializedIcons();
            CloseSubMenu();
            InvalidateVisual();
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
        if (_hotIndex != index)
        {
            _hotIndex = index;
            InvalidateVisual();
        }

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
                window.CommandRouter.TryExecuteFromInput(command, _capturedCommandTarget, this);
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
            _openSubMenuIndex = -1;
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
        subMenuPopup.UpdateCommandPresentation(window);
        subMenuPopup.PrepareMaterializedIcons();

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

        window.ShowPopup(this, subMenuPopup, new Rect(x, y, width, height));
        _openSubMenu = subMenuPopup;
        _openSubMenuIndex = index;
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
        _openSubMenuIndex = -1;
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
        _openSubMenuIndex = -1;
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
        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        double radius = CornerRadius;
        var borderInset = GetBorderVisualInset();
        double itemRadius = Math.Max(0, LayoutRounding.RoundToPixel(radius, dpiScale) - borderInset);

        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, radius);

        var innerBounds = bounds.Deflate(new Thickness(borderInset));
        var contentBounds = GetContentViewportBounds();
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
        {
            return;
        }

        var factory = GetGraphicsFactory();
        var style = GetTextRunStyle();
        uint dpi = GetDpi();

        context.Save();
        context.SetClip(LayoutRounding.MakeClipRect(contentBounds, dpiScale));

        double y = contentBounds.Y - _verticalOffset;
        for (int i = 0; i < Items.Count; i++)
        {
            var entry = Items[i];
            double h = GetEntryHeight(entry);
            // Snap each row to the device pixel grid so highlight, separator and text stay crisp at
            // fractional scales (e.g. 125%). y keeps the true unsnapped running position, so a row's
            // snapped bottom equals the next row's snapped top and rows tile without a seam.
            double rowTop = LayoutRounding.RoundToPixel(y, dpiScale);
            double rowBottom = LayoutRounding.RoundToPixel(y + h, dpiScale);
            var row = new Rect(contentBounds.X, rowTop, contentBounds.Width, rowBottom - rowTop);
            if (row.Bottom < contentBounds.Y)
            {
                y += h;
                continue;
            }

            if (entry is MenuSeparator)
            {
                double onePx = 1.0 / dpiScale;
                double sepY = LayoutRounding.RoundToPixel(row.Y + (row.Height - onePx) / 2, dpiScale);
                context.FillRectangle(new Rect(row.X + 4, sepY, row.Width - 8, onePx), Theme.Palette.ControlBorder);
                y += h;
                continue;
            }

            if (entry is MenuItem item)
            {
                bool isHot = i == _hotIndex || i == _openSubMenuIndex;
                var bg = isHot ? Theme.Palette.SelectionBackground.WithAlpha((byte)(0.6 * 255)) : Color.Transparent;
                if (bg.A > 0)
                {
                    if (itemRadius > 0)
                    {
                        context.FillRoundedRectangle(row, itemRadius, itemRadius, bg);
                    }
                    else
                    {
                        context.FillRectangle(row, bg);
                    }
                }

                var fg = item.IsEffectivelyEnabled ? Foreground : Theme.Palette.DisabledText;
                var chevronReserved = item.SubMenu != null ? SubMenuGlyphAreaWidth : 0;

                var paddedRow = row.Deflate(ItemPadding);

                double textLeft = paddedRow.X;
                if (_hasAnyIcon)
                {
                    if (_materializedIcons.TryGetValue(item, out var icon))
                    {
                        if (!item.IsEffectivelyEnabled)
                        {
                            context.BeginOpacity(0.5);
                        }

                        icon.Render(context);

                        if (!item.IsEffectivelyEnabled)
                        {
                            context.EndOpacity();
                        }
                    }

                    textLeft += ResolveIconSize() + IconTextGap;
                }
                double textRight = paddedRow.Right - chevronReserved;
                if (_hasAnyShortcut)
                {
                    textRight -= (_maxShortcutWidth + ShortcutColumnGap);
                }

                var textRect = new Rect(textLeft, paddedRow.Y, Math.Max(0, textRight - textLeft), paddedRow.Height);
                var showAccessKeys = GetValue(Window.ShowAccessKeysProperty);
                var parsed = item.GetParsedText();
                var textLayout = _textLayouts.GetOrCreate(
                    factory, parsed.displayText, dpi, in style, textRect.Width, textRect.Height);
                if (textLayout != null)
                {
                    MenuTextLayouts.Draw(
                        context, textLayout, textRect, fg, showAccessKeys, parsed.underlineIndex);
                }

                var shortcutText = item.GetShortcutDisplayText();
                if (_hasAnyShortcut && !string.IsNullOrEmpty(shortcutText))
                {
                    double shortcutRight = paddedRow.Right - chevronReserved;
                    double shortcutLeft = shortcutRight - _maxShortcutWidth;
                    var shortcutRect = new Rect(shortcutLeft, paddedRow.Y, Math.Max(0, shortcutRight - shortcutLeft), paddedRow.Height);
                    var shortcutLayout = _textLayouts.GetOrCreate(
                        factory,
                        shortcutText,
                        dpi,
                        in style,
                        shortcutRect.Width,
                        shortcutRect.Height,
                        TextAlignment.Right);
                    if (shortcutLayout != null)
                    {
                        MenuTextLayouts.Draw(context, shortcutLayout, shortcutRect, fg);
                    }
                }

                if (item.SubMenu != null)
                {
                    // Submenu chevron indicator (matches ComboBox/TreeView chevron style).
                    var center = new Point(paddedRow.Right - (SubMenuGlyphAreaWidth / 2), paddedRow.Y + paddedRow.Height / 2);
                    Glyph.Draw(context, center, size: 3, fg, GlyphKind.ChevronRight);
                }
            }

            y += h;
            if (y > contentBounds.Bottom)
            {
                break;
            }
        }

        context.Restore();

        if (_vBar.IsVisible)
        {
            _vBar.Render(context);
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        foreach (var icon in _materializedIcons.Values)
        {
            if (!visitor(icon))
            {
                return false;
            }
        }

        return visitor(_vBar);
    }

    protected override void OnDispose()
    {
        Menu.Changed -= OnMenuChanged;
        ClearMaterializedIcons();
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
