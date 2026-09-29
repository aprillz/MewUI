using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Geometry;
using Aprillz.MewUI.Rendering;

internal sealed partial class GeometryOperationsWindow
{
    private static FrameworkElement CreateBoundsCard(bool renderBounds)
    {
        PathGeometry curve = Curve();
        var preview = new DraggablePreview();
        Rect bounds = renderBounds
            ? curve.GetRenderBounds(new Pen(_sourceColor, 10))
            : curve.GetTightBounds();
        preview.AddPath(PathGeometry.FromRect(bounds), null, _resultColor, 2);
        preview.AddPath(curve, null, _sourceColor, renderBounds ? 10 : 3);
        TextBlock result = ResultText();
        result.Text = renderBounds
            ? $"Stroke width: 10\nBounds: {RectText(bounds)}"
            : $"Fill bounds: {RectText(bounds)}";
        return Card(renderBounds ? "GetRenderBounds" : "GetTightBounds", preview, result);
    }

    private static FrameworkElement CreatePathCard(PathOperation operation)
    {
        var preview = new DraggablePreview();
        var pen = new Pen(_sourceColor, 10);
        Aprillz.MewUI.Rendering.Geometry source = operation == PathOperation.Outline ? Ring() : Curve();
        PathGeometry output;
        if (operation == PathOperation.Flatten)
        {
            output = source.GetFlattenedPathGeometry();
        }
        else if (operation == PathOperation.Widen)
        {
            output = source.GetWidenedPathGeometry(pen);
        }
        else
        {
            output = source.GetOutlinedPathGeometry();
        }

        bool filledResult = operation == PathOperation.Widen;
        preview.AddPath(output, filledResult ? _resultColor.WithAlpha(100) : null, _resultColor, 2);
        preview.AddPath(source.GetPathGeometry(),
            operation == PathOperation.Outline ? _sourceColor.WithAlpha(70) : null,
            _sourceColor, 3);
        TextBlock result = ResultText();
        result.Text = operation switch
        {
            PathOperation.Flatten => $"The Bézier curve becomes {output.Count} straight-line path commands.",
            PathOperation.Widen => $"A 10 DIP stroke becomes a filled path with {output.Count} commands.",
            _ => $"The filled ring becomes a boundary path with {output.Count} commands.",
        };
        string title = operation switch
        {
            PathOperation.Flatten => "GetFlattenedPathGeometry",
            PathOperation.Widen => "GetWidenedPathGeometry",
            _ => "GetOutlinedPathGeometry",
        };
        return Card(title, preview, result);
    }

    private static FrameworkElement CreateAreaCard()
    {
        var preview = new DraggablePreview();
        var scaleSlider = new Slider().Range(0.55, 1.15).Value(0.8).Width(235);
        PathShape ringPath = preview.AddPath(new PathGeometry(), _sourceColor.WithAlpha(100), _sourceColor, 2);
        TextBlock result = ResultText();

        void Refresh()
        {
            GeometryGroup ring = Ring(scaleSlider.Value);
            ringPath.Data = ring.GetPathGeometry();
            result.Text = $"Filled area: {ring.GetArea():F1} square DIPs";
        }

        scaleSlider.OnValueChanged(_ => Refresh());
        Refresh();
        return Card("GetArea", preview, result, LabeledControl("Ring size", scaleSlider));
    }
}
