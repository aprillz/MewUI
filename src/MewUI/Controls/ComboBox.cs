using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class ComboBox : Control, IPopupOwner
{
    private readonly List<string> _items = new();
    private bool _isDropDownOpen;
    private ListBox? _popupList;
    private bool _restoreFocusAfterPopupClose;
    private ValueBinding<int>? _selectedIndexBinding;
    private bool _updatingFromSource;

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

    protected override Color DefaultBackground => Theme.Current.Palette.ControlBackground;

    protected override Color DefaultBorderBrush => Theme.Current.Palette.ControlBorder;

    public ComboBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(8, 4, 8, 4);
        // Do not set explicit Height, otherwise FrameworkElement.MeasureOverride will clamp DesiredSize
        // and the drop-down cannot expand. Use MinHeight as the default header height.
        Height = double.NaN;
        MinHeight = Theme.Current.BaseControlHeight;
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        if (MinHeight == oldTheme.BaseControlHeight)
        {
            MinHeight = newTheme.BaseControlHeight;
        }

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

        using (var measure = BeginTextMeasurement())
        {
            double maxWidth = 0;
            foreach (var item in _items)
            {
                if (string.IsNullOrEmpty(item))
                {
                    continue;
                }

                maxWidth = Math.Max(maxWidth, measure.Context.MeasureText(item, measure.Font).Width);
            }

            if (!string.IsNullOrEmpty(Placeholder))
            {
                maxWidth = Math.Max(maxWidth, measure.Context.MeasureText(Placeholder, measure.Font).Width);
            }

            width = maxWidth + Padding.HorizontalThickness + ArrowAreaWidth;
        }

        return new Size(width, headerHeight);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        double radius = theme.ControlCornerRadius;

        var bg = IsEnabled ? Background : theme.Palette.DisabledControlBackground;

        var borderColor = BorderBrush;
        if (IsEnabled)
        {
            if (IsFocused)
            {
                borderColor = theme.Palette.Accent;
            }
            else if (IsMouseOver)
            {
                borderColor = BorderBrush.Lerp(theme.Palette.Accent, 0.6);
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
        var textColor = IsEnabled ? Foreground : theme.Palette.DisabledText;
        if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(Placeholder) && !IsFocused)
        {
            text = Placeholder;
            textColor = theme.Palette.PlaceholderText;
        }

        if (!string.IsNullOrEmpty(text))
        {
            context.DrawText(text, textRect, GetFont(), textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }

        // Arrow
        DrawArrow(context, headerRect, IsEnabled ? textColor : theme.Palette.DisabledText, IsDropDownOpen);

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

        return Math.Max(18, FontSize + 6);
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

        double size = 4;
        var p1 = isUp
            ? new Point(centerX - size, centerY + size / 2)
            : new Point(centerX - size, centerY - size / 2);
        var p2 = new Point(centerX, isUp ? centerY - size / 2 : centerY + size / 2);
        var p3 = isUp
            ? new Point(centerX + size, centerY + size / 2)
            : new Point(centerX + size, centerY - size / 2);

        context.DrawLine(p1, p2, color, 1);
        context.DrawLine(p2, p3, color, 1);
    }

    private const double ArrowAreaWidth = 22;

    private void ShowPopup()
    {
        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        if (_popupList == null)
        {
            _popupList = new ListBox();
            _popupList.Items.Clear();
            foreach (var item in _items)
            {
                _popupList.AddItem(item);
            }

            _popupList.SelectedIndex = SelectedIndex;
        }
        else
        {
            // Sync items/selection.
            _popupList.Items.Clear();
            foreach (var item in _items)
            {
                _popupList.AddItem(item);
            }

            _popupList.SelectedIndex = SelectedIndex;
        }

        _popupList.SelectionChanged -= OnPopupListSelectionChanged;
        _popupList.SelectionChanged += OnPopupListSelectionChanged;

        var popupBounds = CalculatePopupBounds(window);
        window.ShowPopup(this, _popupList, popupBounds);
        window.FocusManager.SetFocus(_popupList);
    }

    private void OnPopupListSelectionChanged(int index)
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
        window.UpdatePopup(_popupList, popupBounds);
    }

    private Rect CalculatePopupBounds(Window window)
    {
        var bounds = Bounds;
        double width = Math.Max(0, bounds.Width);
        if (width <= 0)
        {
            width = 120;
        }

        _popupList!.Measure(new Size(width, double.PositiveInfinity));
        double desiredHeight = _popupList.DesiredSize.Height;
        double height = Math.Min(desiredHeight, Math.Max(0, MaxDropDownHeight));

        double x = bounds.X;
        double belowY = bounds.Bottom;
        double aboveY = bounds.Y - height;

        var client = window.ClientSizeDip;

        bool fitsBelow = belowY + height <= client.Height;
        bool fitsAbove = aboveY >= 0;

        double y = fitsBelow || !fitsAbove ? belowY : aboveY;

        // Clamp horizontally to client area.
        if (x + width > client.Width)
        {
            x = Math.Max(0, client.Width - width);
        }

        if (x < 0)
        {
            x = 0;
        }

        return new Rect(x, y, width, height);
    }

    protected override void OnDispose()
    {
        if (_popupList != null)
        {
            ClosePopup();
            _popupList.SelectionChanged -= OnPopupListSelectionChanged;
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