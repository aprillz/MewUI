# Aprillz.MewUI.Geometry

Backend-independent operations for [MewUI](https://github.com/aprillz/MewUI) `Geometry`
instances. `PathGeometry`, rectangles, ellipses, lines, and geometry groups share tight and
stroked bounds, flattening, fill and stroke hit testing, area and relationship queries, widened
and outlined paths, and Boolean path operations.

## Install

```sh
dotnet add package Aprillz.MewUI.Geometry
```

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Geometry;
using Aprillz.MewUI.Rendering;
```

## Geometry operations

```csharp
var group = new GeometryGroup();
group.Add(new RectangleGeometry(new Rect(0, 0, 20, 20)));
Geometry geometry = group;
var pen = new Pen(Color.Black, 2);

Rect fillBounds = geometry.GetTightBounds();
Rect strokeBounds = geometry.GetRenderBounds(pen);
bool fillsPoint = geometry.FillContains(new Point(10, 10));
bool strokesPoint = geometry.StrokeContains(pen, new Point(0, 10));

PathGeometry outline = geometry.GetOutlinedPathGeometry();
PathGeometry union = PathGeometryOperations.Combine(
    geometry,
    PathGeometry.FromRect(10, 0, 20, 20),
    GeometryCombineMode.Union);
```

Operations accept tolerance overloads when callers need to choose between absolute and relative
flattening tolerance. Invalid coordinates return safe empty or false results instead of throwing.

## Shape hit testing

Enable precise geometry hit testing once during application startup:

```csharp
GeometryServices.EnableShapeHitTesting();
```

After activation, every `Shape` uses the union of its visible fill and stroke for hit testing.
Before activation, shapes retain MewUI's normal rectangular Bounds hit test. Bounds are still the
first input cull, so a stroke that extends outside its element Bounds is not hittable there.

The package includes Clipper2 (Boost Software License 1.0) and adaptations of WPF WpfGfx geometry
algorithms (MIT). See `THIRD_PARTY_NOTICES.md` in the package.
