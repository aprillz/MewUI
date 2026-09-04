using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A drop-down selection control with text header and popup list.
/// </summary>
public sealed partial class ComboBox : DropDownBase, ISelector, IIndexedSelector, IVisualTreeHost
{
    static ComboBox() { }

    private static readonly bool _popupStyleRegistered =
        FrameworkNamedStyles.Register("combobox-popup", BuiltInStyles.CreateComboBoxPopupStyle);

    public static readonly MewProperty<int> SelectedIndexProperty =
        MewProperty<int>.Register<ComboBox>(nameof(SelectedIndex), -1,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedIndexPropertyChanged(newVal));

    public static readonly MewProperty<object?> SelectedItemProperty =
        MewProperty<object?>.Register<ComboBox>(nameof(SelectedItem), null,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedItemPropertyChanged(newVal));

    public static readonly MewProperty<bool> ZebraStripingProperty =
        MewProperty<bool>.Register<ComboBox>(nameof(ZebraStriping), true, MewPropertyOptions.None,
            static (self, oldValue, newValue) => self.OnZebraStripingChanged(oldValue, newValue));

    public static readonly MewProperty<string> PlaceholderProperty =
        MewProperty<string>.Register<ComboBox>(nameof(Placeholder), string.Empty, MewPropertyOptions.AffectsRender);

    private ListBox? _popupList;
    private readonly SelectionSync _selection;
    private bool _suppressItemsSelectionChanged;
    private ISelectableItemsView _itemsSource = ItemsView.EmptySelectable;
    private WheelNotchAccumulator _wheelAccumulator;
    private IDataTemplate? _itemTemplate;
    private IDataTemplate? _selectedItemTemplate;

    // The header view is built once from the effective template and rebound as the selection moves;
    // _headerTemplate remembers which template built it so a template change rebuilds it.
    private IDataTemplate? _headerTemplate;
    private FrameworkElement? _headerView;
    private TemplateContext? _headerContext;

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
            _selection.SyncFromModel();

            // Same index in a new source is a different item, so the header view rebinds regardless.
            BindHeaderView();
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
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

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

