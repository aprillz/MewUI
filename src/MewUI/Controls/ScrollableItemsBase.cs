namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for scrollable items controls: hosts a <see cref="ScrollViewer"/> over an
/// <see cref="IItemsPresenter"/> and provides item-binding generation, deferred scroll-into-view,
/// a pending tab-focus helper, and shared list chrome. Virtualization is a presenter concern, not this base.
/// </summary>
public abstract partial class ScrollableItemsBase : Control, ISubtreeInvalidationHost
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ScrollableItemsBase>(DefaultStyles.CreateScrollableItemsBaseStyle);

    private static readonly MewPropertyKey<double> VerticalOffsetPropertyKey =
        MewProperty<double>.RegisterReadOnly<ScrollableItemsBase>(nameof(VerticalOffset), 0);
    public static readonly MewProperty<double> VerticalOffsetProperty = VerticalOffsetPropertyKey.Property;

    private static readonly MewPropertyKey<double> HorizontalOffsetPropertyKey =
        MewProperty<double>.RegisterReadOnly<ScrollableItemsBase>(nameof(HorizontalOffset), 0);
    public static readonly MewProperty<double> HorizontalOffsetProperty = HorizontalOffsetPropertyKey.Property;

    protected readonly ScrollViewer _scrollViewer;

    /// <summary>
    /// Set by derived class constructor (after the presenter is created).
    /// </summary>
    private protected PendingTabFocusHelper _tabFocusHelper = null!;

    private protected uint _itemBindingGeneration;
    private ScrollIntoViewRequest _scrollIntoViewRequest;

    static ScrollableItemsBase()
    {
        FocusableProperty.OverrideDefaultValue<ScrollableItemsBase>(true);
    }

    protected ScrollableItemsBase()
    {
        _scrollViewer = new ScrollViewer
        {
            BorderThickness = 0,
            Background = default,
            VerticalScroll = ScrollMode.Auto,
            HorizontalScroll = ScrollMode.Auto,
        };
        _scrollViewer.Parent = this;
        _scrollViewer.ScrollChanged += OnInnerScrollChanged;
    }

    /// <summary>Raised when the scroll offset, extent, or viewport changes.</summary>
    public event Action? ScrollChanged;

    /// <summary>
    /// Gets the current vertical scroll offset in DIPs. Observable: it settles during layout, so a
    /// value read straight after mutating the items still reports the previous position.
    /// </summary>
    public double VerticalOffset => GetValue(VerticalOffsetProperty);

    /// <summary>
    /// Gets the current horizontal scroll offset in DIPs. Observable: it settles during layout, so a
    /// value read straight after mutating the items still reports the previous position.
    /// </summary>
    public double HorizontalOffset => GetValue(HorizontalOffsetProperty);

    private void OnInnerScrollChanged()
    {
        SetValue(VerticalOffsetPropertyKey, _scrollViewer.VerticalOffset);
        SetValue(HorizontalOffsetPropertyKey, _scrollViewer.HorizontalOffset);
        ScrollChanged?.Invoke();
    }

    protected override void OnDispose()
    {
        _scrollViewer.ScrollChanged -= OnInnerScrollChanged;
        ScrollChanged = null;
        base.OnDispose();
    }

    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();
        InvalidateItemBindings(invalidateMeasure: false);
        InvalidateVisual();
    }

    private protected uint ItemBindingGeneration => _itemBindingGeneration;

    private protected void InvalidateItemBindings(bool invalidateMeasure = true)
    {
        unchecked { _itemBindingGeneration++; }
        if (invalidateMeasure)
        {
            InvalidateMeasure();
        }
        InvalidateVisual();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => VisitScrollChildren(visitor);

    private PrepareContainerHandler<ItemContainer, object?>? _prepareContainer;
    private PrepareContainerHandler<ItemContainer, object?>? _clearContainer;

    /// <summary>
    /// Sets the container-prepare hook and rebuilds the containers, since their shape depends on
    /// whether a hook is registered. Use the typed <c>PrepareContainer</c> extension.
    /// </summary>
    internal void SetPrepareContainer(PrepareContainerHandler<ItemContainer, object?>? hook)
    {
        _prepareContainer = hook;
        ReapplyItemTemplate();
        InvalidateItemBindings();
    }

    /// <summary>
    /// Sets the container-clear hook and rebuilds the containers. Use the typed
    /// <c>ClearContainer</c> extension.
    /// </summary>
    internal void SetClearContainer(PrepareContainerHandler<ItemContainer, object?>? hook)
    {
        _clearContainer = hook;
        ReapplyItemTemplate();
        InvalidateItemBindings();
    }

    /// <summary>
    /// Returns the template to hand the presenter: the caller's template as-is when no hook is
    /// registered, so applications that do not use the hooks pay for no extra element per item.
    /// </summary>
    private protected IDataTemplate WrapItemTemplate(IDataTemplate template)
        => _prepareContainer == null && _clearContainer == null
            ? template
            : new ItemContainerTemplate(template, IsItemSelectedForContainer, _prepareContainer, _clearContainer);

    /// <summary>Whether the item at that index is selected, for <see cref="ItemContainer.IsSelected"/>.</summary>
    private protected virtual bool IsItemSelectedForContainer(int index) => false;

    /// <summary>Re-pushes the effective item template to the presenter. Override where hooks are supported.</summary>
    private protected virtual void ReapplyItemTemplate()
    {
    }

    /// <summary>
    /// Brings every realized container's <see cref="ItemContainer.IsSelected"/> up to date. Call
    /// from a selection change; it costs one pass over the containers on screen.
    /// </summary>
    private protected void RefreshContainerSelection(IItemsPresenter presenter)
    {
        if (_prepareContainer == null && _clearContainer == null)
        {
            return;
        }

        presenter.VisitRealized((index, element) =>
        {
            if (element is ItemContainer container)
            {
                container.SetIsSelected(IsItemSelectedForContainer(index));
            }
        });
    }

    private protected void RequestScrollIntoView(ScrollIntoViewRequest request)
        => _scrollIntoViewRequest = request;

    private protected bool TryConsumeScrollIntoViewRequest(out ScrollIntoViewRequest request)
    {
        if (_scrollIntoViewRequest.IsNone)
        {
            request = default;
            return false;
        }

        request = _scrollIntoViewRequest;
        _scrollIntoViewRequest.Clear();
        return true;
    }

    /// <summary>
    /// Override to visit additional children (e.g. a header row) before or after the scroll viewer.
    /// </summary>
    protected virtual bool VisitScrollChildren(Func<Element, bool> visitor) => visitor(_scrollViewer);

    /// <summary>
    /// Resolves the foreground color for an item, applying the correct priority:
    /// disabled state > selection state > default Foreground.
    /// </summary>
    protected Color ResolveItemForeground(bool selected)
        => !IsEffectivelyEnabled
            ? Theme.Palette.DisabledText
            : selected ? Theme.Palette.SelectionText : Theme.Palette.WindowText;

    /// <summary>
    /// Returns the current viewport height in DIPs, accounting for border insets and padding.
    /// Returns 0 when the control has not been arranged.
    /// </summary>
    protected double GetViewportHeightDip()
    {
        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            var snapped = GetSnappedBorderBounds(Bounds);
            var borderInset = GetBorderVisualInset();
            var innerBounds = snapped.Deflate(new Thickness(borderInset));
            var dpiScale = GetDpi() / 96.0;
            return LayoutRounding.RoundToPixel(Math.Max(0, innerBounds.Height - Padding.VerticalThickness), dpiScale);
        }

        return 0;
    }

    /// <summary>
    /// Returns whether the key event is the Ctrl/Meta+A "select all" shortcut shared by
    /// multi-selection-capable list controls (ListBox/GridView/TreeView).
    /// </summary>
    internal static bool IsSelectAllShortcut(KeyEventArgs e)
        => (e.Modifiers & (ModifierKeys.Control | ModifierKeys.Meta)) != 0 && e.Key == Key.A;

    /// <summary>
    /// Computes the target index for a list navigation key (Up/Down/Home/End, optionally
    /// PageUp/PageDown). Returns false for keys not handled here, leaving each control's own
    /// switch to process its remaining unique keys (Enter/Space/Left/Right/etc.).
    /// Callers resolve <paramref name="currentIndex"/> themselves (raw SelectedIndex vs. a
    /// zero-fallback), since that resolution differs by control and changes the Down/PageDown
    /// result when nothing is currently selected.
    /// </summary>
    internal static bool TryGetListNavigationTarget(Key key, int currentIndex, int count, int pageStep, bool supportsPaging, out int target)
    {
        switch (key)
        {
            case Key.Up:
                target = Math.Max(0, currentIndex - 1);
                return true;
            case Key.Down:
                target = Math.Min(count - 1, currentIndex + 1);
                return true;
            case Key.Home:
                target = 0;
                return true;
            case Key.End:
                target = count - 1;
                return true;
            case Key.PageUp when supportsPaging:
                target = Math.Max(0, currentIndex - pageStep);
                return true;
            case Key.PageDown when supportsPaging:
                target = Math.Min(count - 1, currentIndex + pageStep);
                return true;
            default:
                target = currentIndex;
                return false;
        }
    }

    /// <summary>
    /// Finds the realized index and container whose subtree contains <paramref name="focusedElement"/>.
    /// Shared by TryMoveFocusFromDescendant/OnDescendantFocused across the list controls.
    /// Exposed as internal static (not protected) because TreeView does not derive from this base
    /// but shares this exact routine.
    /// </summary>
    internal static bool TryFindRealizedIndex(IItemsPresenter presenter, UIElement focusedElement, out int index, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FrameworkElement? container)
    {
        int foundIndex = -1;
        FrameworkElement? foundContainer = null;
        presenter.VisitRealized((i, element) =>
        {
            if (foundIndex != -1)
            {
                return;
            }

            if (VisualTree.IsInSubtreeOf(focusedElement, element))
            {
                foundIndex = i;
                foundContainer = element;
            }
        });

        index = foundIndex;
        container = foundContainer;
        return foundIndex >= 0 && foundContainer != null;
    }

    /// <summary>
    /// Maps a point in this control's coordinate space to an item index via <paramref name="presenter"/>:
    /// converts to presenter-local coordinates, then to content-space Y (accounting for vertical scroll
    /// offset), so hit-testing agrees with the rendered rows at fractional DPI. Does not check whether
    /// the point instead lands on a scrollbar; callers that need that guard check it separately.
    /// </summary>
    private protected bool TryMapPointToItemIndex(Point position, IItemsPresenter presenter, out int index)
    {
        index = -1;

        if (presenter is not Element presenterElement)
        {
            return false;
        }

        var dpiScale = GetDpi() / 96.0;
        var local = TranslatePoint(position, presenterElement);
        var presenterRect = new Rect(0, 0, presenterElement.RenderSize.Width, presenterElement.RenderSize.Height);
        if (!presenterRect.Contains(local))
        {
            return false;
        }

        double alignedLocalY = LayoutRounding.RoundToPixel(local.Y, dpiScale);
        double alignedOffsetY = LayoutRounding.RoundToPixel(_scrollViewer.VerticalOffset, dpiScale);
        double yContent = alignedLocalY + alignedOffsetY;
        double xContent = local.X;
        return presenter.TryGetItemIndexAt(xContent, yContent, out index);
    }

    /// <summary>
    /// The reverse of <see cref="TryMapPointToItemIndex"/>: takes the item's range in content space
    /// and returns it where the item is drawn. The rectangle can fall outside the viewport, since
    /// where an item off screen would sit is what a caller scrolling towards it needs.
    /// </summary>
    private protected bool TryMapItemIndexToBounds(int index, IItemsPresenter presenter, out Rect bounds)
    {
        bounds = default;

        if (presenter is not UIElement presenterElement ||
            !presenter.TryGetItemYRange(index, out double top, out double bottom))
        {
            return false;
        }

        var dpiScale = GetDpi() / 96.0;
        double alignedOffsetY = LayoutRounding.RoundToPixel(_scrollViewer.VerticalOffset, dpiScale);
        var presenterBounds = presenterElement.Bounds;
        bounds = new Rect(
            presenterBounds.X,
            presenterBounds.Y + top - alignedOffsetY,
            presenterBounds.Width,
            Math.Max(0, bottom - top));
        return bounds.Height > 0;
    }
}
