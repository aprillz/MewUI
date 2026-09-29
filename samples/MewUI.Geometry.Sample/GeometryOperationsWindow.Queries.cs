using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Geometry;
using Aprillz.MewUI.Rendering;

internal sealed partial class GeometryOperationsWindow
{
    private static FrameworkElement CreatePointCard(bool stroke)
    {
        double pointX = 85;
        double pointY = stroke ? 72 : 60;
        var pen = new Pen(_sourceColor, 10);
        Aprillz.MewUI.Rendering.Geometry source = stroke
            ? new LineGeometry(new Point(22, 72), new Point(158, 72))
            : new EllipseGeometry(new Rect(35, 25, 110, 80));
        var preview = new DraggablePreview();
        preview.AddPath(source.GetPathGeometry(),
            stroke ? null : _sourceColor.WithAlpha(110), _sourceColor, stroke ? 10 : 2);
        PathShape pointPath = preview.AddPath(Marker(pointX, pointY), _queryColor, _queryColor);
        TextBlock result = ResultText();

        void Refresh()
        {
            Point point = new(pointX, pointY);
            bool contains = stroke ? source.StrokeContains(pen, point) : source.FillContains(point);
            pointPath.Data = Marker(pointX, pointY);
            result.Text = $"Point ({pointX:F0}, {pointY:F0}): {contains}\nClick or drag the purple point.";
        }

        preview.Drag(pointPath, (deltaX, deltaY) =>
        {
            pointX += deltaX;
            pointY += deltaY;
            Refresh();
        });
        preview.EmptyPressed = point =>
        {
            pointX = point.X;
            pointY = point.Y;
            Refresh();
        };

        Refresh();
        return Card(stroke ? "StrokeContains(point)" : "FillContains(point)", preview, result);
    }

    private static FrameworkElement CreateGeometryQueryCard(QueryOperation operation)
    {
        double otherX = operation == QueryOperation.Intersects ? 113 : 70;
        double otherY = operation == QueryOperation.StrokeContainsWithDetail ? 59 : 52;
        bool stroke = operation == QueryOperation.StrokeContainsWithDetail;
        var pen = new Pen(_sourceColor, 10);
        Aprillz.MewUI.Rendering.Geometry source = stroke
            ? new LineGeometry(new Point(20, 72), new Point(160, 72))
            : new EllipseGeometry(new Rect(20, 20, 125, 90));
        var preview = new DraggablePreview();
        preview.AddPath(source.GetPathGeometry(),
            stroke ? null : _sourceColor.WithAlpha(85), _sourceColor, stroke ? 10 : 2);
        PathShape otherPath = preview.AddPath(new PathGeometry(), _otherColor.WithAlpha(110), _otherColor, 2);
        TextBlock result = ResultText();

        void Refresh()
        {
            Aprillz.MewUI.Rendering.Geometry other = stroke
                ? new EllipseGeometry(new Rect(otherX, otherY, 30, 25))
                : new RectangleGeometry(new Rect(otherX, otherY, 35, 28));
            otherPath.Data = other.GetPathGeometry();

            string value;
            if (operation == QueryOperation.FillContains)
            {
                value = source.FillContains(other).ToString();
            }
            else if (operation == QueryOperation.FillContainsWithDetail)
            {
                value = source.FillContainsWithDetail(other).ToString();
            }
            else if (operation == QueryOperation.Intersects)
            {
                value = source.Intersects(other).ToString();
            }
            else if (operation == QueryOperation.Encloses)
            {
                value = source.Encloses(other).ToString();
            }
            else
            {
                value = source.StrokeContainsWithDetail(pen, other).ToString();
            }

            result.Text = $"Result: {value}\nDrag the orange geometry.";
        }

        preview.Drag(otherPath, (deltaX, deltaY) =>
        {
            otherX += deltaX;
            otherY += deltaY;
            Refresh();
        });

        Refresh();
        string title = operation switch
        {
            QueryOperation.FillContains => "FillContains(geometry)",
            QueryOperation.FillContainsWithDetail => "FillContainsWithDetail",
            QueryOperation.Intersects => "Intersects",
            QueryOperation.Encloses => "Encloses",
            _ => "StrokeContainsWithDetail",
        };
        return Card(title, preview, result);
    }
}
