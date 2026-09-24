using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// Renders a <see cref="RowNode"/> by splitting its rect among its children proportional to weight along the
/// row's orientation, with a draggable <see cref="FlexSplitter"/> between adjacent children. It follows the row's
/// children as they are inserted and removed, keeping the views of the children that stay. A splitter drag changes
/// the weights live and commits them with an AdjustWeights action when it ends.
/// </summary>
internal sealed class FlexRowView : Panel, INodeView
{
    private readonly RowNode _row;
    private readonly FlexViewContext _context;
    private readonly List<FlexSplitter> _splitters = new();
    private readonly HashSet<Node> _observed = new();
    private Orientation _splitterOrientation;
    private bool _released;

    public FlexRowView(RowNode row, FlexViewContext context)
    {
        _row = row;
        _context = context;
        row.View = this;
        _splitterOrientation = row.Orientation;
        row.ChildInserted += OnChildrenChanged;
        row.ChildRemoved += OnChildrenChanged;
        row.Model.DraggingChanged += OnDraggingChanged;
        row.Model.AttributesChanged += OnAttributesChanged;
        SyncChildren();
    }

    public Node Node => _row;

    public void Release()
    {
        if (_released)
        {
            return;
        }
        _released = true;
        _row.ChildInserted -= OnChildrenChanged;
        _row.ChildRemoved -= OnChildrenChanged;
        _row.Model.DraggingChanged -= OnDraggingChanged;
        _row.Model.AttributesChanged -= OnAttributesChanged;
        foreach (var node in _observed)
        {
            node.PropertyChanged -= OnChildPropertyChanged;
        }
        _observed.Clear();
        if (ReferenceEquals(_row.View, this))
        {
            _row.View = null;
        }
    }

    private void OnChildrenChanged(Node child, int index) => SyncChildren();

    /// <summary>Hiding or restoring a torn-off child reflows its siblings.</summary>
    private void OnDraggingChanged() => InvalidateMeasure();

    /// <summary>The splitter size and the root orientation are global attributes.</summary>
    private void OnAttributesChanged() => SyncChildren();

