using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A horizontal menu bar control for application menus.
/// </summary>
public sealed class MenuBar : Control, IPopupOwner
{
    private const double ItemHorizontalPadding = 10;
    private const double ItemVerticalPadding = 4;

    private readonly List<MenuItem> _items = new();
    private readonly List<Rect> _itemBounds = new();
    private readonly List<KeyBinding> _registeredBindings = new();
    private int _hotIndex = -1;
    private int _openIndex = -1;
    private ContextMenu? _openPopup;

    /// <summary>
    /// Gets the menu items collection.
    /// </summary>
    public IList<MenuItem> Items => _items;

    public static readonly MewProperty<double> SpacingProperty =
        MewProperty<double>.Register<MenuBar>(nameof(Spacing), 2.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<bool> DrawBottomSeparatorProperty =
        MewProperty<bool>.Register<MenuBar>(nameof(DrawBottomSeparator), true, MewPropertyOptions.AffectsRender);

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
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        UnregisterKeyBindings(oldRoot as Window);
        RegisterKeyBindings(newRoot as Window);
    }

    private void RegisterKeyBindings(Window? window)
    {
        if (window == null) return;

        foreach (var item in _items)
            RegisterMenuItemBindings(window, item);

        RegisterAccessKeys(window);
    }

    private void UnregisterKeyBindings(Window? window)
    {
        if (window == null) return;

        if (_registeredBindings.Count > 0)
        {
            for (int i = 0; i < _registeredBindings.Count; i++)
                window.KeyBindings.Remove(_registeredBindings[i]);
            _registeredBindings.Clear();
        }

        window.AccessKeyManager.Unregister(this);
    }

    private void RegisterAccessKeys(Window window)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (AccessKeyHelper.TryParse(item.Text ?? string.Empty, out var key, out _))
            {
                int index = i; // capture for closure
                window.AccessKeyManager.Register(key, this, () => OpenMenu(index));
            }
        }
    }

    private static string GetDisplayText(MenuItem item)
    {
        var raw = item.Text ?? string.Empty;
        return AccessKeyHelper.TryParse(raw, out _, out var display) ? display : raw;
    }

    private void RegisterMenuItemBindings(Window window, MenuItem item)
    {
        if (item.Shortcut is { } gesture && item.Click is { } click)
        {
            var binding = new KeyBinding(gesture, click);
            window.KeyBindings.Add(binding);
            _registeredBindings.Add(binding);
        }

        if (item.SubMenu != null)
        {
            foreach (var entry in item.SubMenu.Items)
            {
                if (entry is MenuItem sub)
                    RegisterMenuItemBindings(window, sub);
            }
        }
    }

    /// <summary>
    /// Adds a menu item to the menu bar.
    /// </summary>
    /// <param name="item">The menu item to add.</param>
    public void Add(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Sets the menu items collection.
    /// </summary>
    /// <param name="items">The menu items to set.</param>
    public void SetItems(params MenuItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CloseOpenMenu();
        UnregisterKeyBindings(FindVisualRoot() as Window);
        _items.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            Add(items[i]);
        }
        RegisterKeyBindings(FindVisualRoot() as Window);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        using var measure = BeginTextMeasurement();

        double w = Padding.HorizontalThickness;
        double maxH = 0;
        bool first = true;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var text = GetDisplayText(item);
            var textSize = string.IsNullOrEmpty(text) ? Size.Empty : measure.Context.MeasureText(text, measure.Font);
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
        using var measure = BeginTextMeasurement();

        _itemBounds.Clear();
        double x = bounds.X + Padding.Left;
        double y = bounds.Y + Padding.Top;
        double innerH = Math.Max(0, bounds.Height - Padding.VerticalThickness);

        bool first = true;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var text = GetDisplayText(item);
            var textSize = string.IsNullOrEmpty(text) ? Size.Empty : measure.Context.MeasureText(text, measure.Font);
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

    public override bool Focusable => true;

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

        Focus();

        if (_openIndex == index)
        {
            CloseOpenMenu();
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
        if (item.SubMenu == null)
        {
            CloseOpenMenu();
            return;
        }

        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        CloseOpenMenu();

        _openIndex = index;
        InvalidateVisual();

        var popup = new ContextMenu(item.SubMenu);
        popup.FontFamily = FontFamily;
        popup.FontSize = FontSize;
        popup.FontWeight = FontWeight;

        _openPopup = popup;

        var b = _itemBounds.Count > index ? _itemBounds[index] : Rect.Empty;
        popup.ShowAt(this, new Point(b.X, b.Bottom + 1), anchorTopY: b.Y - 1);
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

        using var measure = BeginTextMeasurement();
        var font = measure.Font;

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

            var fg = item.IsEnabled ? Foreground : Theme.Palette.DisabledText;
            var textRect = row.Deflate(new Thickness(ItemHorizontalPadding, 0, ItemHorizontalPadding, 0));
            var showAccessKeys = GetValue(Window.ShowAccessKeysProperty);
            AccessKeyRenderer.DrawText(context, item.Text ?? string.Empty, textRect, font, fg, showAccessKeys, dpiScale: GetDpi() / 96.0);
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
}
