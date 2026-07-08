using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// A draggable bar between two adjacent row children. Owns cursor + mouse capture + drag state and exposes the
/// drag as events so <see cref="FlexRowView"/> applies the weight resize (the split math lives in
/// <see cref="Model.RowNode"/>). Colours come from the themed <see cref="DockStyles"/>: transparent at rest with
/// a tinted grip, accent on hover/drag (reported as the Pressed visual state).
/// </summary>
internal sealed class FlexSplitter : Control
{
    public event Action<MouseEventArgs>? SplitterDragStarted;
    public event Action<MouseEventArgs>? SplitterDragging;
    public event Action? SplitterDragCompleted;

    private bool _isDragging;

    public bool IsColumnAxis { get; init; }

    public double BarThickness { get; init; }

    // Report Pressed while dragging so the style's Pressed trigger highlights the bar.
    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (_isDragging)
        {
            state = new VisualState { Flags = state.Flags | VisualStateFlags.Pressed };
        }
        return state;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButton.Left && FindVisualRoot() is Window window)
        {
            _isDragging = true;
            InvalidateVisualState();
            window.CaptureMouse(this);
            SplitterDragStarted?.Invoke(e);
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDragging)
        {
            SplitterDragging?.Invoke(e);
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButton.Left && _isDragging)
        {
            _isDragging = false;
            InvalidateVisualState();
            (FindVisualRoot() as Window)?.ReleaseMouseCapture();
            SplitterDragCompleted?.Invoke();
            e.Handled = true;
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        var background = GetValue(BackgroundProperty);
        var lineColor = GetValue(BorderBrushProperty);

        if (background.A > 0)
        {
            double radius = Math.Min(Theme.Metrics.ControlCornerRadius, BarThickness / 2.0);
            if (radius > 0)
            {
                context.FillRoundedRectangle(bounds, radius, radius, background);
            }
            else
            {
                context.FillRectangle(bounds, background);
            }
        }

        double length = Theme.Metrics.BaseControlHeight;
        double centerX = bounds.X + bounds.Width / 2;
        double centerY = bounds.Y + bounds.Height / 2;

        if (IsColumnAxis)
        {
            length = Math.Min(length, bounds.Width - 8);
            context.DrawLine(new Point(centerX - length / 2, centerY), new Point(centerX + length / 2, centerY),
                lineColor, Theme.Metrics.ControlBorderThickness);
        }
        else
        {
            length = Math.Min(length, bounds.Height - 8);
            context.DrawLine(new Point(centerX, centerY - length / 2), new Point(centerX, centerY + length / 2),
                lineColor, Theme.Metrics.ControlBorderThickness);
        }
    }
}