            SyncHeaderView();
        }
    }

    /// <summary>
    /// Gets or sets the template that presents the selected item in the header. Null falls back to
    /// <see cref="ItemTemplate"/>; with neither set the header draws the item text.
    /// </summary>
    /// <remarks>
    /// The header keeps its own size: the built view is clipped to the text area and never grows the
    /// control, so a template larger than the header needs Width / MinWidth / Height / MinHeight on the
    /// ComboBox. The view is not hit-testable; clicks reach the ComboBox and open the drop-down.
    /// </remarks>
    public IDataTemplate? SelectedItemTemplate
    {
        get => _selectedItemTemplate;
        set
        {
            if (ReferenceEquals(_selectedItemTemplate, value))
            {
                return;
            }

            _selectedItemTemplate = value;
            SyncHeaderView();
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
        _selection = new SelectionSync(() => _itemsSource,
            value => SetCurrentValue(SelectedIndexProperty, value),
            value => SetCurrentValue(SelectedItemProperty, value),
            null,
            value => CommitTargetValue(SelectedIndexProperty, value),
            value => CommitTargetValue(SelectedItemProperty, value));

        _itemsSource.SelectionChanged += OnItemsSelectionChanged;
        _itemsSource.Changed += OnItemsChanged;
    }

    private void OnZebraStripingChanged(bool oldValue, bool newValue)
    {
        if (_popupList != null)
            _popupList.ZebraStriping = newValue;
    }

    private void OnItemsChanged(ItemsChange change)
    {
        if (_popupList != null)
        {
            SyncPopupContent(_popupList);
        }

        BindHeaderView();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnSelectedIndexPropertyChanged(int newIndex) => _selection.PushIndex(newIndex);

    private void OnSelectedItemPropertyChanged(object? item) => _selection.PushItem(item);

    private void OnItemsSelectionChanged(int index)
    {
        if (_suppressItemsSelectionChanged)
        {
            return;
        }

        _selection.SyncFromModel();
        SelectionChanged?.Invoke(_itemsSource.SelectedItem);
        BindHeaderView();
        InvalidateVisual();

        if (_popupList != null)
        {
            _popupList.SelectedIndex = index;
        }
    }

    private bool HasSelectedItem => SelectedIndex >= 0 && SelectedIndex < ItemsSource.Count;

    private IDataTemplate? EffectiveHeaderTemplate => _selectedItemTemplate ?? _itemTemplate;

    /// <summary>Builds, replaces or drops the header view to match the effective template, then rebinds it.</summary>
    private void SyncHeaderView()
    {
        // A control template owns the whole visual tree, so the header view only exists without one.
        var template = HasTemplateInstance ? null : EffectiveHeaderTemplate;
        if (!ReferenceEquals(_headerTemplate, template))
        {
            ReleaseHeaderView();
            if (template != null)
            {
                var context = new TemplateContext();
                var view = template.Build(context);
                // The view only presents the selection; input has to reach the ComboBox so a click opens the drop-down.
                view.IsHitTestVisible = false;
                view.Parent = this;
                _headerContext = context;
                _headerView = view;
                _headerTemplate = template;
            }
        }

        BindHeaderView();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Binds the header view to the current selection; with none it is unbound but kept for the next one.</summary>
    private void BindHeaderView()
    {
        if (_headerView == null || _headerContext == null || _headerTemplate == null)
        {
            return;
        }

        if (HasSelectedItem)
        {
            int index = SelectedIndex;
            _headerContext.BindTemplate(_headerView, _headerTemplate, ItemsSource.GetItem(index), index);
        }
        else
        {
            _headerContext.UnbindTemplate(_headerView);
        }
    }

    private void ReleaseHeaderView()
    {
        if (_headerView == null)
        {
            return;
        }

        _headerContext?.UnbindTemplate(_headerView);
        _headerView.Parent = null;
        _headerView = null;
        _headerContext = null;
        _headerTemplate = null;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        SyncHeaderView();
    }

    private protected override void OnTemplateInstanceDetached()
    {
        base.OnTemplateInstanceDetached();
        SyncHeaderView();
    }

    // Control's VisitChildren is an explicit implementation, so the header view is hosted by
    // re-implementing the interface and keeping the template-instance branch Control would have taken.
    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        if (HasTemplateInstance)
        {
            return TemplateVisualRoot == null || visitor(TemplateVisualRoot);
        }

        return _headerView == null || visitor(_headerView);
    }

    /// <summary>The area the selected item occupies: the header minus border, padding and the arrow column.</summary>
    private Rect GetHeaderContentRect(Rect bounds)
    {
        var snapped = GetSnappedBorderBounds(bounds);
        var borderInset = GetBorderVisualInset();
        var header = new Rect(snapped.X, snapped.Y, snapped.Width, ResolveAnchorHeight());
        var inner = header.Deflate(new Thickness(borderInset));
        return new Rect(inner.X, inner.Y, Math.Max(0, inner.Width - ArrowAreaWidth), inner.Height).Deflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        if (_headerView != null && !HasTemplateInstance)
        {
            _headerView.Arrange(GetHeaderContentRect(bounds));
        }
    }

    protected override Size MeasureHeader(Size availableSize)
    {
        var headerHeight = ResolveHeaderHeight();

        if (_headerView != null && HasSelectedItem)
        {
            // Measured for its own arrange only: the header size follows the item texts and the header
            // height, never the view, so changing the selection cannot resize the control.
            var borderInset = GetBorderVisualInset();
            _headerView.Measure(new Size(
                Math.Max(0, availableSize.Width - ArrowAreaWidth),
                Math.Max(0, headerHeight - borderInset * 2 - Padding.VerticalThickness)));
        }

        double width = 80;
        var dpi = GetDpi();
        var factory = GetGraphicsFactory();
        var style = GetTextRunStyle();

        double maxWidth = 0;
        int count = ItemsSource.Count;

        for (int i = 0; i < count; i++)
        {
            var item = ItemsSource.GetText(i);
            if (string.IsNullOrEmpty(item))
            {
                continue;
            }

            maxWidth = Math.Max(maxWidth, TextLayoutOperations.Measure(factory, item, dpi, in style).Width);
        }

        if (!string.IsNullOrEmpty(Placeholder))
        {
            maxWidth = Math.Max(maxWidth, TextLayoutOperations.Measure(factory, Placeholder, dpi, in style).Width);
        }

        width = maxWidth + ArrowAreaWidth;
        return new Size(width, headerHeight);
    }

    protected override void RenderHeaderContent(IGraphicsContext context, Rect headerRect, Rect innerHeaderRect)
    {
        var textRect = new Rect(innerHeaderRect.X, innerHeaderRect.Y, innerHeaderRect.Width - ArrowAreaWidth, innerHeaderRect.Height)
            .Deflate(Padding);

        if (_headerView != null && HasSelectedItem)
        {
            // Rendered here rather than through RenderSubtree so it sits between the chrome and the arrow.
            context.Save();
            try
            {
                context.IntersectClip(textRect);
                _headerView.Render(context);
            }
            finally
            {
                context.Restore();
            }
        }
        else
        {
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
                var style = GetTextRunStyle();
                var layout = TextLayoutOperations.GetOrCreate(
                    GetGraphicsFactory(), text, GetDpi(), in style, textRect.Width, textRect.Height);
                TextLayoutOperations.DrawInBounds(
                    context, layout, textRect, textColor, TextAlignment.Center, this);
            }
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

        var region = window.GetPopupPlacementRegion(bounds);
        double x = PopupPlacement.ClampHorizontal(bounds.X, width, region, floorToLeftEdge: true);

        // Do not measure the popup ListBox with infinite height; it can reset its scroll state.
        double itemHeight = ResolveItemHeight();
        double chrome = _popupList!.Padding.VerticalThickness + (_popupList.BorderThickness * 2);
        double desiredHeight = ItemsSource.Count * itemHeight + chrome;
        double maxHeight = Math.Max(0, MaxDropDownHeight);
        double desiredClamped = Math.Min(desiredHeight, maxHeight);

        // Open downward when the dropdown fits below the box (standard combo behavior), only flipping
        // up when it does not fit below. Preferring the side with more raw space would open upward for a
        // box low in the window, because the native work-area region extends far above it.
        double belowY = bounds.Bottom;
        var (y, height) = PopupPlacement.ResolveVerticalPreferBelowIfFits(bounds.Y, belowY, region, desiredClamped);

        return new Rect(x, y, width, height);
    }

    protected override UIElement CreatePopupContent()
    {
        _popupList = new ListBox();
        _popupList.StyleName = BuiltInStyles.ComboBoxPopup;
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

        CommitTargetValue(SelectedIndexProperty, _popupList.SelectedIndex);
    }

    private void OnPopupListItemActivated(int index)
    {
        CommitTargetValue(SelectedIndexProperty, index);
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
                    CommitTargetValue(
                        SelectedIndexProperty,
                        Math.Min(count - 1, SelectedIndex < 0 ? 0 : SelectedIndex + 1));
                }
                else
                {
                    CommitTargetValue(
                        SelectedIndexProperty,
                        Math.Max(0, SelectedIndex <= 0 ? 0 : SelectedIndex - 1));
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

        // Accumulate so trackpad sub-notch swipes don't advance an item per micro-event.
        int notches = _wheelAccumulator.TakeY(e.Delta.Y);
        if (notches == 0)
        {
            e.Handled = true;
            return;
        }

        // Wheel up (+notch) selects the previous item; wheel down advances.
        int next = Math.Clamp(SelectedIndex - notches, 0, count - 1);
        if (next != SelectedIndex)
        {
            CommitTargetValue(SelectedIndexProperty, next);
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
        ReleaseHeaderView();

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
