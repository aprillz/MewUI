using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// Renders a <see cref="RowNode"/> by splitting its rect among its children proportional to weight along the
/// row's orientation, with a draggable <see cref="FlexSplitter"/> between adjacent children. The splitter drag
/// uses the model's split math (<see cref="RowNode.CalculateSplit"/>) and commits via an AdjustWeights action.
/// </summary>
internal sealed class FlexRowView : Panel
{
    private readonly RowNode _row;
    private readonly FlexViewContext _context;
    private readonly double _splitterSize;
    private readonly List<FlexSplitter> _splitters = new();

    public FlexRowView(RowNode row, FlexViewContext context)
    {
        _row = row;
        _context = context;
        _splitterSize = row.Model.SplitterSize;
        row.View = this;
        Rebuild();
    }

    private void Rebuild()
    {
        Clear();
        _splitters.Clear();

        var children = _row.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (i > 0)
            {
                var splitter = CreateSplitter(i);
                _splitters.Add(splitter);
                Add(splitter);
            }
            Add(FlexViewFactory.BuildNodeView(children[i], _context));
        }
        InvalidateMeasure();
    }

    // The splitter before child index (FlexLayout RowNode.calculateSplit indexes by the child to its right).
    private FlexSplitter CreateSplitter(int index)
    {
        bool horizontal = _row.Orientation == Orientation.Horizontal;
        var splitter = new FlexSplitter { IsColumnAxis = !horizontal, BarThickness = _splitterSize };

        double[] initialSizes = Array.Empty<double>();
        double sum = 0;
        double startPosition = 0;
        double dragStart = 0;

        splitter.SplitterDragStarted += e =>
        {
            var position = e.GetPosition(this);
            dragStart = horizontal ? position.X : position.Y;
            (initialSizes, sum, startPosition) = _row.GetSplitterInitials(index);
        };

        splitter.SplitterDragging += e =>
        {
            var position = e.GetPosition(this);
            double main = horizontal ? position.X : position.Y;
            // Only the delta matters; startPosition supplies the absolute basis the model math expects.
            double splitterPos = startPosition + (main - dragStart);
            var weights = _row.CalculateSplit(index, splitterPos, initialSizes, sum, startPosition);

            // Apply weights directly and re-arrange (no full rebuild) for a smooth drag; the model stays live.
            var children = _row.Children;
            int applied = Math.Min(weights.Length, children.Count);
            for (int k = 0; k < applied; k++)
            {
                ((SizedNode)children[k]).Weight = weights[k];
            }
            InvalidateArrange();
        };

        return splitter;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        foreach (var child in _row.Children)
        {
            child.View?.Measure(availableSize);
        }
        foreach (var splitter in _splitters)
        {
            splitter.Measure(availableSize);
        }
        return availableSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        _row.Rect = bounds;

        if (_row.Children.Count == 0)
        {
            return;
        }

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
                child.View?.Arrange(Rect.Empty);
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
                splitter.Arrange(Rect.Empty);
            }
            return;
        }

        bool horizontal = _row.Orientation == Orientation.Horizontal;
        double totalSplitters = (count - 1) * _splitterSize;
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
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                if (splitterIndex < _splitters.Count)
                {
                    var splitterRect = horizontal
                        ? new Rect(offset, bounds.Y, _splitterSize, bounds.Height)
                        : new Rect(bounds.X, offset, bounds.Width, _splitterSize);
                    _splitters[splitterIndex].Arrange(splitterRect);
                    splitterIndex++;
                }
                offset += _splitterSize;
            }

            double extent = ((SizedNode)visible[i]).Weight / sumWeights * available;
            var childRect = horizontal
                ? new Rect(offset, bounds.Y, extent, bounds.Height)
                : new Rect(bounds.X, offset, bounds.Width, extent);
            visible[i].View?.Arrange(childRect);
            offset += extent;
        }

        // A removed child leaves one boundary fewer; park the leftover splitter(s) off-screen.
        for (int s = splitterIndex; s < _splitters.Count; s++)
        {
            _splitters[s].Arrange(Rect.Empty);
        }
    }
}
