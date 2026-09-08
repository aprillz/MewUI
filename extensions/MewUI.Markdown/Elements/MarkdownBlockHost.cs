using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// Hosts the top-level blocks of a scrolling document, creating elements only near the viewport.
/// Unmeasured blocks use the running average height; when a measured block near the viewport
/// turns out to differ from its estimate, the scroll offset is corrected so the anchor block at the
/// top of the viewport stays where the reader sees it.
/// </summary>
/// <remarks>
/// Elements are created and attached only during measure: attaching a fresh element inside an
/// arrange pass leaves the ancestors measure-dirty but arrange-clean, which stalls the next layout.
/// Arrange only positions what exists; when the viewport reaches unrealized blocks it requests a
/// measure pass, and that pass raises the arrange invalidation again so the same update pass lays
/// the new elements out.
/// </remarks>
internal sealed class MarkdownBlockHost : Control, IVisualTreeHost, ILogicalTreeHost
{
    // Realized elements are kept this many viewports beyond the visible range.
    private const double OVERSCAN = 1.0;
    private const double FALLBACK_ESTIMATE = 48;

    private readonly IReadOnlyList<MarkdownBlock> _blocks;
    private readonly Func<MarkdownBlock, FrameworkElement> _create;
    private readonly double _spacing;
    private readonly FrameworkElement?[] _realized;
    private readonly double[] _heights;
    private readonly double[] _prefix;
    private readonly SortedSet<int> _realizedIndices = [];
    // Elements that left the realized window, kept detached until the cap evicts them.
    private readonly Dictionary<int, FrameworkElement> _cache = [];
    private const int CACHE_LIMIT = 120;
    private bool _prefixValid;
    private double _width = double.NaN;
    private double _measuredSum;
    private int _measuredCount;
    private bool _pendingRealize;
    private bool _anchorRetry;
    private bool _disposing;
    private int _correctionCount;
    private (int Index, double Within)? _initialAnchor;
    private int _pendingTarget = -1;
    // Per-kind running averages: a heading and a table differ too much for one global estimate.
    private readonly double[] _kindSum = new double[Enum.GetValues<MarkdownBlockKind>().Length];
    private readonly int[] _kindCount = new int[Enum.GetValues<MarkdownBlockKind>().Length];

    internal MarkdownBlockHost(IReadOnlyList<MarkdownBlock> blocks, double spacing, Func<MarkdownBlock, FrameworkElement> create)
    {
        _blocks = blocks;
        _spacing = Math.Max(0, spacing);
        _create = create;
        _realized = new FrameworkElement?[blocks.Count];
        _heights = new double[blocks.Count];
        _prefix = new double[blocks.Count + 1];
        Array.Fill(_heights, double.NaN);
    }

    internal int RealizedCount => _realizedIndices.Count;

    internal int BlockCount => _blocks.Count;

    /// <summary>Number of scroll offset corrections applied so far; exposed for tests.</summary>
    internal int CorrectionCount => _correctionCount;

    internal FrameworkElement? GetRealized(int index) => _realized[index];

    internal bool IsMeasured(int index) => !double.IsNaN(_heights[index]);

    /// <summary>Returns the block's top in content coordinates, measuring only that block so its own height is exact.</summary>
    internal double GetBlockTop(int index)
    {
        if (index <= 0 || _blocks.Count == 0)
        {
            return 0;
        }
        index = Math.Min(index, _blocks.Count - 1);
        if (!double.IsNaN(_width) && double.IsNaN(_heights[index]))
        {
            MeasureBlock(index, EnsureRealized(index));
            Unrealize(index);
        }
        EnsurePrefix();
        return _prefix[index];
    }

    /// <summary>Returns the index of the block containing the content offset.</summary>
    internal int IndexAt(double offset)
    {
        EnsurePrefix();
        return FindIndexByY(offset);
    }

    /// <summary>Scrolls the owner so the block's top lands on the viewport top; completes over one or two layout passes.</summary>
    internal void RequestScrollToBlock(int index)
    {
        if (_blocks.Count == 0)
        {
            return;
        }
        _pendingTarget = Math.Clamp(index, 0, _blocks.Count - 1);
        InvalidateMeasure();
    }

