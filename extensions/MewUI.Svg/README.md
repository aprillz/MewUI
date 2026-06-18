# Aprillz.MewUI.Svg

SVG parsing and rendering for [MewUI](https://github.com/aprillz/MewUI), drawn through MewUI's `IGraphicsContext`.

**Pure managed, no System.Drawing.** Built on [SVG.NET](https://github.com/svg-net/SVG) with its `System.Drawing` rendering replaced by a MewUI `IGraphicsContext` backend, so it parses and renders SVG on every MewUI backend (Direct2D, GDI, MewVG/OpenGL) and is NativeAOT/trim compatible.

## Install

```
dotnet add package Aprillz.MewUI.Svg
```

Targets `net8.0` and `net10.0`.

## Quick start

`SvgImageSource` plugs into the standard `Image` control, so an SVG renders like any other image — and stays crisp at any size (it re-renders at the laid-out size instead of stretching a bitmap):

```csharp
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Svg;

var image = new Image
{
    Source = SvgImageSource.FromFile("icon.svg"),
    StretchMode = Stretch.Uniform,
}.Size(24, 24);
```

`SvgImageSource` is an `IVectorImageSource`: `Image` draws it vector at the current size each paint, so it stays sharp when resized or on high-DPI displays.

## Loading

```csharp
SvgImageSource.FromFile(path);                 // file path
SvgImageSource.FromString(svgMarkup);          // SVG markup
SvgImageSource.FromStream(stream);             // any stream (e.g. a ZIP entry)
SvgImageSource.FromResource(assembly, name);   // embedded assembly resource
```

## Recoloring (monochrome icons)

Set `Tint` to recolor icons whose fill is inherited (no explicit per-element fill). Changing it re-renders the hosting control automatically:

```csharp
var source = SvgImageSource.FromFile("home.svg");
source.Tint = theme.Foreground;
new Image { Source = source };
```

- `IntrinsicSize` — the SVG viewBox size (used by `Image` for measuring).
- `RasterWidth` / `RasterHeight` — only affect the `CreateImage` raster fallback (e.g. when something asks the source for pixels); the vector `Image` path ignores them.

## Advanced: SvgDocument

For direct document access, the SVG.NET engine type `SvgDocument` (namespace `Svg`) is available:

```csharp
using Svg;

var doc = SvgDocument.Open("icon.svg");   // or .Parse(markup) / .Parse(markup, baseUri)
doc.Render(context, new Rect(0, 0, width, height));   // draw into an IGraphicsContext
```

Use `doc.ViewBoxWidth` / `doc.ViewBoxHeight` for aspect ratio. For most cases prefer `SvgImageSource` + `Image`.

## Backends

MewUI.Svg only uses `IGraphicsContext`, so it renders on whichever backend the host app registers. Direct2D and MewVG (OpenGL) give the best curve/text quality; GDI works but anti-aliases curves and text less smoothly.

## Building from source

SVG.NET is brought in as a git submodule and its non-`System.Drawing` source is compiled into this assembly, so initialize submodules before building:

```
git submodule update --init --recursive
dotnet build extensions/MewUI.Svg/MewUI.Svg.csproj
```

The submodule (`ThirdParty/SvgNet`) is kept pristine; the `*.Drawing.cs` rendering partials are excluded and replaced by the MewUI `*.MewUI.cs` partials in this project.

## Dependencies

`Aprillz.MewUI` and [ExCSS](https://github.com/TylerBrinks/ExCSS) (CSS style parsing). SVG.NET itself is compiled in, not a package reference.

## License

`Aprillz.MewUI.Svg` is MIT. It includes a copy of [SVG.NET](https://github.com/svg-net/SVG) (Microsoft Public License, Ms-PL); see `THIRD_PARTY_NOTICES.md`. System.Drawing is not used or redistributed.
