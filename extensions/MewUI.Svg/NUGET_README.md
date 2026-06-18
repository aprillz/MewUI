# Aprillz.MewUI.Svg

SVG parsing and rendering for [MewUI](https://github.com/aprillz/MewUI), drawn through MewUI's
`IGraphicsContext`. **Pure managed, no System.Drawing** — built on [SVG.NET](https://github.com/svg-net/SVG)
with a MewUI rendering backend, so it works on every MewUI backend (Direct2D, GDI, MewVG/OpenGL) and is
NativeAOT/trim compatible.

```csharp
using Svg;

// load
var document = SvgDocument.Open("icon.svg");   // or .Parse(svgMarkup)

// render into any element's OnRender(IGraphicsContext context)
document.Render(context, new Rect(0, 0, ActualWidth, ActualHeight));
```

Use `document.ViewBoxWidth` / `ViewBoxHeight` to preserve aspect ratio when sizing the destination rect.

Depends on `Aprillz.MewUI` and `ExCSS`. Bundles SVG.NET (Ms-PL); see `THIRD_PARTY_NOTICES.md`.

Full docs: https://github.com/aprillz/MewUI/blob/main/extensions/MewUI.Svg/README.md