    /// <summary>Carries the block at the viewport top over from a previous host so a rebuild keeps the reader's place.</summary>
    internal void SetInitialAnchor(int index, double within)
    {
        _initialAnchor = (Math.Clamp(index, 0, Math.Max(0, _blocks.Count - 1)), Math.Max(0, within));
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : 1_000_000;
        ScrollViewer? scroll = Parent as ScrollViewer;
        var (offset, viewportHeight) = GetViewport(availableSize.Height);
        EnsurePrefix();
        int anchorIndex;
        double anchorWithin;
        // Only a block whose height was already measured has a position the reader has seen; a
        // fresh host or a jump into unmeasured territory has nothing to keep in place.
        bool anchorKnown;
        if (_initialAnchor is (int carriedIndex, double carriedWithin))
        {
            _initialAnchor = null;
            anchorIndex = carriedIndex;
            anchorWithin = carriedWithin;
            anchorKnown = true;
            offset = Math.Clamp(_prefix[anchorIndex] + anchorWithin, 0, Math.Max(0, Extent - viewportHeight));
            scroll?.SetScrollOffsets(scroll.HorizontalOffset, offset);
            offset = scroll?.VerticalOffset ?? offset;
        }
        else
        {
            anchorIndex = FindIndexByY(offset);
            anchorWithin = offset - _prefix[anchorIndex];
            anchorKnown = !double.IsNaN(_heights[anchorIndex]);
        }
        bool widthChanged = width != _width;
        if (widthChanged)
        {
            // Heights depend on the wrap width; the anchor block keeps its place across the change.
            _width = width;
            Array.Fill(_heights, double.NaN);
            _measuredSum = 0;
            _measuredCount = 0;
            Array.Clear(_kindSum);
            Array.Clear(_kindCount);
            _prefixValid = false;
            EnsurePrefix();
        }
        if (_pendingTarget >= 0)
        {
            // The target block becomes the anchor at the viewport top. The owner clamps against the
            // extent of its last arrange, so the request stays pending until the offset lands.
            MeasureBlock(_pendingTarget, EnsureRealized(_pendingTarget));
            EnsurePrefix();
            anchorIndex = _pendingTarget;
            anchorWithin = 0;
            anchorKnown = true;
            double target = Math.Clamp(_prefix[anchorIndex], 0, Math.Max(0, Extent - viewportHeight));
            scroll?.SetScrollOffsets(scroll.HorizontalOffset, target);
            offset = scroll?.VerticalOffset ?? target;
            if (Math.Abs(offset - target) < OnePixel())
            {
                _pendingTarget = -1;
            }
        }

        double maxWidth = 0;
        // A correction moves the viewport, which can move the realization window; one more round
        // covers what the moved window needs.
        for (int round = 0; round < 2; round++)
        {
            bool changed = Realize(offset, viewportHeight, ref maxWidth) || widthChanged;
            if (!changed || !anchorKnown)
            {
                break;
            }
            widthChanged = false;
            _prefixValid = false;
            EnsurePrefix();
            double desired = Math.Clamp(_prefix[anchorIndex] + Math.Min(anchorWithin, Math.Max(0, _prefix[anchorIndex + 1] - _prefix[anchorIndex] - 1)),
                0, Math.Max(0, Extent - viewportHeight));
            if (scroll == null || Math.Abs(desired - offset) < OnePixel())
            {
                break;
            }
            _correctionCount++;
            scroll.SetScrollOffsets(scroll.HorizontalOffset, desired);
            offset = scroll.VerticalOffset;
            if (Math.Abs(offset - desired) >= OnePixel())
            {
                // The owner clamped against the extent of its last arrange; the anchor is carried into
                // the next pass, whose metrics include the new extent.
                _initialAnchor = (anchorIndex, anchorWithin);
                _anchorRetry = true;
                break;
            }
        }

        EnsurePrefix();
        _pendingRealize = false;
        // Raised from measure so the ancestor chain is arrange-dirty again for this pass.
        InvalidateArrange();
        return new Size(double.IsFinite(availableSize.Width) ? width : maxWidth, Extent);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (double.IsNaN(_width))
        {
            return;
        }
        EnsurePrefix();
        var (offset, viewportHeight) = GetViewport(bounds.Height);
        foreach (int index in _realizedIndices)
        {
            double height = _prefix[index + 1] - _prefix[index] - _spacing;
            _realized[index]!.Arrange(new Rect(bounds.X, bounds.Y + _prefix[index], bounds.Width, height));
        }
        if (_anchorRetry && !_pendingRealize)
        {
            _anchorRetry = false;
            _pendingRealize = true;
            InvalidateMeasure();
        }
        if (_pendingTarget >= 0 && !_pendingRealize)
        {
            // The owner now holds fresh extent metrics; the next measure can land the target.
            _pendingRealize = true;
            InvalidateMeasure();
        }
        if (_blocks.Count > 0 && !_pendingRealize)
        {
            int first = FindIndexByY(offset);
            int last = FindIndexByY(offset + viewportHeight);
            for (int index = first; index <= last; index++)
            {
                if (_realized[index] == null || double.IsNaN(_heights[index]))
                {
                    // Wheel and thumb input only arrange; a measure pass creates what the viewport now needs.
                    _pendingRealize = true;
                    InvalidateMeasure();
                    break;
                }
            }
        }
    }

