using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Terminal;

/// <summary>One visible row. Each row is its own element, so a change re-records that row alone.</summary>
internal sealed class TerminalRowVisual : FrameworkElement
{
    private readonly TerminalView _view;

    public TerminalRowVisual(TerminalView view, int index)
    {
        _view = view;
        Index = index;
        IsHitTestVisible = false;
    }

    public int Index { get; }

    protected override void OnRender(IGraphicsContext context) => _view.RenderRow(context, Index, Bounds, this);
}

/// <summary>The cursor and the text being composed, kept apart from the rows so blinking re-records nothing else.</summary>
internal sealed class TerminalCursorVisual : FrameworkElement
{
    private readonly TerminalView _view;

    public TerminalCursorVisual(TerminalView view)
    {
        _view = view;
        IsHitTestVisible = false;
    }

    protected override void OnRender(IGraphicsContext context) => _view.RenderCursor(context, Bounds);
}

/// <summary>Lays out the rows, the cursor and the scroll bar, and paints the default background.</summary>
internal sealed class TerminalSurface : Panel
{
    private readonly TerminalView _view;
    private readonly List<TerminalRowVisual> _rows = new();
    private readonly TerminalCursorVisual _cursor;
    private readonly ScrollBar _scrollBar;

    public TerminalSurface(TerminalView view, ScrollBar scrollBar)
    {
        _view = view;
        _scrollBar = scrollBar;
        _cursor = new TerminalCursorVisual(view);
        ClipToBounds = true;
        Add(_cursor);
        Add(scrollBar);
    }

    public IReadOnlyList<TerminalRowVisual> Rows => _rows;

    public TerminalCursorVisual CursorVisual => _cursor;

    /// <summary>The rectangle the cell grid occupies, in window coordinates.</summary>
    public Rect TextArea { get; private set; }

    public void SetRowCount(int count)
    {
        while (_rows.Count > count)
        {
            var row = _rows[^1];
            _rows.RemoveAt(_rows.Count - 1);
            _view.ReleaseRow(row);
            Remove(row);
        }

        while (_rows.Count < count)
        {
            var row = new TerminalRowVisual(_view, _rows.Count);
            _rows.Add(row);

            // Rows sit below the cursor and the scroll bar in drawing order.
            Insert(_rows.Count - 1, row);
        }

        InvalidateMeasure();
    }

    /// <summary>The rectangle of <paramref name="span"/> cells from a viewport cell, in window coordinates.</summary>
    public Rect CellRect(int column, int row, int span)
    {
        double width = _view.CellWidth;
        double height = _view.CellHeight;
        return new Rect(TextArea.X + (column * width), TextArea.Y + (row * height), span * width, height);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        _view.EnsureMetrics();
        double barWidth = 0;
        if (_scrollBar.IsVisible)
        {
            _scrollBar.Measure(availableSize);
            barWidth = _scrollBar.DesiredSize.Width;
        }

        var rowSize = new Size(_view.CellWidth * _view.Columns, _view.CellHeight);
        foreach (var row in _rows)
        {
            row.Measure(rowSize);
        }

        _cursor.Measure(new Size(_view.CellWidth * 2, _view.CellHeight));
        double padding = 2 * _view.TextPadding;
        return new Size((_view.InitialColumns * _view.CellWidth) + barWidth + padding, (_view.InitialRows * _view.CellHeight) + padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        double barWidth = _scrollBar.IsVisible ? _scrollBar.DesiredSize.Width : 0;
        if (_scrollBar.IsVisible)
        {
            _scrollBar.Arrange(new Rect(bounds.Right - barWidth, bounds.Y, barWidth, bounds.Height));
        }

        double padding = _view.TextPadding;
        TextArea = new Rect(
            bounds.X + padding,
            bounds.Y + padding,
            Math.Max(0, bounds.Width - barWidth - (2 * padding)),
            Math.Max(0, bounds.Height - (2 * padding)));

        double height = _view.CellHeight;
        double width = _view.CellWidth * _view.Columns;
        for (int index = 0; index < _rows.Count; index++)
        {
            _rows[index].Arrange(new Rect(TextArea.X, TextArea.Y + (index * height), width, height));
        }

        _view.ArrangeCursor();
        _view.OnTextAreaArranged(TextArea.Width, TextArea.Height);
    }

    protected override void OnRender(IGraphicsContext context) => _view.RenderBackground(context, Bounds);
}
