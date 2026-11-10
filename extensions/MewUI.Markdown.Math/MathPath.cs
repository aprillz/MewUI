using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Markdown;

/// <summary>A glyph outline or rule CSharpMath builds segment by segment; disposing it records the path.</summary>
internal sealed class MathPath : CSharpMath.Rendering.FrontEnd.Path
{
    private readonly MathCanvas _owner;
    private readonly PathGeometry _geometry = new();

    public MathPath(MathCanvas owner)
    {
        _owner = owner;
    }

    public override System.Drawing.Color? Foreground { get; set; }

    public override void MoveTo(float x0, float y0)
    {
        Point point = _owner.Map(x0, y0);
        _geometry.MoveTo(point.X, point.Y);
    }

    public override void LineTo(float x1, float y1)
    {
        Point point = _owner.Map(x1, y1);
        _geometry.LineTo(point.X, point.Y);
    }

    public override void Curve3(float x1, float y1, float x2, float y2)
    {
        Point control = _owner.Map(x1, y1);
        Point end = _owner.Map(x2, y2);
        _geometry.QuadTo(control.X, control.Y, end.X, end.Y);
    }

    public override void Curve4(float x1, float y1, float x2, float y2, float x3, float y3)
    {
        Point first = _owner.Map(x1, y1);
        Point second = _owner.Map(x2, y2);
        Point end = _owner.Map(x3, y3);
        _geometry.BezierTo(first.X, first.Y, second.X, second.Y, end.X, end.Y);
    }

    public override void CloseContour()
    {
        if (!_geometry.IsEmpty)
        {
            _geometry.Close();
        }
    }

    public override void Dispose() => _owner.AddPath(_geometry, Foreground);
}
