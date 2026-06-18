# Aprillz.MewUI.Skia

[MewUI](https://github.com/aprillz/MewUI)용 Skia(SkiaSharp) 렌더링 통합입니다. MewUI 창 안에서 SkiaSharp로 직접 그릴 수 있는 `SkiaCanvasView` 컨트롤과 `ISkiaInteropProvider` 레지스트리를 제공합니다.

- 어떤 MewUI 백엔드에서도 바로 동작하는 **CPU 업로드 폴백**을 기본 포함합니다.
- 사용하는 백엔드에 맞는 **`Aprillz.MewUI.Skia.Interop.*`** 패키지를 설치하면 **zero-copy GPU** 경로를 쓸 수 있습니다.

## 설치

```
dotnet add package Aprillz.MewUI.Skia
```

그런 다음 실행 백엔드에 맞는 interop 패키지를 추가합니다(선택 — 없으면 CPU 폴백 사용):

| Interop 패키지 | 백엔드 | 시작 시 활성화 |
|---|---|---|
| `Aprillz.MewUI.Skia.Interop.Direct2D` | Direct2D (Windows) | `SkiaDirect2DInterop.Use()` |
| `Aprillz.MewUI.Skia.Interop.Gdi` | GDI (Windows) | `SkiaGdiInterop.Register()` |
| `Aprillz.MewUI.Skia.Interop.MewVG.Win32` | MewVG / Win32 | `SkiaMewVGWin32Interop.Use()` |
| `Aprillz.MewUI.Skia.Interop.MewVG.X11` | MewVG / X11 (Linux) | `SkiaMewVGX11Interop.Use()` |
| `Aprillz.MewUI.Skia.Interop.MewVG.MacOS` | MewVG / macOS | `SkiaMewVGMacOSInterop.Use()` |

각 interop은 Skia와 해당 백엔드를 연결해 Skia 표면을 프레임마다 복사 없이 소비합니다. 백엔드 등록 이후 시작 시 한 번 등록하세요. interop이 등록되지 않으면 `SkiaCanvasView`는 CPU 업로드 폴백으로 동작합니다(정확하지만 zero-copy는 아님).

## 사용법

```csharp
// 시작: 백엔드 등록 후 (선택적으로) 맞는 Skia interop 등록
Direct2DBackend.Register();
SkiaDirect2DInterop.Use();

// MewUI 안에서 SkiaSharp로 그리기
var canvas = new SkiaCanvasView();
canvas.PaintSurface += (s, e) =>
{
    var c = e.Surface.Canvas;
    c.Clear(SKColors.Transparent);
    using var paint = new SKPaint { Color = SKColors.MediumPurple, IsAntialias = true };
    c.DrawCircle(e.Info.Width / 2f, e.Info.Height / 2f, 40, paint);
};
```

## 패키지 구성

- **`Aprillz.MewUI.Skia`** (이 패키지) — `SkiaCanvasView`, interop 레지스트리, CPU 폴백.
- **`Aprillz.MewUI.Skia.Interop.*`** — 백엔드별 zero-copy GPU 브리지(위 표).
- **`Aprillz.MewUI.Skia.{Windows,Linux,MacOS,All}`** — 코어 + 플랫폼별 interop을 묶은 메타패키지.

전체 문서: https://github.com/aprillz/MewUI
