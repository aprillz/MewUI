# Aprillz.MewUI.Skia

Skia (SkiaSharp) rendering integration for [MewUI](https://github.com/aprillz/MewUI). Provides a `SkiaCanvasView`
control so you can draw with SkiaSharp inside a MewUI window, plus the `ISkiaInteropProvider` registry.

- Ships a **CPU upload fallback** that works on any MewUI backend out of the box.
- Install a matching **`Aprillz.MewUI.Skia.Interop.*`** package for a **zero-copy GPU** path on your backend.

## Install

```
dotnet add package Aprillz.MewUI.Skia
```

Then add the interop package for the backend you run on (optional - without it, the CPU fallback is used):

| Interop package | Backend | Activate at startup |
|---|---|---|
| `Aprillz.MewUI.Skia.Interop.Direct2D` | Direct2D (Windows) | `SkiaDirect2DInterop.Use()` |
| `Aprillz.MewUI.Skia.Interop.Gdi` | GDI (Windows) | `SkiaGdiInterop.Register()` |
| `Aprillz.MewUI.Skia.Interop.MewVG.Win32` | MewVG / Win32 | `SkiaMewVGWin32Interop.Use()` |
| `Aprillz.MewUI.Skia.Interop.MewVG.X11` | MewVG / X11 (Linux) | `SkiaMewVGX11Interop.Use()` |
| `Aprillz.MewUI.Skia.Interop.MewVG.MacOS` | MewVG / macOS | `SkiaMewVGMacOSInterop.Use()` |

Each interop bridges Skia and that backend so the Skia surface is consumed without a per-frame copy. Register it
once at startup, after the backend is registered; if no interop is registered, `SkiaCanvasView` falls back to a CPU
upload (correct, just not zero-copy).

## Usage

```csharp
// startup: register your backend, then (optionally) the matching Skia interop
Direct2DBackend.Register();
SkiaDirect2DInterop.Use();

// draw with SkiaSharp inside MewUI
var canvas = new SkiaCanvasView();
canvas.PaintSurface += (s, e) =>
{
    var c = e.Surface.Canvas;
    c.Clear(SKColors.Transparent);
    using var paint = new SKPaint { Color = SKColors.MediumPurple, IsAntialias = true };
    c.DrawCircle(e.Info.Width / 2f, e.Info.Height / 2f, 40, paint);
};
```

## Packages in this family

- **`Aprillz.MewUI.Skia`** (this package) - `SkiaCanvasView`, the interop registry, and the CPU fallback.
- **`Aprillz.MewUI.Skia.Interop.*`** - per-backend zero-copy GPU bridges (table above).
- **`Aprillz.MewUI.Skia.{Windows,Linux,MacOS,All}`** - metapackages bundling the core + the interops for a platform.

Full docs: https://github.com/aprillz/MewUI
