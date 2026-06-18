# Aprillz.MewUI.Svg

SVG parsing and rendering for [MewUI](https://github.com/aprillz/MewUI), drawn through MewUI's `IGraphicsContext`.

**Pure managed, no System.Drawing.** Built on [SVG.NET](https://github.com/svg-net/SVG) with its `System.Drawing` rendering replaced by a MewUI `IGraphicsContext` backend, so it parses and renders SVG on every MewUI backend (Direct2D, GDI, MewVG/OpenGL) and is NativeAOT/trim compatible.

## Install

```
dotnet add package Aprillz.MewUI.Svg
```

Targets `net8.0` and `net10.0`.

## Quick start

Load an `SvgDocument` and render it into any element's `OnRender`:

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Svg;

sealed class SvgView : FrameworkElement
{
    public SvgDocument? Document { get; set; }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);
        Document?.Render(context, new Rect(0, 0, ActualWidth, ActualHeight));
    }
}

// usage
var view = new SvgView { Document = SvgDocument.Open("icon.svg") };
```

## Loading

`SvgDocument` (namespace `Svg`) parses from a file, stream, or string:

| Method | Source |
|---|---|
| `SvgDocument.Open(path)` | file path |
| `SvgDocument.Load(path)` | file path (MewUI overload) |
| `SvgDocument.Parse(svg)` | SVG markup string |
| `SvgDocument.Parse(svg, baseUri)` | markup + base URI for relative references |

## Rendering

```csharp
document.Render(IGraphicsContext context, Rect destRect);
```

The document is scaled into `destRect`. Use `document.ViewBoxWidth` / `document.ViewBoxHeight` to preserve aspect ratio when computing `destRect`. For a lower-level path, implement `ISvgRenderer` (see `MewSvgRenderer`) and call `document.Draw(renderer)`.

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
