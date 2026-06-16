# Aprillz.MewUI.MewCharts

Charting for [MewUI](https://github.com/aprillz/MewUI), powered by the
[LiveChartsCore](https://github.com/beto-rodriguez/LiveCharts2) engine compiled into a pure MewUI
`IGraphicsContext` backend. **No SkiaSharp dependency** — it renders on every MewUI backend
(Direct2D, GDI, MewVG/OpenGL) across Win32, macOS, and X11.

```csharp
using Aprillz.MewUI.MewCharts.Views; // chart controls + series

var chart = new CartesianChart
{
    Series =
    [
        new LineSeries<double>(2, 1, 3, 5, 3, 4, 6),
        new LineSeries<double>(4, 2, 5, 2, 4, 5, 3),
    ],
};
// chart is a MewUI FrameworkElement: put it in any layout.
```

## Controls

| Control | For |
|---|---|
| `CartesianChart` | Line, Area, Column/Bar, Stacked, StepLine, Scatter, Financial, Heat, Box, Error |
| `PieChart` | Pie, Doughnut, Nightingale, Gauges |
| `PolarChart` | Polar line/area, radial |

Series live in `Aprillz.MewUI.MewCharts.Views`; axes/paints come from LiveChartsCore, so the
LiveCharts2 docs apply as-is. View properties are bindable `MewProperty` fields.

Bundles LiveChartsCore (MIT, v2.0.4); see `THIRD_PARTY_NOTICES.md`. GeoMap is not ported.

Repo: https://github.com/aprillz/MewUI
