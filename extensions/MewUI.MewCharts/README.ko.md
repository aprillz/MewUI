# Aprillz.MewUI.MewCharts

[LiveChartsCore](https://github.com/beto-rodriguez/LiveCharts2)엔진을 순수 MewUI `IGraphicsContext` 백엔드로 컴파일해 동작하는 [MewUI](https://github.com/aprillz/MewUI)용 차트 확장입니다.

**SkiaSharp 비의존.** LiveChartsCore 엔진(v2.0.4, MIT)을 이 어셈블리에 직접 컴파일해 넣고 MewUI 자체 그래픽 추상화로 그리므로, Win32/macOS/X11 전반의 모든 MewUI 백엔드(Direct2D, GDI, MewVG/OpenGL)에서 렌더링됩니다. 런타임 의존성은 `Aprillz.MewUI` 하나뿐입니다.

## 설치

```
dotnet add package Aprillz.MewUI.MewCharts
```

`net8.0`, `net10.0`을 대상으로 합니다.

## 빠른 시작

```csharp
using Aprillz.MewUI.MewCharts.Views; // 차트 컨트롤 + 시리즈 (CartesianChart, LineSeries<T>, ...)

var chart = new CartesianChart
{
    Series =
    [
        new LineSeries<double>(2, 1, 3, 5, 3, 4, 6),
        new LineSeries<double>(4, 2, 5, 2, 4, 5, 3),
    ],
};

// chart는 MewUI FrameworkElement이므로 어떤 레이아웃에든 넣을 수 있습니다.
window.Content = chart;
```

차트는 최초 사용 시 MewUI 엔진을 자동 초기화하므로 별도 설정 호출이 필요 없습니다. 테마나 기본값을 미리 구성하려면 시작 시 `LiveChartsMewUI.EnsureInitialized()`를 한 번 호출하면 됩니다.

## 차트 컨트롤

셋 다 `Aprillz.MewUI.MewCharts.Views`에 있으며 `ChartViewBase`(MewUI `Control`)를 상속합니다:

| 컨트롤 | 인터페이스 | 용도 |
|---|---|---|
| `CartesianChart` | `ICartesianChartView` | Line, Area, Column/Bar, Stacked, StepLine, Scatter, Financial, Heat, Box, Error |
| `PieChart` | `IPieChartView` | Pie, Doughnut, Nightingale, 게이지 |
| `PolarChart` | `IPolarChartView` | Polar line/area, radial |

편의 시리즈 타입(`LineSeries<T>`, `ColumnSeries<T>`, `PieSeries<T>`, ...)은 `Aprillz.MewUI.MewCharts.Views`에 있으며 MewUI 기본 geometry/paint를 연결합니다(공식 SkiaSharp 패키지가 하던 역할). 축, 섹션, 페인트, 비주얼 요소는 LiveChartsCore에서 그대로 가져옵니다(`Axis`, `SolidColorPaint`, ...). 따라서 LiveCharts2 문서와 샘플이 그대로 적용됩니다.

## 바인딩

차트의 뷰 속성(Series, 축, 제목, 범례/툴팁 페인트와 위치, 줌 모드, 애니메이션 속도, 테마 등)은 다른 MewUI 속성과 동일하게 바인딩 가능합니다. 각 속성 `X`에는 `Bind`/`SetBinding`에 넘길 수 있는 `XProperty` 필드가 있습니다:

```csharp
chart.Bind(ChartViewBase.SeriesProperty, source, SourceType.SeriesProperty);
```

## 백엔드

MewCharts는 `IGraphicsContext`만 사용하므로 호스트 앱이 등록한 백엔드 위에서 렌더링됩니다. Direct2D와 MewVG(OpenGL)가 곡선/텍스트 품질이 가장 좋고, GDI도 동작하지만 곡선/텍스트 안티앨리어싱 품질이 떨어집니다.

## 소스에서 빌드

LiveChartsCore는 git submodule로 가져와 이 어셈블리에 컴파일되므로, 빌드 전에 submodule을 초기화해야 합니다:

```
git submodule update --init --recursive
dotnet build extensions/MewUI.MewCharts/MewUI.MewCharts.csproj
```

submodule(`ThirdParty/LiveCharts2`)은 태그 `2.0.4`에 고정되며 원본 그대로 유지됩니다. 모든 MewUI 백엔드 코드는 submodule 밖에 있습니다.

## 샘플

`samples/MewUI.MewCharts.Sample`은 공식 LiveCharts2 샘플(~75개 섹션)을 충실히 옮긴 갤러리로, 전부 MewCharts로 렌더링됩니다. `--gdi` 또는 `--vg` 인자로 백엔드를 전환할 수 있습니다.

## 알려진 차이/제한

- GDI 백엔드는 Direct2D/MewVG보다 곡선/텍스트 안티앨리어싱 품질이 낮습니다.
- 임의 형상 drop shadow는 근사로 처리됩니다(box-shadow는 사각형 기준).
- 텍스트 메트릭이 SkiaSharp와 미세하게 달라, 일부 회전/멀티라인 라벨이 약간 어긋날 수 있습니다.
- GeoMap(지도)은 포팅되지 않았습니다.

## 라이선스

`Aprillz.MewUI.MewCharts`는 MIT입니다. [beto-rodriguez/LiveCharts2](https://github.com/beto-rodriguez/LiveCharts2)의 LiveChartsCore 엔진(MIT)을 포함하며, `THIRD_PARTY_NOTICES.md`를 참고하세요. SkiaSharp 드로잉 백엔드는 사용하거나 재배포하지 않습니다.
