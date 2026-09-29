using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Geometry;
using Aprillz.MewUI.Rendering;

internal sealed class DraggablePreview : Canvas
{
    public const double PREVIEW_WIDTH = 180;
    public const double PREVIEW_HEIGHT = 130;

    private readonly List<Operand> _operands = [];
    private Operand? _activeOperand;
    private Point _previousPoint;

    public Action<Point>? EmptyPressed { get; set; }

    public DraggablePreview()
    {
        Width = PREVIEW_WIDTH;
        Height = PREVIEW_HEIGHT;
        ClipToBounds = true;
    }

    public PathShape AddPath(PathGeometry geometry, Color? fill = null, Color? stroke = null, double thickness = 1)
    {
        var path = new PathShape
        {
            Data = geometry,
            Stretch = Stretch.Fill,
            ViewBox = new Rect(0, 0, PREVIEW_WIDTH, PREVIEW_HEIGHT),
            Width = PREVIEW_WIDTH,
            Height = PREVIEW_HEIGHT,
            IsHitTestVisible = false,
        };

        if (fill is Color fillColor)
        {
            path.Fill(fillColor);
        }

        if (stroke is Color strokeColor)
        {
            path.Stroke(strokeColor, thickness);
        }

        Add(path);
        return path;
    }

    public void Drag(PathShape path, Action<double, double> move, bool useStroke = false)
        => _operands.Add(new Operand(path, move, useStroke));

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (eventArgs.Button != MouseButton.Left || eventArgs.Handled)
        {
            return;
        }

        Point point = eventArgs.GetPosition(this);
        for (int index = _operands.Count - 1; index >= 0; index--)
        {
            Operand operand = _operands[index];
            PathGeometry? geometry = operand.Path.Data;
            if (geometry is null)
            {
                continue;
            }

            bool hit = operand.UseStroke
                ? geometry.StrokeContains(new Pen(Color.Black, 12), point)
                : geometry.FillContains(point);
            if (!hit)
            {
                continue;
            }

            if (FindVisualRoot() is Window window)
            {
                window.CaptureMouse(this);
            }

            if (IsMouseCaptured)
            {
                _activeOperand = operand;
                _previousPoint = point;
            }

            eventArgs.Handled = true;
            return;
        }

        EmptyPressed?.Invoke(point);
        eventArgs.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        if (_activeOperand is not Operand operand || !IsMouseCaptured || !eventArgs.LeftButton)
        {
            return;
        }

        Point point = eventArgs.GetPosition(this);
        operand.Move(point.X - _previousPoint.X, point.Y - _previousPoint.Y);
        _previousPoint = point;
        eventArgs.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        base.OnMouseUp(eventArgs);
        if (eventArgs.Button != MouseButton.Left || _activeOperand is null)
        {
            return;
        }

        _activeOperand = null;
        if (FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }

        eventArgs.Handled = true;
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == IsMouseCapturedProperty && !IsMouseCaptured)
        {
            _activeOperand = null;
        }
    }

    private sealed record Operand(PathShape Path, Action<double, double> Move, bool UseStroke);
}
