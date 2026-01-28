using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class ComboBox : Control, IPopupOwner
{
    private readonly List<string> _items = new();
    private readonly TextWidthCache _textWidthCache = new(512);
    private bool _isDropDownOpen;
    private ListBox? _popupList;
    private bool _restoreFocusAfterPopupClose;
    private ValueBinding<int>? _selectedIndexBinding;
    private bool _updatingFromSource;
    private Rect? _lastPopupBounds;

    public IList<string> Items => _items;

    public int SelectedIndex
    {
        get;
        set
        {
            int clamped = value;
            if (_items.Count == 0)
            {
                clamped = -1;
            }
            else
            {
                clamped = Math.Clamp(value, -1, _items.Count - 1);
            }

            if (field == clamped)
            {
                return;
            }

            field = clamped;
            SelectionChanged?.Invoke(field);
            InvalidateVisual();
        }
    } = -1;

    public string? SelectedItem => SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex] : null;

    public string Placeholder
    {
        get;
        set { field = value ?? string.Empty; InvalidateVisual(); }
    } = string.Empty;

    public double ItemHeight
    {
        get;
        set { field = value; InvalidateMeasure(); }
    } = double.NaN;

    public double MaxDropDownHeight
    {
        get;
        set { field = value; InvalidateMeasure(); }
    } = 160;

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

    public event Action<int>? SelectionChanged;

    public override bool Focusable => true;

    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    public ComboBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(8, 4, 8, 4);
        // Do not set explicit Height, otherwise FrameworkElement.MeasureOverride will clamp DesiredSize
        // and the drop-down cannot expand. Use MinHeight as the default header height.
        Height = double.NaN;
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // The popup ListBox can exist while the dropdown is closed, so it won't be in the Window visual tree
        // and would miss theme broadcasts. Keep it in sync here.
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
            _textWidthCache.SetCapacity(Math.Clamp(_items.Count + 8, 64, 4096));

            foreach (var item in _items)
            {
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

        string text = SelectedItem ?? string.Empty;
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

            if (_items.Count > 0)
            {
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex < 0 ? 0 : SelectedIndex + 1);
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

            if (_items.Count > 0)
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

    public void AddItem(string item)
    {
        _items.Add(item ?? string.Empty);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void ClearItems()
    {
        _items.Clear();
        SelectedIndex = -1;
        IsDropDownOpen = false;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetSelectedIndexBinding(
        Func<int> get,
        Action<int> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        _selectedIndexBinding?.Dispose();
        _selectedIndexBinding = new ValueBinding<int>(
            get,
            set,
            subscribe,
            unsubscribe,
            () =>
            {
                _updatingFromSource = true;
                try { SelectedIndex = get(); }
                finally { _updatingFromSource = false; }
            });

        // Ensure the binding forwarder is registered once (no duplicates), without tracking extra state.
        SelectionChanged -= ForwardSelectionChangedToBinding;
        SelectionChanged += ForwardSelectionChangedToBinding;

        _updatingFromSource = true;
        try { SelectedIndex = get(); }
        finally { _updatingFromSource = false; }
    }

    private void ForwardSelectionChangedToBinding(int index)
    {
        if (_updatingFromSource)
        {
            return;
        }

        _selectedIndexBinding?.Set(index);
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
        list.Items.Clear();
        foreach (var item in _items)
        {
            list.AddItem(item);
        }

        list.SelectedIndex = SelectedIndex;
        list.ItemHeight = ResolveItemHeight();
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

    private void OnPopupListSelectionChanged(int index)
    {
        SelectedIndex = index;
    }

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
        double desiredHeight = _items.Count * itemHeight + chrome;
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

        SelectionChanged -= ForwardSelectionChangedToBinding;
        _selectedIndexBinding?.Dispose();
        _selectedIndexBinding = null;
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
