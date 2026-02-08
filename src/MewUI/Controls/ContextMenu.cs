using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A context menu popup control for displaying menu items.
/// </summary>
public sealed class ContextMenu : Control, IPopupOwner
{
    private const double SubMenuGlyphAreaWidth = 14;
    private const double ShortcutColumnGap = 12;
    private readonly ScrollBar _vBar;
    private readonly ScrollController _scroll = new();
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
    public double ItemHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    } = double.NaN;

    /// <summary>
    /// Gets or sets the padding around menu items.
    /// </summary>
    public Thickness ItemPadding
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum height of the menu.
    /// </summary>
    public double MaxMenuHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = 320;

    /// <summary>
    /// Gets whether the context menu can receive keyboard focus.
    /// </summary>
    public override bool Focusable => true;

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
        BorderThickness = 1;
        Padding = new Thickness(1);

        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false, Parent = this };
        _vBar.ValueChanged += v =>
        {
            UpdateScrollFromBar(v);
        };
    }

    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    public void AddItem(string text, Action? onClick = null, bool isEnabled = true, string? shortcutText = null)
    {
        Menu.Item(text, onClick, isEnabled, shortcutText);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void AddSubMenu(string text, Menu subMenu, bool isEnabled = true, string? shortcutText = null)
    {
        ArgumentNullException.ThrowIfNull(subMenu);
        Menu.SubMenu(text, subMenu, isEnabled, shortcutText);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void AddEntry(MenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Menu.Add(entry);
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
        InvalidateMeasure();
        InvalidateVisual();
    }

    internal void ShowAt(UIElement owner, Point positionInWindow)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var root = owner.FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        CloseDescendants(window);
        _parentMenu = null;

        // Measure without passing infinity into backends that may convert widths to ints.
        var client = window.ClientSizeDip;
        Measure(new Size(Math.Max(0, client.Width), Math.Max(0, client.Height)));
        var desired = DesiredSize;

        double width = Math.Max(0, desired.Width);
        double height = Math.Max(0, desired.Height);

        double maxH = Math.Max(0, MaxMenuHeight);
        if (maxH > 0)
        {
            height = Math.Min(height, maxH);
        }

        double x = positionInWindow.X;
        double y = positionInWindow.Y;

        if (x + width > client.Width)
        {
            x = Math.Max(0, client.Width - width);
        }

        if (y + height > client.Height)
        {
            y = Math.Max(0, client.Height - height);
        }

        window.ShowPopup(owner, this, new Rect(x, y, width, height));
        window.FocusManager.SetFocus(this);
    }

    private double ResolveItemHeight()
    {
        if (!double.IsNaN(ItemHeight) && ItemHeight > 0)
        {
            return ItemHeight;
        }

        
        return Math.Max(18, Theme.Metrics.BaseControlHeight - 2);
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        if (ItemPadding == oldTheme.Metrics.ItemPadding)
        {
            ItemPadding = newTheme.Metrics.ItemPadding;
        }
    }

    private double GetEntryHeight(MenuEntry entry)
    {
        if (entry is MenuSeparator)
        {
            return MenuSeparator.MenuSeparatorHeight;
        }

        return ResolveItemHeight();
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

    private Rect GetItemViewportBounds()
    {
        var contentBounds = GetContentViewportBounds();
        if (!_vBar.IsVisible)
        {
            return contentBounds;
        }

        var dpiScale = GetDpi() / 96.0;
        double t = Theme.Metrics.ScrollBarHitThickness;
        return LayoutRounding.SnapViewportRectToPixels(new Rect(
            contentBounds.X,
            contentBounds.Y,
            Math.Max(0, contentBounds.Width - t),
            contentBounds.Height), dpiScale);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();

        double height = 0;
        double itemHeight = ResolveItemHeight();
        

        using var measure = BeginTextMeasurement();

        _maxTextWidth = 0;
        _maxShortcutWidth = 0;
        _hasAnyShortcut = false;
        bool hasAnySubMenu = false;

        foreach (var entry in Items)
        {
            if (entry is MenuSeparator)
            {
                height += MenuSeparator.MenuSeparatorHeight;
                continue;
            }

            if (entry is MenuItem item)
            {
                var text = item.Text ?? string.Empty;
                var size = string.IsNullOrEmpty(text) ? Size.Empty : measure.Context.MeasureText(text, measure.Font);
                _maxTextWidth = Math.Max(_maxTextWidth, size.Width);

                var shortcutText = item.ShortcutText;
                if (!string.IsNullOrEmpty(shortcutText))
                {
                    _hasAnyShortcut = true;
                    var shortcutSize = measure.Context.MeasureText(shortcutText, measure.Font);
                    _maxShortcutWidth = Math.Max(_maxShortcutWidth, shortcutSize.Width);
                }

                hasAnySubMenu |= item.SubMenu != null;

                height += itemHeight;
            }
        }

        double maxWidth = _maxTextWidth + ItemPadding.HorizontalThickness;

        if (_hasAnyShortcut)
        {
            maxWidth += ShortcutColumnGap + _maxShortcutWidth;
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

        bool needV = _extentHeight > _viewportHeight + 0.5;
        _vBar.IsVisible = needV;

        if (!needV)
        {
            _verticalOffset = 0;
            _vBar.Value = 0;
            _vBar.Arrange(Rect.Empty);
            return;
        }

        double t = Theme.Metrics.ScrollBarHitThickness;
        var itemBounds = new Rect(contentBounds.X, contentBounds.Y, Math.Max(0, contentBounds.Width - t), contentBounds.Height);
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

        _vBar.Arrange(new Rect(
            itemBounds.Right,
            itemBounds.Y,
            t,
            Math.Max(0, itemBounds.Height)));
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

        int notches = Math.Sign(e.Delta);
        if (notches == 0)
        {
            return;
        }

        var dpiScale = GetDpi() / 96.0;
        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(1, _extentHeight, _viewportHeight);
        if (_scroll.ScrollByNotches(1, -notches, Theme.Metrics.ScrollWheelStep))
        {
            _verticalOffset = _scroll.GetOffsetDip(1);
            _vBar.Value = _verticalOffset;
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

        if (index >= 0 && index < Items.Count && Items[index] is MenuItem item && item.SubMenu != null && item.IsEnabled)
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

        if (!IsEnabled || e.Handled || e.Button != MouseButton.Left)
        {
            return;
        }

        int index = HitTestEntryIndex(e.Position);
        if (index < 0 || index >= Items.Count)
        {
            return;
        }

        if (Items[index] is MenuItem item && item.IsEnabled)
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

            item.Click?.Invoke();

            var root = FindVisualRoot();
            if (root is Window window)
            {
                CloseHierarchy(window);
            }

            e.Handled = true;
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
        }
    }

    void IPopupOwner.OnPopupClosed(UIElement popup)
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

        var client = window.ClientSizeDip;
        subMenuPopup.Measure(new Size(Math.Max(0, client.Width), Math.Max(0, client.Height)));
        var desired = subMenuPopup.DesiredSize;

        double width = Math.Max(0, desired.Width);
        double height = Math.Max(0, desired.Height);
        double maxH = Math.Max(0, subMenuPopup.MaxMenuHeight);
        if (maxH > 0)
        {
            height = Math.Min(height, maxH);
        }

        // Place to the right of the row (WPF-like), clamped to window client.
        const double horizontalOffset = 2;
        const double verticalOffset = -2;
        double x = ownerRowBounds.Right + horizontalOffset;
        double y = ownerRowBounds.Y + verticalOffset;

        if (x + width > client.Width)
        {
            x = Math.Max(0, ownerRowBounds.X - horizontalOffset - width);
        }

        if (y + height > client.Height)
        {
            y = Math.Max(0, client.Height - height);
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
        double radius = Theme.Metrics.ControlCornerRadius;
        var borderInset = GetBorderVisualInset();
        double itemRadius = Math.Max(0, radius - borderInset);

        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, radius);

        var innerBounds = bounds.Deflate(new Thickness(borderInset));
        var contentBounds = GetContentViewportBounds();
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
        {
            return;
        }

        using var measure = BeginTextMeasurement();
        var font = measure.Font;

        context.Save();
        context.SetClip(LayoutRounding.MakeClipRect(contentBounds, dpiScale));

        double barOverlayWidth = _vBar.IsVisible ? Theme.Metrics.ScrollBarHitThickness : 0;
        double y = contentBounds.Y - _verticalOffset;
        for (int i = 0; i < Items.Count; i++)
        {
            var entry = Items[i];
            double h = GetEntryHeight(entry);
            var row = new Rect(contentBounds.X, y, contentBounds.Width, h);
            if (row.Bottom < contentBounds.Y)
            {
                y += h;
                continue;
            }

            // Keep content (text/chevrons/separators) out of the scrollbar overlay area,
            // but allow background/selection to extend under the overlay.
            var layoutRow = barOverlayWidth > 0
                ? new Rect(row.X, row.Y, Math.Max(0, row.Width - barOverlayWidth), row.Height)
                : row;

            if (entry is MenuSeparator)
            {
                var sepY = row.Y + (row.Height - 1) / 2;
                context.DrawLine(new Point(layoutRow.X + 4, sepY), new Point(layoutRow.Right - 4, sepY), Theme.Palette.ControlBorder, 1);
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

                var fg = item.IsEnabled ? Foreground : Theme.Palette.DisabledText;
                var chevronReserved = item.SubMenu != null ? SubMenuGlyphAreaWidth : 0;

                var paddedRow = layoutRow.Deflate(ItemPadding);

                double textLeft = paddedRow.X;
                double textRight = paddedRow.Right - chevronReserved;
                if (_hasAnyShortcut)
                {
                    textRight -= (_maxShortcutWidth + ShortcutColumnGap);
                }

                var textRect = new Rect(textLeft, paddedRow.Y, Math.Max(0, textRight - textLeft), paddedRow.Height);
                context.DrawText(item.Text ?? string.Empty, textRect, font, fg,
                    TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

                if (_hasAnyShortcut && !string.IsNullOrEmpty(item.ShortcutText))
                {
                    double shortcutRight = paddedRow.Right - chevronReserved;
                    double shortcutLeft = shortcutRight - _maxShortcutWidth;
                    var shortcutRect = new Rect(shortcutLeft, paddedRow.Y, Math.Max(0, shortcutRight - shortcutLeft), paddedRow.Height);
                    context.DrawText(item.ShortcutText, shortcutRect, font, fg,
                        TextAlignment.Right, TextAlignment.Center, TextWrapping.NoWrap);
                }

                if (item.SubMenu != null)
                {
                    // Submenu chevron indicator (matches ComboBox/TreeView chevron style).
                    var center = new Point(paddedRow.Right - (SubMenuGlyphAreaWidth / 2), paddedRow.Y + paddedRow.Height / 2);
                    ChevronGlyph.Draw(context, center, size: 3, fg, ChevronDirection.Right);
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
}
