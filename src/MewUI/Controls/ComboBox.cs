using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A drop-down selection control with text header and popup list.
/// </summary>
public sealed partial class ComboBox : DropDownBase
{
    public static readonly MewProperty<int> SelectedIndexProperty =
        MewProperty<int>.Register<ComboBox>(nameof(SelectedIndex), -1,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedIndexPropertyChanged(newVal));

    public static readonly MewProperty<bool> ZebraStripingProperty =
        MewProperty<bool>.Register<ComboBox>(nameof(ZebraStriping), true, MewPropertyOptions.None,
            static (self, oldValue, newValue) => self.OnZebraStripingChanged(oldValue, newValue));

    public static readonly MewProperty<string> PlaceholderProperty =
        MewProperty<string>.Register<ComboBox>(nameof(Placeholder), string.Empty, MewPropertyOptions.AffectsRender);

    private readonly TextWidthCache _textWidthCache = new(512);
    private ListBox? _popupList;

    private void OnZebraStripingChanged(bool oldValue, bool newValue)
    {
        if (_popupList != null)
            _popupList.ZebraStriping = newValue;
    }
    private bool _syncingSelectedIndex;
    private bool _suppressItemsSelectionChanged;
    private ISelectableItemsView _itemsSource = ItemsView.EmptySelectable;
    private IDataTemplate? _itemTemplate;

    public bool ZebraStriping
    {
        get => GetValue(ZebraStripingProperty);
        set => SetValue(ZebraStripingProperty, value);
    }

    /// <summary>
    /// Gets or sets the items data source.
    /// </summary>
    public ISelectableItemsView ItemsSource
    {
        get => _itemsSource;
        set
        {
            value ??= ItemsView.EmptySelectable;
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
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// Gets the currently selected item object.
    /// </summary>
    public object? SelectedItem => ItemsSource.SelectedItem;

    /// <summary>
    /// Gets the currently selected item text.
    /// </summary>
    public string? SelectedText => SelectedIndex >= 0 && SelectedIndex < ItemsSource.Count ? ItemsSource.GetText(SelectedIndex) : null;

    public static readonly MewProperty<bool> ChangeOnWheelProperty =
        MewProperty<bool>.Register<ComboBox>(nameof(ChangeOnWheel), true, MewPropertyOptions.None);

    public bool ChangeOnWheel
    {
        get => GetValue(ChangeOnWheelProperty);
        set => SetValue(ChangeOnWheelProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text shown when no item is selected.
    /// </summary>
    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the height of items in the dropdown list.
    /// </summary>
    public static readonly MewProperty<double> ItemHeightProperty =
        MewProperty<double>.Register<ComboBox>(nameof(ItemHeight), double.NaN, MewPropertyOptions.AffectsLayout);

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
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
    /// Initializes a new instance of the ComboBox class.
    /// </summary>
    public ComboBox()
    {
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

    private void OnSelectedIndexPropertyChanged(int newIndex)
    {
        if (_syncingSelectedIndex) return;
        _syncingSelectedIndex = true;
        try
        {
            _itemsSource.SelectedIndex = newIndex;
            int actual = _itemsSource.SelectedIndex;
            if (actual != newIndex)
            {
                SetValue(SelectedIndexProperty, actual);
            }
        }
        finally { _syncingSelectedIndex = false; }
    }

    private void OnItemsSelectionChanged(int index)
    {
        if (_suppressItemsSelectionChanged)
        {
            return;
        }

        if (!_syncingSelectedIndex)
        {
            _syncingSelectedIndex = true;
            try { SetValue(SelectedIndexProperty, index); }
            finally { _syncingSelectedIndex = false; }
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

        string text = SelectedText ?? string.Empty;
        var state = CurrentVisualState;
        var textColor = state.IsEnabled ? Foreground : Theme.Palette.DisabledText;
        if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(Placeholder) && !state.IsFocused)
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
        _popupList.ZebraStriping = ZebraStriping;
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
        list.ZebraStriping = ZebraStriping;

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
        if (!IsEffectivelyEnabled)
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

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEffectivelyEnabled || !ChangeOnWheel /*|| IsDropDownOpen*/)
        {
            return;
        }

        int count = ItemsSource.Count;
        if (count == 0)
        {
            return;
        }

        int next = Math.Clamp(SelectedIndex + (e.Delta > 0 ? -1 : 1), 0, count - 1);
        if (next != SelectedIndex)
        {
            SelectedIndex = next;
        }

        e.Handled = true;
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