    private void OnChildPropertyChanged(Node child, NodeProperty property)
    {
        if (property == NodeProperty.Weight)
        {
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Makes the panel hold the children's views with a splitter between each pair, in the row's order. Views that
    /// are already in place stay attached; only what changed is added or removed.
    /// </summary>
    internal void SyncChildren()
    {
        if (_released)
        {
            return;
        }

        var children = _row.Children;

        foreach (var node in _observed.ToList())
        {
            if (!ReferenceEquals(node.Parent, _row))
            {
                node.PropertyChanged -= OnChildPropertyChanged;
                _observed.Remove(node);
            }
        }
        foreach (var child in children)
        {
            if (_observed.Add(child))
            {
                child.PropertyChanged += OnChildPropertyChanged;
            }
        }

        // A splitter's resize axis is fixed when it is made; a row whose orientation flipped gets new ones.
        if (_row.Orientation != _splitterOrientation)
        {
            _splitterOrientation = _row.Orientation;
            foreach (var splitter in _splitters)
            {
                Remove(splitter);
            }
            _splitters.Clear();
        }
        while (_splitters.Count < Math.Max(0, children.Count - 1))
        {
            _splitters.Add(CreateSplitter());
        }
        while (_splitters.Count > Math.Max(0, children.Count - 1))
        {
            var extra = _splitters[^1];
            _splitters.RemoveAt(_splitters.Count - 1);
            Remove(extra);
        }

        var desired = new List<UIElement>(children.Count * 2);
        for (int index = 0; index < children.Count; index++)
        {
            if (index > 0)
            {
                desired.Add(_splitters[index - 1]);
            }
            desired.Add(_context.ViewFor(children[index]));
        }

        foreach (var existing in Children.ToList())
        {
            if (existing is UIElement element && !desired.Contains(element))
            {
                Remove(element);
            }
        }
        for (int index = 0; index < desired.Count; index++)
        {
            var element = desired[index];
            if (index < Children.Count && ReferenceEquals(Children[index], element))
            {
                continue;
            }
            if (ReferenceEquals(element.Parent, this))
            {
                Remove(element);
            }
            else if (element.Parent is Panel otherParent)
            {
                // Still shown where the node was before; that container follows its own change later.
                otherParent.Remove(element);
            }
            Insert(index, element);
        }
        InvalidateMeasure();
    }

    private FlexSplitter CreateSplitter()
    {
        bool horizontal = _splitterOrientation == Orientation.Horizontal;
        var splitter = new FlexSplitter { IsColumnAxis = !horizontal, BarThickness = _row.Model.SplitterSize };

        // The splitter before child index (FlexLayout RowNode.calculateSplit indexes by the child to its right).
        int index = 0;
        double[] initialSizes = Array.Empty<double>();
        double sum = 0;
        double startPosition = 0;
        double dragStart = 0;
        double[]? lastWeights = null;

        splitter.SplitterDragStarted += e =>
        {
            index = _splitters.IndexOf(splitter) + 1;
            var position = e.GetPosition(this);
            dragStart = horizontal ? position.X : position.Y;
            (initialSizes, sum, startPosition) = _row.GetSplitterInitials(index);
            lastWeights = null;
        };

        splitter.SplitterDragging += e =>
        {
            var position = e.GetPosition(this);
            double main = horizontal ? position.X : position.Y;
            // Only the delta matters; startPosition supplies the absolute basis the model math expects.
            double splitterPos = startPosition + (main - dragStart);
            var weights = _row.CalculateSplit(index, splitterPos, initialSizes, sum, startPosition);

            // The weights change live (each change re-lays out this row) and are committed once, at the end.
            var children = _row.Children;
            int applied = Math.Min(weights.Length, children.Count);
            for (int child = 0; child < applied; child++)
            {
                ((SizedNode)children[child]).Weight = weights[child];
            }
            lastWeights = weights;
        };

        splitter.SplitterDragCompleted += () =>
        {
            if (lastWeights is double[] weights && weights.Length == _row.Children.Count)
            {
                _row.Model.DoAction(DockAction.AdjustWeights(_row.GetId(), weights));
            }
            lastWeights = null;
        };

        return splitter;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        LayoutChildren(new Rect(0, 0, availableSize.Width, availableSize.Height), arrange: false);
        return availableSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        _row.Rect = bounds;
        LayoutChildren(bounds, arrange: true);
    }

    /// <summary>Splits <paramref name="bounds"/> among the children by weight and measures or arranges each child and splitter in its share.</summary>
    private void LayoutChildren(Rect bounds, bool arrange)
    {
        if (_row.Children.Count == 0)
        {
            return;
        }

        double splitterSize = _row.Model.SplitterSize;

        // Tear-off: the dragged child (a tabset being torn out) is arranged off-screen and excluded from the weight
        // split, so its siblings reflow to fill the gap. Its view stays alive to carry the drag; ESC restores it.
        var dragging = _row.Model.DraggingNode;
        var visible = new List<Node>();
        foreach (var child in _row.Children)
        {
            // Exclude a child with no surviving content: the dragged tabset itself, OR a single-tab tabset whose tab
            // is being dragged (it would otherwise linger as an empty caption + pane).
            if (dragging is not null && !ModelUtils.HasContent(child, dragging))
            {
                Place(ChildView(child), Rect.Empty, arrange);
            }
            else
            {
                visible.Add(child);
            }
        }

        int count = visible.Count;
        if (count == 0)
        {
            foreach (var splitter in _splitters)
            {
                Place(splitter, Rect.Empty, arrange);
            }
            return;
        }

        bool horizontal = _row.Orientation == Orientation.Horizontal;
        double totalSplitters = (count - 1) * splitterSize;
        double available = Math.Max(0, (horizontal ? bounds.Width : bounds.Height) - totalSplitters);

        double sumWeights = 0;
        foreach (var child in visible)
        {
            sumWeights += ((SizedNode)child).Weight;
        }
        if (sumWeights <= 0)
        {
            sumWeights = 1;
        }

        double offset = horizontal ? bounds.X : bounds.Y;
        int splitterIndex = 0;
        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                if (splitterIndex < _splitters.Count)
                {
                    var splitterRect = horizontal
                        ? new Rect(offset, bounds.Y, splitterSize, bounds.Height)
                        : new Rect(bounds.X, offset, bounds.Width, splitterSize);
                    Place(_splitters[splitterIndex], splitterRect, arrange);
                    splitterIndex++;
                }
                offset += splitterSize;
            }

            // An unbounded row (measured with infinite room) offers every child the whole of it.
            double extent = double.IsPositiveInfinity(available)
                ? available
                : ((SizedNode)visible[index]).Weight / sumWeights * available;
            var childRect = horizontal
                ? new Rect(offset, bounds.Y, extent, bounds.Height)
                : new Rect(bounds.X, offset, bounds.Width, extent);
            Place(ChildView(visible[index]), childRect, arrange);
            offset += extent;
        }

        // A removed child leaves one boundary fewer; park the leftover splitter(s) off-screen.
        for (int leftover = splitterIndex; leftover < _splitters.Count; leftover++)
        {
            Place(_splitters[leftover], Rect.Empty, arrange);
        }
    }

    /// <summary>Only a view this row holds is laid out here; one borrowed elsewhere (a maximized tabset) is laid out there.</summary>
    private UIElement? ChildView(Node child) =>
        child.View is UIElement view && ReferenceEquals(view.Parent, this) ? view : null;

    private static void Place(UIElement? element, Rect rect, bool arrange)
    {
        if (element is null)
        {
            return;
        }

        if (arrange)
        {
            element.Arrange(rect);
        }
        else
        {
            element.Measure(rect.Size);
        }
    }
}
