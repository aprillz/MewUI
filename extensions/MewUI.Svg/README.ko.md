# Aprillz.MewUI.Svg

MewUI의 `IGraphicsContext`로 그리는 [MewUI](https://github.com/aprillz/MewUI)용 SVG 파싱/렌더링 확장입니다.

**순수 관리 코드, System.Drawing 비의존.** [SVG.NET](https://github.com/svg-net/SVG)을 기반으로 하되 `System.Drawing` 렌더링을 MewUI `IGraphicsContext` 백엔드로 교체했습니다. 따라서 모든 MewUI 백엔드(Direct2D, GDI, MewVG/OpenGL)에서 SVG를 파싱/렌더링하며 NativeAOT/트리밍 호환입니다.

## 설치

```
dotnet add package Aprillz.MewUI.Svg
```

`net8.0`, `net10.0`을 대상으로 합니다.

## 빠른 시작

`SvgDocument`를 로드해 요소의 `OnRender`에서 렌더링합니다:

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

// 사용
var view = new SvgView { Document = SvgDocument.Open("icon.svg") };
```

## 로딩

`SvgDocument`(네임스페이스 `Svg`)는 파일, 스트림, 문자열에서 파싱합니다:

| 메서드 | 소스 |
|---|---|
| `SvgDocument.Open(path)` | 파일 경로 |
| `SvgDocument.Load(path)` | 파일 경로 (MewUI 오버로드) |
| `SvgDocument.Parse(svg)` | SVG 마크업 문자열 |
| `SvgDocument.Parse(svg, baseUri)` | 마크업 + 상대 참조용 base URI |

## 렌더링

```csharp
document.Render(IGraphicsContext context, Rect destRect);
```

문서가 `destRect`에 맞춰 스케일됩니다. 비율을 유지하려면 `document.ViewBoxWidth` / `document.ViewBoxHeight`로 `destRect`를 계산하세요. 더 저수준이 필요하면 `ISvgRenderer`(예: `MewSvgRenderer`)를 구현해 `document.Draw(renderer)`를 호출합니다.

## 백엔드

MewUI.Svg는 `IGraphicsContext`만 사용하므로 호스트 앱이 등록한 백엔드 위에서 렌더링됩니다. Direct2D와 MewVG(OpenGL)가 곡선/텍스트 품질이 가장 좋고, GDI도 동작하지만 곡선/텍스트 안티앨리어싱 품질이 떨어집니다.

## 소스에서 빌드

SVG.NET은 git submodule로 가져와 `System.Drawing` 비의존 소스만 이 어셈블리에 컴파일됩니다. 빌드 전에 submodule을 초기화하세요:

```
git submodule update --init --recursive
dotnet build extensions/MewUI.Svg/MewUI.Svg.csproj
```

submodule(`ThirdParty/SvgNet`)은 원본 그대로 유지됩니다. `*.Drawing.cs` 렌더링 partial은 제외되고 이 프로젝트의 MewUI `*.MewUI.cs` partial로 대체됩니다.

## 의존성

`Aprillz.MewUI`와 [ExCSS](https://github.com/TylerBrinks/ExCSS)(CSS 스타일 파싱). SVG.NET 자체는 컴파일되어 들어가므로 패키지 참조가 아닙니다.

## 라이선스

`Aprillz.MewUI.Svg`는 MIT입니다. [SVG.NET](https://github.com/svg-net/SVG)(Microsoft Public License, Ms-PL) 사본을 포함하며, `THIRD_PARTY_NOTICES.md`를 참고하세요. System.Drawing은 사용하거나 재배포하지 않습니다.
