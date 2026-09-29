using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Geometry;
using Aprillz.MewUI.Rendering;

internal sealed partial class GeometryOperationsWindow
{
    private static FrameworkElement CreateCombineCard(GeometryCombineMode mode)
    {
        double leftX = 18;
        double leftY = 23;
        double rightX = 88;
        double rightY = 49;
        var preview = new DraggablePreview();
        PathShape resultPath = preview.AddPath(new PathGeometry(), _resultColor.WithAlpha(125), _resultColor);
        PathShape leftPath = preview.AddPath(new PathGeometry(), null, _sourceColor, 3);
        PathShape rightPath = preview.AddPath(new PathGeometry(), null, _otherColor, 3);
        TextBlock result = ResultText();

        void Refresh()
        {
            var left = new EllipseGeometry(new Rect(leftX, leftY, 95, 77));
            var right = new RectangleGeometry(new Rect(rightX, rightY, 67, 52));
            leftPath.Data = left.GetPathGeometry();
            rightPath.Data = right.GetPathGeometry();
            PathGeometry combined = PathGeometryOperations.Combine(left, right, mode);
            resultPath.Data = combined;
            result.Text = $"Green: {mode} result\nArea: {combined.GetArea():F1}\nDrag either input shape.";
        }

        preview.Drag(leftPath, (deltaX, deltaY) =>
        {
            leftX += deltaX;
            leftY += deltaY;
            Refresh();
        });
        preview.Drag(rightPath, (deltaX, deltaY) =>
        {
            rightX += deltaX;
            rightY += deltaY;
            Refresh();
        });

        Refresh();
        return Card($"Combine: {mode}", preview, result);
    }

    private static FrameworkElement CreateShapeHitTestCard()
    {
        var preview = new DraggablePreview();
        PathShape ringPath = preview.AddPath(Ring().GetPathGeometry(),
            _sourceColor.WithAlpha(110), _sourceColor, 2);
        ringPath.IsHitTestVisible = true;
        TextBlock result = ResultText();
        result.Text = "Click the filled ring or its empty center.";
        ringPath.OnMouseDown(eventArgs =>
        {
            result.Text = "PathShape hit: True";
            eventArgs.Handled = true;
        });
        preview.EmptyPressed = _ => result.Text = "PathShape hit: False";
        return Card("Shape hit testing", preview, result);
    }
}
