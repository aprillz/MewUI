# Aprillz.MewUI.Svg

SVG rendering for [MewUI](https://github.com/aprillz/MewUI), drawn through MewUI's `IGraphicsContext`.
**Pure managed, no System.Drawing** — built on [SVG.NET](https://github.com/svg-net/SVG) with a MewUI
rendering backend, so it works on every MewUI backend (Direct2D, GDI, MewVG/OpenGL) and is
NativeAOT/trim compatible.

`SvgImageSource` plugs into the standard `Image` control and stays crisp at any size (it re-renders
at the laid-out size, not a stretched bitmap):

```csharp
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Svg;

var image = new Image
{
    Source = SvgImageSource.FromFile("icon.svg"),   // or FromString / FromStream / FromResource
    StretchMode = Stretch.Uniform,
}.Size(24, 24);

// recolor a monochrome icon (re-renders automatically)
var src = SvgImageSource.FromFile("home.svg");
src.Tint = Colors.White;
```

For direct document access the SVG.NET engine type `Svg.SvgDocument` is also available
(`SvgDocument.Open/Parse` + `doc.Render(context, rect)`).

Depends on `Aprillz.MewUI` and `ExCSS`. Bundles SVG.NET (Ms-PL); see `THIRD_PARTY_NOTICES.md`.

Full docs: https://github.com/aprillz/MewUI/blob/main/extensions/MewUI.Svg/README.md