    private double Extent => _blocks.Count == 0 ? 0 : _prefix[_blocks.Count] - _spacing;

    private double OnePixel()
    {
        double dpiScale = GetDpi() / 96.0;
        return dpiScale > 0 ? 1.0 / dpiScale : 1.0;
    }

    private (double Offset, double ViewportHeight) GetViewport(double fallbackHeight)
    {
        if (Parent is ScrollViewer scroll && scroll.ViewportHeight > 0)
        {
            return (scroll.VerticalOffset, scroll.ViewportHeight);
        }
        return (0, double.IsFinite(fallbackHeight) ? fallbackHeight : double.PositiveInfinity);
    }

    // Realizes and measures the blocks inside the keep window around the viewport and drops the
    // rest, keeping their measured heights. Returns whether any height changed.
    private bool Realize(double offset, double viewportHeight, ref double maxWidth)
    {
        if (_blocks.Count == 0)
        {
            return false;
        }
        double keepStart = offset - viewportHeight * OVERSCAN;
        double keepEnd = offset + viewportHeight * (1 + OVERSCAN);
        int first = FindIndexByY(keepStart);
        int last = double.IsPositiveInfinity(keepEnd) ? _blocks.Count - 1 : FindIndexByY(keepEnd);

        foreach (int index in _realizedIndices.ToArray())
        {
            // A block holding keyboard focus stays alive outside the window so focus is not lost.
            if ((index < first || index > last) && !IsFocusedSubtree(_realized[index]!))
            {
                Unrealize(index);
            }
        }
        TrimCache(first, last);

        bool changed = false;
        for (int index = first; index <= last; index++)
        {
            var element = EnsureRealized(index);
            changed |= MeasureBlock(index, element);
            maxWidth = Math.Max(maxWidth, element.DesiredSize.Width);
        }
        return changed;
    }

    private FrameworkElement EnsureRealized(int index)
    {
        FrameworkElement? element = _realized[index];
        if (element == null)
        {
            if (_cache.Remove(index, out var cached))
            {
                element = cached;
            }
            else
            {
                element = _create(_blocks[index]);
            }
            _realized[index] = element;
            _realizedIndices.Add(index);
            AttachChild(element);
        }
        return element;
    }

    private bool IsFocusedSubtree(FrameworkElement element)
    {
        return FindVisualRoot() is Window window &&
            window.FocusManager.FocusedElement is UIElement focused &&
            VisualTree.IsInSubtreeOf(focused, element);
    }

