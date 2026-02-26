using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Realizes and recycles item containers for a scrollable items host to reduce UI element allocations.
/// </summary>
/// <remarks>
/// This presenter is intended for fixed-height item layouts where only a contiguous visible range is rendered.
/// </remarks>
internal sealed class VirtualizedItemsPresenter
{
    private readonly FrameworkElement _owner;
    private Func<FrameworkElement> _createContainer;
    private Action<FrameworkElement, int> _bind;
    private Action<FrameworkElement>? _unbind;

    private readonly Dictionary<int, FrameworkElement> _realized = new();
    private readonly Stack<FrameworkElement> _pool = new();
    private readonly Dictionary<int, FrameworkElement> _recycledByIndex = new();
    private UIElement? _deferredFocusedElement;
    private UIElement? _deferredFocusOwner;
    private int? _deferredFocusedIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualizedItemsPresenter"/> class.
    /// </summary>
    public VirtualizedItemsPresenter(
        FrameworkElement owner,
        Func<FrameworkElement> createContainer,
        Action<FrameworkElement, int> bind,
        Action<FrameworkElement>? unbind = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _createContainer = createContainer ?? throw new ArgumentNullException(nameof(createContainer));
        _bind = bind ?? throw new ArgumentNullException(nameof(bind));
        _unbind = unbind;
    }

    /// <summary>
    /// Gets the number of currently realized containers.
    /// </summary>
    public int RealizedCount => _realized.Count;

    /// <summary>
    /// Updates the container factory/binding callbacks used for realization.
    /// </summary>
    public void SetTemplate(
        Func<FrameworkElement> createContainer,
        Action<FrameworkElement, int> bind,
        Action<FrameworkElement>? unbind = null,
        bool clearPool = false)
    {
        ArgumentNullException.ThrowIfNull(createContainer);
        ArgumentNullException.ThrowIfNull(bind);

        bool containerChanged = !ReferenceEquals(_createContainer, createContainer);
        if (containerChanged || clearPool)
        {
            RecycleAll();
            _pool.Clear();
        }

        _createContainer = createContainer;
        _bind = bind;
        _unbind = unbind;
    }

    /// <summary>
    /// Recycles all realized containers back into the pool.
    /// </summary>
    public void RecycleAll()
    {
        foreach (var index in _realized.Keys.ToArray())
        {
            Recycle(index);
        }
    }

    /// <summary>
    /// Visits all realized containers (useful for diagnostic traversal).
    /// </summary>
    public void VisitRealized(Action<Element> visitor)
    {
        if (_realized.Count == 0)
        {
            return;
        }

        foreach (var key in _realized.Keys.OrderBy(static k => k))
        {
            visitor(_realized[key]);
        }
    }

    public void VisitRealized(Action<int, FrameworkElement> visitor)
    {
        if (_realized.Count == 0)
        {
            return;
        }

        foreach (var key in _realized.Keys.OrderBy(static k => k))
        {
            visitor(key, _realized[key]);
        }
    }

    /// <summary>
    /// Realizes, arranges, and renders a contiguous range of items.
    /// </summary>
    public void RenderRange(
        IGraphicsContext context,
        Rect contentBounds,
        int first,
        int lastExclusive,
        double itemHeight,
        double yStart,
        Action<IGraphicsContext, int, Rect>? beforeItemRender = null,
        Func<int, Rect, Rect>? getContainerRect = null,
        bool rebindExisting = true)
    {
        if (lastExclusive <= first)
        {
            RecycleAll();
            return;
        }

        ArrangeRange(
            contentBounds,
            first,
            lastExclusive,
            itemHeight,
            yStart,
            getContainerRect,
            rebindExisting);

        double dpiScale = _owner.GetDpiScaleCached();
        int baseYPx = LayoutRounding.RoundToPixelInt(yStart, dpiScale);
        int itemHeightPx = LayoutRounding.RoundToPixelInt(itemHeight, dpiScale);
        double itemHeightDip = itemHeightPx / dpiScale;

        for (int i = first; i < lastExclusive; i++)
        {
            if (!_realized.TryGetValue(i, out var element))
            {
                continue;
            }

            // Use pixel-int math for Y to avoid accumulating floating error at fractional DPI (e.g. 150%).
            int yPx = baseYPx + (i - first) * itemHeightPx;
            double y = yPx / dpiScale;
            var itemRect = new Rect(contentBounds.X, y, contentBounds.Width, itemHeightDip);
            beforeItemRender?.Invoke(context, i, itemRect);
            element.Render(context);
        }
    }

    /// <summary>
    /// Renders a contiguous range of items assuming they have already been realized and arranged.
    /// Does not create, bind, measure, arrange, or recycle containers.
    /// </summary>
    public void RenderArrangedRange(
        IGraphicsContext context,
        Rect contentBounds,
        int first,
        int lastExclusive,
        double itemHeight,
        double yStart,
        Action<IGraphicsContext, int, Rect>? beforeItemRender = null)
    {
        if (lastExclusive <= first)
        {
            return;
        }

        double dpiScale = _owner.GetDpiScaleCached();
        int baseYPx = LayoutRounding.RoundToPixelInt(yStart, dpiScale);
        int itemHeightPx = LayoutRounding.RoundToPixelInt(itemHeight, dpiScale);
        double itemHeightDip = itemHeightPx / dpiScale;

        for (int i = first; i < lastExclusive; i++)
        {
            if (!_realized.TryGetValue(i, out var element))
            {
                continue;
            }

            int yPx = baseYPx + (i - first) * itemHeightPx;
            double y = yPx / dpiScale;
            var itemRect = new Rect(contentBounds.X, y, contentBounds.Width, itemHeightDip);
            beforeItemRender?.Invoke(context, i, itemRect);
            element.Render(context);
        }
    }

