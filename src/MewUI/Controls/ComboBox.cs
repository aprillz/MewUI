using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A drop-down selection control with text header and popup list.
/// </summary>
public sealed partial class ComboBox : Control, IPopupOwner
{
    private readonly TextWidthCache _textWidthCache = new(512);
    private bool _isDropDownOpen;
    private ListBox? _popupList;
    private bool _restoreFocusAfterPopupClose;
    private bool _updatingFromSource;
    private Rect? _lastPopupBounds;
    private bool _suppressItemsSelectionChanged;
    private IItemsView _itemsSource = ItemsView.Empty;
    private IDataTemplate? _itemTemplate;

    /// <summary>
    /// Gets or sets the items data source.
    /// </summary>
    public IItemsView ItemsSource
    {
        get => _itemsSource;
        set
        {
            value ??= ItemsView.Empty;
            if (ReferenceEquals(_itemsSource, value))
            {
                return;
            }

            int oldIndex = SelectedIndex;

            _itemsSource.Changed -= OnItemsChanged;
            _itemsSource.SelectionChanged -= OnItemsSelectionChanged;

            _itemsSource = value;
            _itemsSource.SelectionChanged += OnItemsSelectionChanged;
            _itemsSource.Changed += OnItemsChanged;

            _suppressItemsSelectionChanged = true;
            try
            {
                _itemsSource.SelectedIndex = oldIndex;
            }
            finally
            {
                _suppressItemsSelectionChanged = false;
            }

            if (_popupList != null)
            {
                SyncPopupList(_popupList);
            }

            int newIndex = _itemsSource.SelectedIndex;
            if (newIndex != oldIndex)
            {
                OnItemsSelectionChanged(newIndex);
            }

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the selected item index.
    /// </summary>
    public int SelectedIndex
    {
        get => ItemsSource.SelectedIndex;
        set => ItemsSource.SelectedIndex = value;
    }

    /// <summary>
    /// Gets the currently selected item object.
    /// </summary>
    public object? SelectedItem => ItemsSource.SelectedItem;

    /// <summary>
    /// Gets the currently selected item text.
    /// </summary>
    public string? SelectedText => SelectedIndex >= 0 && SelectedIndex < ItemsSource.Count ? ItemsSource.GetText(SelectedIndex) : null;

    /// <summary>
    /// Gets or sets the placeholder text shown when no item is selected.
    /// </summary>
    public string Placeholder
    {
        get;
        set
        {
            var v = value ?? string.Empty;
            if (Set(ref field, v))
            {
                InvalidateVisual();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the height of items in the dropdown list.
    /// </summary>
    public double ItemHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.NaN;

    /// <summary>
    /// Gets or sets the maximum height of the dropdown list.
    /// </summary>
    public double MaxDropDownHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = 160;

    /// <summary>
    /// Gets or sets the item template for the dropdown list. If null, the list uses its default template.
    /// </summary>
    public IDataTemplate? ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            if (ReferenceEquals(_itemTemplate, value))
            {
                return;
            }

            _itemTemplate = value;
            if (_popupList != null)
            {
                SyncPopupList(_popupList);
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the dropdown list is open.
    /// </summary>
    public bool IsDropDownOpen
    {
        get => _isDropDownOpen;
        set
        {
            if (_isDropDownOpen == value)
            {
                return;
            }

            _isDropDownOpen = value;
            if (_isDropDownOpen)
            {
                ShowPopup();
            }
            else
            {
                ClosePopup();
            }

            InvalidateVisual();
        }
    }

    /// <summary>
    /// Occurs when the selected item changes.
    /// </summary>
    public event Action<object?>? SelectionChanged;

    /// <summary>
    /// Gets whether the combobox can receive keyboard focus.
    /// </summary>
    public override bool Focusable => true;

    /// <summary>
    /// Gets the default background color.
    /// </summary>
    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    /// <summary>
    /// Gets the default border brush color.
    /// </summary>
    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    /// <summary>
    /// Gets the default minimum height.
    /// </summary>
    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    /// <summary>
    /// Initializes a new instance of the ComboBox class.
    /// </summary>
    public ComboBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(8, 4, 8, 4);
        // Do not set explicit Height, otherwise FrameworkElement.MeasureOverride will clamp DesiredSize
        // and the drop-down cannot expand. Use MinHeight as the default header height.
        Height = double.NaN;

        _itemsSource.SelectionChanged += OnItemsSelectionChanged;
        _itemsSource.Changed += OnItemsChanged;
    }

    private void OnItemsChanged(ItemsChange change)
    {
        if (_popupList != null)
        {
            SyncPopupList(_popupList);
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnItemsSelectionChanged(int index)
    {
        if (_suppressItemsSelectionChanged)
        {
            return;
        }

        if (!_updatingFromSource)
        {
            if (TryGetBinding(SelectedIndexBindingSlot, out ValueBinding<int> binding))
            {
                binding.Set(index);
            }
        }

        SelectionChanged?.Invoke(SelectedItem);
        InvalidateVisual();

        if (_popupList != null)
        {
            _popupList.SelectedIndex = index;
        }
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // The popup ListBox can exist while the dropdown is closed, so it won't be in the Window visual tree
        // and would miss InternalTheme broadcasts. Keep it in sync here.
        if (_popupList != null && _popupList.Parent == null)
        {
            _popupList.NotifyThemeChanged(oldTheme, newTheme);
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var headerHeight = ResolveHeaderHeight();
        double width = 80;
        var dpi = GetDpi();

        using (var measure = BeginTextMeasurement())
        {
            double maxWidth = 0;
            int count = ItemsSource.Count;
            _textWidthCache.SetCapacity(Math.Clamp(count + 8, 64, 4096));

            for (int i = 0; i < count; i++)
            {
                var item = ItemsSource.GetText(i);
                if (string.IsNullOrEmpty(item))
                {
                    continue;
                }

                maxWidth = Math.Max(maxWidth, _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, item));
            }

            if (!string.IsNullOrEmpty(Placeholder))
            {
                maxWidth = Math.Max(maxWidth, _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, Placeholder));
            }

            width = maxWidth + Padding.HorizontalThickness + ArrowAreaWidth;
        }

        return new Size(width, headerHeight);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        double radius = Theme.Metrics.ControlCornerRadius;

        var bg = IsEnabled ? Background : Theme.Palette.DisabledControlBackground;

        var borderColor = BorderBrush;
        if (IsEnabled)
        {
            // Keep focus highlight while the drop-down popup is open/focused.
            if (IsFocused || IsFocusWithin || IsDropDownOpen)
            {
                borderColor = Theme.Palette.Accent;
            }
            else if (IsMouseOver)
            {
                borderColor = BorderBrush.Lerp(Theme.Palette.Accent, 0.6);
            }
        }

        DrawBackgroundAndBorder(context, bounds, bg, borderColor, radius);

        var headerHeight = ResolveHeaderHeight();
        var headerRect = new Rect(bounds.X, bounds.Y, bounds.Width, headerHeight);
        var innerHeaderRect = headerRect.Deflate(new Thickness(borderInset));

        // Text
        var textRect = new Rect(innerHeaderRect.X, innerHeaderRect.Y, innerHeaderRect.Width - ArrowAreaWidth, innerHeaderRect.Height)
            .Deflate(Padding);

        string text = SelectedText ?? string.Empty;
        var textColor = IsEnabled ? Foreground : Theme.Palette.DisabledText;
        if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(Placeholder) && !IsFocused)
        {
            text = Placeholder;
            textColor = Theme.Palette.PlaceholderText;
        }

        if (!string.IsNullOrEmpty(text))
        {
            context.DrawText(text, textRect, GetFont(), textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }

        // Arrow
        DrawArrow(context, headerRect, IsEnabled ? textColor : Theme.Palette.DisabledText, IsDropDownOpen);

        if (IsDropDownOpen)
        {
            UpdatePopupBounds();
        }
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        if (!IsDropDownOpen)
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is not Window window)
        {
            IsDropDownOpen = false;
            return;
        }

        if (_popupList != null && window.FocusManager.FocusedElement == _popupList)
        {
            return;
        }

        IsDropDownOpen = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        Focus();

        var bounds = Bounds;
        double headerHeight = ResolveHeaderHeight();
        var headerRect = new Rect(bounds.X, bounds.Y, bounds.Width, headerHeight);

        if (headerRect.Contains(e.Position))
        {
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
            return;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsEnabled)
        {
            return;
        }

        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && IsDropDownOpen)
        {
            IsDropDownOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (!IsDropDownOpen)
            {
                IsDropDownOpen = true;
            }

            int count = ItemsSource.Count;
            if (count > 0)
            {
                SelectedIndex = Math.Min(count - 1, SelectedIndex < 0 ? 0 : SelectedIndex + 1);
            }

            if (_popupList != null)
            {
                _popupList.SelectedIndex = SelectedIndex;
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (!IsDropDownOpen)
            {
                IsDropDownOpen = true;
            }

            int count = ItemsSource.Count;
            if (count > 0)
            {
                SelectedIndex = Math.Max(0, SelectedIndex <= 0 ? 0 : SelectedIndex - 1);
            }

            if (_popupList != null)
            {
                _popupList.SelectedIndex = SelectedIndex;
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// Sets a two-way binding for the SelectedIndex property.
    /// </summary>
    /// <param name="get">Function to get the current value.</param>
    /// <param name="set">Action to set the value.</param>
    /// <param name="subscribe">Optional action to subscribe to change notifications.</param>
    /// <param name="unsubscribe">Optional action to unsubscribe from change notifications.</param>
    public void SetSelectedIndexBinding(
        Func<int> get,
        Action<int> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        SetSelectedIndexBindingCore(get, set, subscribe, unsubscribe);
    }

    private double ResolveItemHeight()
    {
        if (!double.IsNaN(ItemHeight) && ItemHeight > 0)
        {
            return ItemHeight;
        }

        return Math.Max(18, Theme.Metrics.BaseControlHeight - 2);
    }

    private double ResolveHeaderHeight()
    {
        if (!double.IsNaN(Height) && Height > 0)
        {
            return Height;
        }

        var min = MinHeight > 0 ? MinHeight : 0;
        return Math.Max(Math.Max(24, FontSize + Padding.VerticalThickness + 8), min);
    }

    private void DrawArrow(IGraphicsContext context, Rect headerRect, Color color, bool isUp)
    {
        double centerX = headerRect.Right - ArrowAreaWidth / 2;
        double centerY = headerRect.Y + headerRect.Height / 2;

        ChevronGlyph.Draw(
            context,
            new Point(centerX, centerY),
            size: 4,
            color,
            isUp ? ChevronDirection.Up : ChevronDirection.Down);
    }

    private const double ArrowAreaWidth = 22;

    private ListBox EnsurePopupList()
    {
        if (_popupList == null)
        {
            _popupList = new ListBox();
            _popupList.SelectionChanged += OnPopupListSelectionChanged;
            _popupList.ItemActivated += OnPopupListItemActivated;
        }

        return _popupList;
    }

    private void SyncPopupList(ListBox list)
    {
        list.ItemsSource = ItemsSource;
        list.SelectedIndex = SelectedIndex;
        list.ItemHeight = ResolveItemHeight();

        if (ItemTemplate != null)
        {
            list.ItemTemplate = ItemTemplate;
        }
    }

    private void ShowPopup()
    {
        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        var list = EnsurePopupList();
        SyncPopupList(list);

        var popupBounds = CalculatePopupBounds(window);
        window.ShowPopup(this, list, popupBounds);
        _lastPopupBounds = popupBounds;
        window.FocusManager.SetFocus(list);
    }

    private void OnPopupListSelectionChanged(object? _)
        => SelectedIndex = _popupList?.SelectedIndex ?? -1;

    private void OnPopupListItemActivated(int index)
    {
        SelectedIndex = index;
        _restoreFocusAfterPopupClose = true;
        IsDropDownOpen = false;
    }

    private void ClosePopup()
    {
        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        if (_popupList != null)
        {
            window.ClosePopup(_popupList);
        }

        _lastPopupBounds = null;
    }

    private void UpdatePopupBounds()
    {
        if (!IsDropDownOpen || _popupList == null)
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        var popupBounds = CalculatePopupBounds(window);
        if (_lastPopupBounds is Rect last && popupBounds.Equals(last))
        {
            return;
        }

        window.UpdatePopup(_popupList, popupBounds);
        _lastPopupBounds = popupBounds;
    }

    private Rect CalculatePopupBounds(Window window)
    {
        var bounds = Bounds;
        double width = Math.Max(0, bounds.Width);
        if (width <= 0)
        {
            width = 120;
        }

        var client = window.ClientSizeDip;
        double x = bounds.X;

        // Clamp horizontally to client area.
        if (x + width > client.Width)
        {
            x = Math.Max(0, client.Width - width);
        }

        if (x < 0)
        {
            x = 0;
        }

        // Do not measure the popup ListBox with infinite height; it can reset its scroll state.
        double itemHeight = ResolveItemHeight();
        double chrome = _popupList!.Padding.VerticalThickness + (_popupList.BorderThickness * 2);
        double desiredHeight = ItemsSource.Count * itemHeight + chrome;
        double maxHeight = Math.Max(0, MaxDropDownHeight);
        double desiredClamped = Math.Min(desiredHeight, maxHeight);

        double belowY = bounds.Bottom;
        double availableBelow = Math.Max(0, client.Height - belowY);
        double availableAbove = Math.Max(0, bounds.Y);

        bool preferBelow = availableBelow >= availableAbove;

        double height;
        double y;

        if (preferBelow)
        {
            if (availableBelow > 0 || availableAbove <= 0)
            {
                y = belowY;
                height = Math.Min(desiredClamped, availableBelow);
            }
            else
            {
                height = Math.Min(desiredClamped, availableAbove);
                y = bounds.Y - height;
            }
        }
        else
        {
            if (availableAbove > 0 || availableBelow <= 0)
            {
                height = Math.Min(desiredClamped, availableAbove);
                y = bounds.Y - height;
            }
            else
            {
                y = belowY;
                height = Math.Min(desiredClamped, availableBelow);
            }
        }

        return new Rect(x, y, width, height);
    }

    protected override void OnDispose()
    {
        if (_popupList != null)
        {
            ClosePopup();
            _popupList.SelectionChanged -= OnPopupListSelectionChanged;
            _popupList.ItemActivated -= OnPopupListItemActivated;
            _popupList.Dispose();
            _popupList = null;
        }
        _itemsSource.Changed -= OnItemsChanged;
        _itemsSource.SelectionChanged -= OnItemsSelectionChanged;
    }

    void IPopupOwner.OnPopupClosed(UIElement popup)
    {
        if (_popupList != null && popup == _popupList)
        {
            _isDropDownOpen = false;
            InvalidateVisual();
            _lastPopupBounds = null;

            var root = FindVisualRoot();
            if (root is Window window && window.FocusManager.FocusedElement == popup)
            {
                if (_restoreFocusAfterPopupClose)
                {
                    window.FocusManager.SetFocus(this);
                }
                else
                {
                    window.FocusManager.ClearFocus();
                }
            }

            _restoreFocusAfterPopupClose = false;
        }
    }
}
