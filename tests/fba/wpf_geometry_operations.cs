#:sdk Microsoft.NET.Sdk

#:property OutputType=Exe
#:property TargetFramework=net10.0-windows
#:property UseWPF=True
#:property PublishAot=False

using System.Windows;
using System.Windows.Media;

var curve = CreateGeometry(context =>
{
    context.BeginFigure(new Point(0, 0), true, false);
    context.BezierTo(new Point(0, 100), new Point(100, 100), new Point(100, 0), true, false);
});
PrintRect("curve.bounds", curve.Bounds);

var openRectangle = CreateGeometry(context =>
{
    context.BeginFigure(new Point(0, 0), true, false);
    context.LineTo(new Point(10, 0), true, false);
    context.LineTo(new Point(10, 10), true, false);
    context.LineTo(new Point(0, 10), true, false);
});
Console.WriteLine($"open.fill.center={openRectangle.FillContains(new Point(5, 5))}");
Console.WriteLine($"open.fill.tolerance={openRectangle.FillContains(new Point(10.1, 5), 0.2, ToleranceType.Absolute)}");

var line = CreateGeometry(context =>
{
    context.BeginFigure(new Point(0, 0), false, false);
    context.LineTo(new Point(10, 0), true, false);
});
var flatPen = new Pen(Brushes.Black, 4);
var squarePen = new Pen(Brushes.Black, 4)
{
    StartLineCap = PenLineCap.Square,
    EndLineCap = PenLineCap.Square,
};
PrintRect("line.flat", line.GetRenderBounds(flatPen));
PrintRect("line.square", line.GetRenderBounds(squarePen));
var roundPen = new Pen(Brushes.Black, 4)
{
    StartLineCap = PenLineCap.Round,
    EndLineCap = PenLineCap.Round,
};
PrintRect("line.round", line.GetRenderBounds(roundPen));
var dashPen = new Pen(Brushes.Black, 2)
{
    DashStyle = new DashStyle(new[] { 2.0, 2.0 }, 0),
};
Console.WriteLine($"line.dash.hit1={line.StrokeContains(dashPen, new Point(1, 0))}");
Console.WriteLine($"line.dash.hit6={line.StrokeContains(dashPen, new Point(6, 0))}");

var corner = CreateGeometry(context =>
{
    context.BeginFigure(new Point(0, 10), false, false);
    context.LineTo(new Point(10, 0), true, false);
    context.LineTo(new Point(20, 10), true, false);
});
var miterPen = new Pen(Brushes.Black, 4)
{
    LineJoin = PenLineJoin.Miter,
    MiterLimit = 10,
};
PrintRect("corner.miter", corner.GetRenderBounds(miterPen));

var outer = new RectangleGeometry(new Rect(0, 0, 10, 10));
var inner = new RectangleGeometry(new Rect(2, 2, 2, 2));
var partial = new RectangleGeometry(new Rect(8, 0, 5, 5));
var touching = new RectangleGeometry(new Rect(10, 0, 2, 2));
Console.WriteLine($"relation.contains={outer.FillContainsWithDetail(inner)}");
Console.WriteLine($"relation.inside={inner.FillContainsWithDetail(outer)}");
Console.WriteLine($"relation.equal={outer.FillContainsWithDetail(outer)}");
Console.WriteLine($"relation.partial={outer.FillContainsWithDetail(partial)}");
Console.WriteLine($"relation.touching={outer.FillContainsWithDetail(touching)}");

static StreamGeometry CreateGeometry(Action<StreamGeometryContext> build)
{
    var geometry = new StreamGeometry();
    using (StreamGeometryContext context = geometry.Open())
    {
        build(context);
    }
    geometry.Freeze();
    return geometry;
}

static void PrintRect(string name, Rect rect) =>
    Console.WriteLine($"{name}={rect.X:R},{rect.Y:R},{rect.Width:R},{rect.Height:R}");
