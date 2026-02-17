using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A drop-down selection control with text header and popup list.
/// </summary>
public sealed partial class ComboBox : DropDownBase
{
    private readonly TextWidthCache _textWidthCache = new(512);
    private ListBox? _popupList;
    private bool _updatingFromSource;
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
                SyncPopupContent(_popupList);
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
    public new double MaxDropDownHeight
    {
        get => base.MaxDropDownHeight;
        set
        {
            // Keep Measure invalidation behavior from the previous implementation.
            if (base.MaxDropDownHeight.Equals(value))
            {
                return;
            }

            base.MaxDropDownHeight = value;
            InvalidateMeasure();
        }
    }

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
                SyncPopupContent(_popupList);
            }
        }
    }

    /// <summary>
    /// Occurs when the selected item changes.
    /// </summary>
    public event Action<object?>? SelectionChanged;

    /// <summary>
    /// Gets the default background color.
    /// </summary>
    protected override Color DefaultBackground => Theme.Palette.ButtonFace;

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
            SyncPopupContent(_popupList);
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

    protected override Size MeasureHeader(Size availableSize)
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

    protected override void RenderHeaderContent(IGraphicsContext context, Rect headerRect, Rect innerHeaderRect)
    {
        // Text
        var textRect = new Rect(innerHeaderRect.X, innerHeaderRect.Y, innerHeaderRect.Width - ArrowAreaWidth, innerHeaderRect.Height)
            .Deflate(Padding);

        if (SelectedText == "Beta" || IsDropDownOpen) { }

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
    }

    protected override Rect CalculatePopupBounds(Window window, UIElement popup)
    {
        if (_popupList == null)
        {
            return base.CalculatePopupBounds(window, popup);
        }

        var bounds = Bounds;
        double width = Math.Max(0, bounds.Width);
        if (width <= 0)
        {
            width = 120;
        }

        var client = window.ClientSize;
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

    protected override UIElement CreatePopupContent()
    {
        _popupList = new ListBox();
        _popupList.SelectionChanged += OnPopupListSelectionChanged;
        _popupList.ItemActivated += OnPopupListItemActivated;
        return _popupList;
    }

    private void OnPopupListSelectionChanged(object? _)
    {
        if (_popupList == null)
        {
            return;
        }

        SelectedIndex = _popupList.SelectedIndex;
    }

    private void OnPopupListItemActivated(int index)
    {
        SelectedIndex = index;
        RequestRestoreFocusAfterPopupClose();
        IsDropDownOpen = false;
    }

    protected override void SyncPopupContent(UIElement popup)
    {
        if (popup is not ListBox list)
        {
            return;
        }

        if (!ReferenceEquals(list.ItemsSource, ItemsSource))
        {
            list.ApplyItemsSource(ItemsSource, preserveListBoxSelection: false);
        }

        list.ItemHeight = ResolveItemHeight();

        // Ensure popup reflects the current ComboBox selection.
        list.SelectedIndex = SelectedIndex;

        if (ItemTemplate != null)
        {
            list.ItemTemplate = ItemTemplate;
        }
    }

    protected override UIElement GetPopupFocusTarget(UIElement popup) => popup;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsEnabled)
        {
            base.OnKeyDown(e);
            return;
        }

        // ComboBox special-case: Up/Down opens dropdown and moves selection.
        if (e.Key == Key.Down || e.Key == Key.Up)
        {
            if (!IsDropDownOpen)
            {
                IsDropDownOpen = true;
            }

            int count = ItemsSource.Count;
            if (count > 0)
            {
                if (e.Key == Key.Down)
                {
                    SelectedIndex = Math.Min(count - 1, SelectedIndex < 0 ? 0 : SelectedIndex + 1);
                }
                else
                {
                    SelectedIndex = Math.Max(0, SelectedIndex <= 0 ? 0 : SelectedIndex - 1);
                }
            }

            if (_popupList != null)
            {
                _popupList.SelectedIndex = SelectedIndex;
            }

            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
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

    protected override void OnDispose()
    {
        if (_popupList != null)
        {
            _popupList.SelectionChanged -= OnPopupListSelectionChanged;
            _popupList.ItemActivated -= OnPopupListItemActivated;
            _popupList.Dispose();
            _popupList = null;
        }
        _itemsSource.Changed -= OnItemsChanged;
        _itemsSource.SelectionChanged -= OnItemsSelectionChanged;

        base.OnDispose();
    }
}
