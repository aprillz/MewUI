# Aprillz.MewUI.Svg

MewUI의 `IGraphicsContext`로 그리는 [MewUI](https://github.com/aprillz/MewUI)용 SVG 파싱/렌더링 확장입니다.

**순수 관리 코드, System.Drawing 비의존.** [SVG.NET](https://github.com/svg-net/SVG)을 기반으로 하되 `System.Drawing` 렌더링을 MewUI `IGraphicsContext` 백엔드로 교체했습니다. 따라서 모든 MewUI 백엔드(Direct2D, GDI, MewVG/OpenGL)에서 SVG를 파싱/렌더링하며 NativeAOT/트리밍 호환입니다.

## 설치

```
dotnet add package Aprillz.MewUI.Svg
```

`net8.0`, `net10.0`을 대상으로 합니다.

## 빠른 시작

`SvgImageSource`는 표준 `Image` 컨트롤에 그대로 꽂혀, SVG를 일반 이미지처럼 표시하면서 **어떤 크기에서도 선명**합니다(비트맵을 늘리는 게 아니라 레이아웃 크기로 다시 그림):

```csharp
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Svg;

var image = new Image
{
    Source = SvgImageSource.FromFile("icon.svg"),
    StretchMode = Stretch.Uniform,
}.Size(24, 24);
```

`SvgImageSource`는 `IVectorImageSource`라, `Image`가 매 페인트마다 현재 크기로 벡터를 그립니다. 리사이즈/고DPI에서도 선명합니다.

## 로딩

```csharp
SvgImageSource.FromFile(path);                 // 파일 경로
SvgImageSource.FromString(svgMarkup);          // SVG 마크업
SvgImageSource.FromStream(stream);             // 임의 스트림 (예: ZIP 엔트리)
SvgImageSource.FromResource(assembly, name);   // 임베드 리소스
```

## 재색칠 (단색 아이콘)

`Tint`를 설정하면 fill이 상속되는(요소별 명시 fill이 없는) 아이콘을 재색칠합니다. 변경 시 호스트 컨트롤이 자동으로 다시 그립니다:

```csharp
var source = SvgImageSource.FromFile("home.svg");
source.Tint = theme.Foreground;
new Image { Source = source };
```

- `IntrinsicSize` — SVG viewBox 크기(`Image` 측정에 사용).
- `RasterWidth` / `RasterHeight` — `CreateImage` 래스터 폴백(픽셀이 필요한 소비자용)에만 영향. 벡터 `Image` 경로는 무시.

## 고급: SvgDocument

문서에 직접 접근하려면 SVG.NET 엔진 타입 `SvgDocument`(네임스페이스 `Svg`)를 쓸 수 있습니다:

```csharp
using Svg;

var doc = SvgDocument.Open("icon.svg");   // 또는 .Parse(markup) / .Parse(markup, baseUri)
doc.Render(context, new Rect(0, 0, width, height));   // IGraphicsContext에 그리기
```

비율 유지에는 `doc.ViewBoxWidth` / `doc.ViewBoxHeight`. 대부분의 경우 `SvgImageSource` + `Image`를 권장합니다.

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