    /// <summary>
    /// Realizes and arranges a contiguous range of items without rendering.
    /// </summary>
    public void ArrangeRange(
        Rect contentBounds,
        int first,
        int lastExclusive,
        double itemHeight,
        double yStart,
        Func<int, Rect, Rect>? getContainerRect = null,
        bool rebindExisting = true)
    {
        if (lastExclusive <= first)
        {
            RecycleAll();
            return;
        }

        foreach (var key in _realized.Keys.ToArray())
        {
            if (key < first || key >= lastExclusive)
            {
                Recycle(key);
            }
        }

        double dpiScale = _owner.GetDpiScaleCached();
        int baseYPx = LayoutRounding.RoundToPixelInt(yStart, dpiScale);
        int itemHeightPx = LayoutRounding.RoundToPixelInt(itemHeight, dpiScale);
        double itemHeightDip = itemHeightPx / dpiScale;

        for (int i = first; i < lastExclusive; i++)
        {
            int yPx = baseYPx + (i - first) * itemHeightPx;
            double y = yPx / dpiScale;
            var itemRect = new Rect(contentBounds.X, y, contentBounds.Width, itemHeightDip);

            var containerRect = getContainerRect != null ? getContainerRect(i, itemRect) : itemRect;
            // Keep container geometry stable at fractional DPI (e.g. 150%) and avoid edge-based +1px drift.
            containerRect = LayoutRounding.RoundRectToPixels(containerRect, dpiScale);
            var element = GetOrCreate(i, rebindExisting);
            element.Measure(new Size(Math.Max(0, containerRect.Width), Math.Max(0, containerRect.Height)));
            element.Arrange(containerRect);
        }

        FlushRecycledByIndexToPool();
    }

    private FrameworkElement GetOrCreate(int index, bool rebindExisting)
    {
        if (_realized.TryGetValue(index, out var existing))
        {
            if (rebindExisting)
            {
                _bind(existing, index);
            }
            return existing;
        }

        FrameworkElement element;
        if (_recycledByIndex.Remove(index, out var recycled))
        {
            element = recycled;
        }
        else
        {
            element = _pool.Count > 0 ? _pool.Pop() : _createContainer();
        }

        element.Parent = _owner;
        element.IsVisible = true;

        _bind(element, index);
        _realized[index] = element;

        TryRestoreDeferredFocus(element, index);
        return element;
    }

    private void Recycle(int index)
    {
        if (!_realized.Remove(index, out var element))
        {
            return;
        }

        if (element is UIElement uiElement && _owner.FindVisualRoot() is Window window)
        {
            var focused = window.FocusManager.FocusedElement;
            if (focused != null && VisualTree.IsInSubtreeOf(focused, uiElement))
            {
                _deferredFocusedElement = focused;
                _deferredFocusedIndex = index;

                if (_owner is UIElement ownerUi && ownerUi.Focusable && ownerUi.IsEffectivelyEnabled && ownerUi.IsVisible)
                {
                    _deferredFocusOwner = ownerUi;
                    window.FocusManager.SetFocus(ownerUi);
                }
                else
                {
                    _deferredFocusOwner = null;
                    // When we clear focus due to virtualization, only restore if focus stays null and the same item
                    // index is realized again. This avoids restoring focus onto a recycled container that now
                    // represents a different item.
                    window.FocusManager.ClearFocus();
                }
            }
        }

        _unbind?.Invoke(element);
        element.Parent = null;
        if (!_recycledByIndex.TryAdd(index, element))
        {
            _pool.Push(element);
        }
    }

    private void FlushRecycledByIndexToPool()
    {
        if (_recycledByIndex.Count == 0)
        {
            return;
        }

        foreach (var element in _recycledByIndex.Values)
        {
            _pool.Push(element);
        }

        _recycledByIndex.Clear();
    }

    private void TryRestoreDeferredFocus(FrameworkElement container, int index)
    {
        if (_deferredFocusedIndex != index)
        {
            return;
        }

        var deferred = _deferredFocusedElement;
        if (deferred == null)
        {
            return;
        }

        if (_owner.FindVisualRoot() is not Window window)
        {
            return;
        }

        // Only restore if focus hasn't moved elsewhere since we deferred it.
        if (_deferredFocusOwner != null)
        {
            if (!ReferenceEquals(window.FocusManager.FocusedElement, _deferredFocusOwner))
            {
                return;
            }
        }
        else
        {
            // Focus was cleared when we deferred it; only restore if focus is still null.
            if (window.FocusManager.FocusedElement != null)
            {
                return;
            }
        }

        if (container is not Element root || !VisualTree.IsInSubtreeOf(deferred, root))
        {
            return;
        }

        if (!deferred.Focusable || !deferred.IsEffectivelyEnabled || !deferred.IsVisible)
        {
            _deferredFocusedElement = null;
            _deferredFocusOwner = null;
            return;
        }

        window.FocusManager.SetFocus(deferred);
        _deferredFocusedElement = null;
        _deferredFocusOwner = null;
        _deferredFocusedIndex = null;
    }

}