    // Drops cached elements farthest from the realized window once the cache exceeds its cap.
    private void TrimCache(int first, int last)
    {
        while (_cache.Count > CACHE_LIMIT)
        {
            int farthest = -1;
            int farthestDistance = -1;
            foreach (int index in _cache.Keys)
            {
                int distance = index < first ? first - index : index > last ? index - last : 0;
                if (distance > farthestDistance)
                {
                    farthest = index;
                    farthestDistance = distance;
                }
            }
            if (_cache.Remove(farthest, out var element))
            {
                MarkdownPresenter.DisposeTree(element);
            }
        }
    }

    private bool MeasureBlock(int index, FrameworkElement element)
    {
        element.Measure(new Size(_width, double.PositiveInfinity));
        double height = LayoutRounding.RoundToPixel(Math.Max(0, element.DesiredSize.Height), GetDpi() / 96.0);
        double previous = _heights[index];
        if (!double.IsNaN(previous) && previous == height)
        {
            return false;
        }
        int kind = (int)_blocks[index].Kind;
        if (double.IsNaN(previous))
        {
            _measuredCount++;
            _measuredSum += height;
            _kindCount[kind]++;
            _kindSum[kind] += height;
        }
        else
        {
            _measuredSum += height - previous;
            _kindSum[kind] += height - previous;
        }
        _heights[index] = height;
        _prefixValid = false;
        return true;
    }

    private void Unrealize(int index)
    {
        FrameworkElement? element = _realized[index];
        if (element == null)
        {
            return;
        }
        _realized[index] = null;
        _realizedIndices.Remove(index);
        DetachChild(element);
        // DisposeTree disposes children before the host itself, so the host only detaches while disposing.
        if (!_disposing)
        {
            // Kept detached so scrolling back reuses the element and its text layouts.
            _cache[index] = element;
        }
    }

    // Estimate for an unmeasured block: the average of measured blocks of its kind, else of all blocks.
    private double GetEstimate(int index)
    {
        int kind = (int)_blocks[index].Kind;
        double raw;
        if (_kindCount[kind] > 0)
        {
            raw = _kindSum[kind] / _kindCount[kind];
        }
        else if (_measuredCount > 0)
        {
            raw = _measuredSum / _measuredCount;
        }
        else
        {
            raw = Math.Max(FALLBACK_ESTIMATE, FontSize * 3);
        }
        return Math.Max(1, LayoutRounding.RoundToPixel(raw, GetDpi() / 96.0));
    }

    private void EnsurePrefix()
    {
        if (_prefixValid)
        {
            return;
        }
        double sum = 0;
        _prefix[0] = 0;
        for (int index = 0; index < _blocks.Count; index++)
        {
            sum += (double.IsNaN(_heights[index]) ? GetEstimate(index) : _heights[index]) + _spacing;
            _prefix[index + 1] = sum;
        }
        _prefixValid = true;
    }

    // Largest index whose top is at or above the content offset.
    private int FindIndexByY(double offset)
    {
        int count = _blocks.Count;
        if (count == 0)
        {
            return 0;
        }
        if (double.IsPositiveInfinity(offset))
        {
            return count - 1;
        }
        offset = Math.Clamp(offset, 0, _prefix[count]);
        int low = 0;
        int high = count - 1;
        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            if (_prefix[middle] <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return low;
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        foreach (int index in _realizedIndices)
        {
            _realized[index]!.Render(context);
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        foreach (int index in _realizedIndices.ToArray())
        {
            if (!visitor(_realized[index]!))
            {
                return false;
            }
        }
        return true;
    }

    bool ILogicalTreeHost.VisitLogicalChildren(Func<Element, bool> visitor) => ((IVisualTreeHost)this).VisitChildren(visitor);

    protected override void OnDispose()
    {
        _disposing = true;
        while (_realizedIndices.Count > 0)
        {
            Unrealize(_realizedIndices.Max);
        }
        foreach (var element in _cache.Values)
        {
            MarkdownPresenter.DisposeTree(element);
        }
        _cache.Clear();
        base.OnDispose();
    }

    internal int CachedCount => _cache.Count;
}
