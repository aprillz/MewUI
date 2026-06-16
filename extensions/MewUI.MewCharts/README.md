# Aprillz.MewUI.MewCharts

Charting for [MewUI](https://github.com/aprillz/MewUI), powered by the [LiveChartsCore](https://github.com/beto-rodriguez/LiveCharts2) engine compiled into a pure MewUI `IGraphicsContext` backend.

**No SkiaSharp dependency.** The LiveChartsCore engine (v2.0.4, MIT) is compiled directly into this assembly and drawn through MewUI's own graphics abstraction, so it renders on every MewUI backend (Direct2D, GDI, MewVG/OpenGL) across Win32, macOS, and X11. The only runtime dependency is `Aprillz.MewUI` itself.

## Install

```
dotnet add package Aprillz.MewUI.MewCharts
```

Targets `net8.0` and `net10.0`.

## Quick start

```csharp
using Aprillz.MewUI.MewCharts.Views; // chart controls + series (CartesianChart, LineSeries<T>, ...)

var chart = new CartesianChart
{
    Series =
    [
        new LineSeries<double>(2, 1, 3, 5, 3, 4, 6),
        new LineSeries<double>(4, 2, 5, 2, 4, 5, 3),
    ],
};

// chart is a MewUI FrameworkElement: put it in any layout.
window.Content = chart;
```

The chart auto-initializes the MewUI engine on first use; no explicit setup call is required. If you want to configure themes or defaults up front, call `LiveChartsMewUI.EnsureInitialized()` once at startup.

## Chart controls

All three live in `Aprillz.MewUI.MewCharts.Views` and derive from `ChartViewBase` (a MewUI `Control`):

| Control | Interface | Use |
|---|---|---|
| `CartesianChart` | `ICartesianChartView` | Line, Area, Column/Bar, Stacked, StepLine, Scatter, Financial, Heat, Box, Error |
| `PieChart` | `IPieChartView` | Pie, Doughnut, Nightingale, Gauges |
| `PolarChart` | `IPolarChartView` | Polar line/area, radial |

The convenience series types (`LineSeries<T>`, `ColumnSeries<T>`, `PieSeries<T>`, ...) live in `Aprillz.MewUI.MewCharts.Views` and wire in MewUI default geometries/paints, mirroring what the official SkiaSharp package provides. Axes, sections, paints, and visual elements come from LiveChartsCore directly (`Axis`, `SolidColorPaint`, ...), so the LiveCharts2 docs and samples apply as-is.

## Binding

The chart's view properties (Series, axes, title, legend/tooltip paints and positions, zoom mode, animation speed, theme, and more) are bindable like any other MewUI property. Each property `X` has a `XProperty` field you can pass to `Bind`/`SetBinding`:

```csharp
chart.Bind(ChartViewBase.SeriesProperty, source, SourceType.SeriesProperty);
```

## Backends

MewCharts only uses `IGraphicsContext`, so it renders on whichever backend the host app registers. Direct2D and MewVG (OpenGL) give the best curve/text quality; GDI works but anti-aliases curves and text less smoothly.

## Building from source

LiveChartsCore is brought in as a git submodule and compiled into this assembly, so initialize submodules before building:

```
git submodule update --init --recursive
dotnet build extensions/MewUI.MewCharts/MewUI.MewCharts.csproj
```

The submodule (`ThirdParty/LiveCharts2`) is pinned to tag `2.0.4` and is kept pristine; all MewUI backend code lives outside it.

## Samples

`samples/MewUI.MewCharts.Sample` is a gallery faithful to the official LiveCharts2 samples (~75 sections), rendered entirely through MewCharts. Run it with `--gdi` or `--vg` to switch backends.

## Known differences / limits

- GDI backend has lower curve/text anti-aliasing quality than Direct2D/MewVG.
- Arbitrary-shape drop shadows are approximated (box-shadow is rectangle-oriented).
- Text metrics differ slightly from SkiaSharp, so some rotated/multi-line labels can shift a little.
- GeoMap (maps) is not ported.

## License

`Aprillz.MewUI.MewCharts` is MIT. It includes the LiveChartsCore engine (MIT) from [beto-rodriguez/LiveCharts2](https://github.com/beto-rodriguez/LiveCharts2); see `THIRD_PARTY_NOTICES.md`. The SkiaSharp drawing backend is not used or redistributed.
