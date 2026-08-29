using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A horizontal menu bar control for application menus.
/// </summary>
public sealed partial class MenuBar : Control, IPopupOwner
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<MenuBar>(DefaultStyles.CreateMenuBarStyle);

    private const double ItemHorizontalPadding = 10;
    private const double ItemVerticalPadding = 4;

    private readonly MenuBarItemCollection _items = new();
    private readonly List<Rect> _itemBounds = new();
    private readonly MenuTextLayouts _textLayouts = new();
    private int _hotIndex = -1;
    private int _openIndex = -1;
    private ContextMenu? _openPopup;

    // Focused context captured before the menu bar takes focus, so command items in the opened
    // menus resolve against the content that was active when the interaction started.
    private CommandTarget? _preMenuTarget;

    /// <summary>
    /// Gets the menu items collection.
    /// </summary>
    public IList<MenuItem> Items => _items;

    public static readonly MewProperty<double> SpacingProperty =
        MewProperty<double>.Register<MenuBar>(nameof(Spacing), 2.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<bool> DrawBottomSeparatorProperty =
        MewProperty<bool>.Register<MenuBar>(nameof(DrawBottomSeparator), true, MewPropertyOptions.AffectsRender);

    static MenuBar()
    {
        FocusableProperty.OverrideDefaultValue<MenuBar>(true);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to draw a bottom separator line below the menu bar. 
    /// </summary>
    public bool DrawBottomSeparator
    {
        get => GetValue(DrawBottomSeparatorProperty);
        set => SetValue(DrawBottomSeparatorProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing between menu items.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the MenuBar class.
    /// </summary>
    public MenuBar()
    {
        _items.Changed += OnItemsChanged;
    }

    private void OnItemsChanged(MenuModelChange change)
    {
        if ((change & (MenuModelChange.Structure | MenuModelChange.SubMenu)) != 0)
        {
            CloseOpenMenu();
        }

        if ((change & (MenuModelChange.Structure | MenuModelChange.Text |
            MenuModelChange.Command)) != 0)
        {
            _textLayouts.Invalidate();
            InvalidateMeasure();

            var window = FindVisualRoot() as Window;
            UnregisterAccessKeys(window);
            RegisterAccessKeys(window);
        }

        if ((change & MenuModelChange.All) != 0)
        {
            InvalidateVisual();
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        UnregisterAccessKeys(oldRoot as Window);
        RegisterAccessKeys(newRoot as Window);
    }

    private void RegisterAccessKeys(Window? window)
    {
        if (window == null) return;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var parsed = item.GetParsedText();
            if (parsed.accessKey != default)
            {
                int index = i; // capture for closure
                window.AccessKeyManager.Register(parsed.accessKey, this, () => OpenMenu(index));
            }
        }
    }

    private void UnregisterAccessKeys(Window? window) => window?.AccessKeyManager.Unregister(this);

    private static string GetDisplayText(MenuItem item)
        => item.GetParsedText().displayText;

    /// <summary>
    /// Adds a menu item to the menu bar.
    /// </summary>
    /// <param name="item">The menu item to add.</param>
    public void Add(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    /// <summary>
    /// Sets the menu items collection.
    /// </summary>
    /// <param name="items">The menu items to set.</param>
    public void SetItems(params MenuItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CloseOpenMenu();
        UnregisterAccessKeys(FindVisualRoot() as Window);
        _items.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            Add(items[i]);
        }
        RegisterAccessKeys(FindVisualRoot() as Window);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var factory = GetGraphicsFactory();
        var style = GetTextRunStyle();
        uint dpi = GetDpi();

        double w = Padding.HorizontalThickness;
        double maxH = 0;
        bool first = true;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var text = GetDisplayText(item);
            var textSize = _textLayouts.Measure(factory, text, dpi, in style);
            var itemW = textSize.Width + (ItemHorizontalPadding * 2);
            var itemH = textSize.Height + (ItemVerticalPadding * 2);

            if (!first)
            {
                w += Spacing;
            }

            w += itemW;
            maxH = Math.Max(maxH, itemH);
            first = false;
        }

        return new Size(w, maxH + Padding.VerticalThickness);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var factory = GetGraphicsFactory();
        var style = GetTextRunStyle();
        uint dpi = GetDpi();

        _itemBounds.Clear();
        double x = bounds.X + Padding.Left;
        double y = bounds.Y + Padding.Top;
        double innerH = Math.Max(0, bounds.Height - Padding.VerticalThickness);

        bool first = true;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var text = GetDisplayText(item);
            var textSize = _textLayouts.Measure(factory, text, dpi, in style);
            var itemW = textSize.Width + (ItemHorizontalPadding * 2);
            var itemH = Math.Min(innerH, textSize.Height + (ItemVerticalPadding * 2));

            if (!first)
            {
                x += Spacing;
            }

            var itemY = y + (innerH - itemH) / 2;
            _itemBounds.Add(new Rect(x, itemY, itemW, itemH));
            x += itemW;
            first = false;
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Handled)
        {
            return;
        }

        int index = HitTestItemIndex(e.Position);
        if (index != _hotIndex)
        {
            _hotIndex = index;
            InvalidateVisual();
        }

        if (_openIndex != -1 && index != -1 && index != _openIndex)
        {
            OpenMenu(index);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!IsEffectivelyEnabled || e.Handled || e.Button != MouseButton.Left)
        {
            return;
        }

        int index = HitTestItemIndex(e.Position);
        if (index == -1)
        {
            return;
        }

        if (_openIndex == -1 && FindVisualRoot() is Window window)
        {
            _preMenuTarget = window.CommandRouter.CaptureTarget();
        }

        Focus();

        if (_openIndex == index)
        {
            CloseOpenMenu();
            _preMenuTarget = null;
        }
        else
        {
            OpenMenu(index);
        }

        e.Handled = true;
    }

    private void OpenMenu(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        var item = _items[index];
        if (item.SubMenu == null || !item.IsEffectivelyEnabled)
        {
            CloseOpenMenu();
            return;
        }

        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        // Read the pre-menu target before CloseOpenMenu (a hover switch closes the previous popup,
        // which clears the pending capture) and restore it for the next switch.
        var target = _preMenuTarget ?? window.CommandRouter.CaptureTarget();
        CloseOpenMenu();
        _preMenuTarget = target;

        _openIndex = index;
        InvalidateVisual();

        var popup = new ContextMenu(item.SubMenu);
        popup.FontFamily = FontFamily;
        popup.FontSize = FontSize;
        popup.FontWeight = FontWeight;
        popup.SetCommandTarget(target);

        _openPopup = popup;

        popup.Placement = MenuPlacement.Below;
        popup.PlacementOffset = new Point(0, 1);
        var b = _itemBounds.Count > index ? _itemBounds[index] : Rect.Empty;
        popup.Show(this, b);
    }

    private void CloseOpenMenu()
    {
        if (_openIndex == -1 && _openPopup == null)
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is Window window && _openPopup != null)
        {
            _openPopup.CloseTree(window);
        }

        _openPopup = null;
        _openIndex = -1;
        InvalidateVisual();
    }

    void IPopupOwner.OnPopupClosed(UIElement popup, PopupCloseKind kind)
    {
        if (_openPopup != null && popup == _openPopup)
        {
            _openPopup = null;
            _openIndex = -1;
            _preMenuTarget = null;
            InvalidateVisual();
        }
    }

    private int HitTestItemIndex(Point position)
    {
        for (int i = 0; i < _itemBounds.Count; i++)
        {
            if (_itemBounds[i].Contains(position))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var bounds = GetSnappedBorderBounds(Bounds);
        context.FillRectangle(bounds, Background);

        var factory = GetGraphicsFactory();
        var style = GetTextRunStyle();
        uint dpi = GetDpi();

        for (int i = 0; i < _itemBounds.Count && i < _items.Count; i++)
        {
            var row = _itemBounds[i];
            var item = _items[i];

            var bg = Color.Transparent;
            if (_openIndex == i)
            {
                bg = Theme.Palette.SelectionBackground;
            }
            else if (_hotIndex == i)
            {
                bg = Theme.Palette.SelectionBackground.WithAlpha((byte)(0.6 * 255));
            }

            if (bg.A > 0)
            {
                if (CornerRadius - 1 is double r && r > 0)
                {
                    context.FillRoundedRectangle(row, r, r, bg);
                }
                else
                {
                    context.FillRectangle(row, bg);
                }
            }

            var fg = item.IsEffectivelyEnabled ? Foreground : Theme.Palette.DisabledText;
            var textRect = row.Deflate(new Thickness(ItemHorizontalPadding, 0, ItemHorizontalPadding, 0));
            var showAccessKeys = GetValue(Window.ShowAccessKeysProperty);
            var parsed = item.GetParsedText();
            var layout = _textLayouts.GetOrCreate(
                factory, parsed.displayText, dpi, in style, textRect.Width, textRect.Height);
            if (layout != null)
            {
                MenuTextLayouts.Draw(
                    context, layout, textRect, fg, showAccessKeys, parsed.underlineIndex);
            }
        }

        if (DrawBottomSeparator)
        {
            // Simple bottom separator.
            var dpiScale = GetDpi() / 96.0;
            var thickness = LayoutRounding.SnapThicknessToPixels(1.0 / dpiScale, dpiScale, 1);
            var rect = LayoutRounding.SnapBoundsRectToPixels(
                new Rect(bounds.X, bounds.Bottom - thickness, Math.Max(0, bounds.Width), thickness),
                dpiScale);
            context.FillRectangle(rect, Theme.Palette.ControlBorder);
        }
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

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _textLayouts.Invalidate();
    }

    protected override void OnFontCacheInvalidated(MewProperty property)
    {
        base.OnFontCacheInvalidated(property);
        _textLayouts.Invalidate();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _textLayouts.Invalidate();
    }

    protected override void OnDispose()
    {
        _items.Changed -= OnItemsChanged;
        UnregisterAccessKeys(FindVisualRoot() as Window);
        base.OnDispose();
    }
}
