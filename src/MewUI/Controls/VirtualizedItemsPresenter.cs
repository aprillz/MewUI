using Aprillz.MewUI.Rendering;
using System.Linq;

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

        for (int i = first; i < lastExclusive; i++)
        {
            if (!_realized.TryGetValue(i, out var element))
            {
                continue;
            }

            double y = yStart + (i - first) * itemHeight;
            var itemRect = new Rect(contentBounds.X, y, contentBounds.Width, itemHeight);
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

        for (int i = first; i < lastExclusive; i++)
        {
            double y = yStart + (i - first) * itemHeight;
            var itemRect = new Rect(contentBounds.X, y, contentBounds.Width, itemHeight);

            var containerRect = getContainerRect != null ? getContainerRect(i, itemRect) : itemRect;
            var element = GetOrCreate(i, rebindExisting);
            element.Measure(new Size(Math.Max(0, containerRect.Width), Math.Max(0, containerRect.Height)));
            element.Arrange(containerRect);
        }
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

        var element = _pool.Count > 0 ? _pool.Pop() : _createContainer();
        element.Parent = _owner;
        element.IsVisible = true;

        _bind(element, index);
        _realized[index] = element;
        return element;
    }

    private void Recycle(int index)
    {
        if (!_realized.Remove(index, out var element))
        {
            return;
        }

        _unbind?.Invoke(element);
        element.Parent = null;
        _pool.Push(element);
    }
}
