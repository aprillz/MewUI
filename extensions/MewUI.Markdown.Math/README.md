# Aprillz.MewUI.Markdown.Math

LaTeX math for the [Aprillz.MewUI.Markdown](../MewUI.Markdown/README.md) controls.

[CSharpMath](https://github.com/verybadcat/CSharpMath) typesets each formula, and the extension draws the glyph outlines as vector paths on the MewUI graphics context: formulas follow the document's font size and foreground, stay sharp at any DPI and render on every MewUI backend. No SkiaSharp, no bitmaps.

## Install

```
dotnet add package Aprillz.MewUI.Markdown.Math
```

Targets `net8.0` and `net10.0`.

## Quick start

```csharp
using Aprillz.MewUI.Markdown;

var viewer = new MarkdownViewer()
    .Options(new MarkdownOptions { ConfigurePipeline = static builder => builder.UseMath() })
    .Renderers(new MarkdownRenderers().UseMath())
    .Markdown("Inline $E = mc^2$ and a display formula:\n\n$$\\int_0^1 x^2\\,dx = \\frac{1}{3}$$");
```

`UseMath()` on the pipeline recognizes `$...$`, `$$...$$`, `\(...\)` and `\[...\]`; `UseMath(bracketDelimiters: false)` keeps `\(` and `\[` as escaped brackets. `UseMath()` on the renderers draws the formulas.

## What is drawn

| Source | Presentation |
|---|---|
| `$...$`, `\(...\)` | Inline, text style, sitting on the text baseline |
| `$$...$$`, `\[...\]` inside a line | Inline, display style |
| `$$...$$`, `\[...\]` on lines of its own, with or without the delimiters on the formula's lines | A centered block |

A formula that CSharpMath cannot parse shows CSharpMath's error text in its place. A formula past the safety limits (10,000 characters or 50 nested braces) stays source text.

## LaTeX coverage

CSharpMath covers the common set: fractions, roots, sums, integrals, limits, Greek letters, `\mathbb`, `\mathcal`, `\mathrm`, `\mathbf`, `\text`, `\left`/`\right`, `\underbrace`, `\not`, and the `aligned`, `cases`, `pmatrix`, `bmatrix`, `vmatrix` and `Vmatrix` environments. The extension rewrites a few commands CSharpMath lacks: `\dfrac` and `\tfrac` become `\frac`, and `\boldsymbol` becomes `\mathbf`. It also adds `\overbrace{...}^{...}`, `\xrightarrow[...]{...}` and `\xleftarrow[...]{...}`.

Block formulas are not part of the document selection and have no copy button.
