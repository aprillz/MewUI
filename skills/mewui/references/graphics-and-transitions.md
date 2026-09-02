# Draw shapes, transform content, and animate replacement

## Shapes

Use shape controls for retained geometry that participates in layout and input. Use `PathShape.Data(string)` for SVG path data, not a complete SVG document.

```csharp
using Aprillz.MewUI.Rendering;

var shapes = new StackPanel()
    .Horizontal()
    .Spacing(12)
    .Children(
        new Ellipse()
            .Size(64)
            .Fill(Color.FromRgb(70, 130, 230))
            .Stroke(Color.FromRgb(40, 80, 180), 2),
        new Line()
            .Size(100, 40)
            .Points(0, 0, 100, 40)
            .Stroke(Color.FromRgb(230, 100, 80), 3)
            .StrokeStyle(new StrokeStyle { DashArray = [8, 4] }),
        new PathShape()
            .Size(64)
            .Data("M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z")
            .Stretch(Stretch.Uniform)
            .Fill(Color.FromRgb(240, 190, 40)));
```

Freeze a programmatically built `PathGeometry` after construction when it will no longer change. Prefer theme palette colors for application chrome; fixed colors are appropriate only when the graphic itself owns those colors.

## Rotate content

Use the public `RotationDecorator` for quarter-turn orientation changes:

```csharp
var rotated = new RotationDecorator()
    .Rotation(Rotation.Clockwise90)
    .Child(new TextBlock().Text("Rotated content").FontSize(24));
```

MewUI's public package does not provide the Gallery's arbitrary `TransformBox`; it is sample-owned code. For arbitrary translate, scale, or rotation behavior, implement an application control only when the task requires it and verify its layout, drawing, and hit testing. Do not present a Gallery helper as a package API.

## Content transitions

Use `TransitionContentControl` when replacing one content subtree should animate:

```csharp
var pageIndex = 1;
var host = new TransitionContentControl
{
    Transition = ContentTransition.CreateSlide(SlideDirection.Left, durationMs: 250),
    Content = new TextBlock().Text("Page 1").Center(),
};

var next = new Button()
    .Content("Next")
    .OnClick(() =>
    {
        pageIndex++;
        host.Content = new TextBlock().Text($"Page {pageIndex}").Center();
    });
```

Built-in factories include fade, slide, scale, and rotate transitions. Keep durations short for routine navigation and avoid starting transitions for state changes that do not replace content.
