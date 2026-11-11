using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Drawing.Layouts;
using Aprillz.MewUI.MewCharts.Painting;

using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Events;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Themes;
using LiveChartsCore.VisualElements;

// General/VisualElements: upstream positions, sizes and text halved, X shifted 40 px left, to fit the gallery cell.
namespace GeneralSamples;

sealed class RectangleVisual : Visual
{
    protected override RectangleGeometry DrawnElement { get; } =
        new RectangleGeometry { Fill = new SolidColorPaint(Color.Red) };

    protected override void Measure(Chart chart)
    {
        DrawnElement.X = 10;
        DrawnElement.Y = 50;
        DrawnElement.Width = 20;
        DrawnElement.Height = 20;
    }
}

sealed class ScaledRectangleVisual : Visual
{
    protected override RectangleGeometry DrawnElement { get; } =
        new RectangleGeometry { Fill = new SolidColorPaint(Color.Red) };

    protected override void Measure(Chart chart)
    {
        var cartesianChart = (ICartesianChartView)chart.View;

        var locationInDataScale = new LvcPointD(5, 5);
        var locationInPixels = cartesianChart.ScaleDataToPixels(locationInDataScale);

        DrawnElement.X = (float)locationInPixels.X;
        DrawnElement.Y = (float)locationInPixels.Y;
        DrawnElement.Width = 20;
        DrawnElement.Height = 20;
    }
}

sealed class PointerDownAwareVisual : Visual
{
    public PointerDownAwareVisual()
    {
        PointerDown += OnPointerDown;
    }

    protected override RectangleGeometry DrawnElement { get; } =
        new RectangleGeometry { Fill = new SolidColorPaint(Color.Red) };

    protected override void Measure(Chart chart)
    {
        DrawnElement.X = 35;
        DrawnElement.Y = 50;
        DrawnElement.Width = 20;
        DrawnElement.Height = 20;
    }

    private void OnPointerDown(IInteractable visual, VisualElementEventArgs visualElementsArgs)
    {
        var location = visualElementsArgs.PointerLocation;
        Trace.WriteLine($"Pointer down at {location.X}, {location.Y}");
    }
}

sealed class SvgVisual : Visual
{
    protected override SvgGeometry DrawnElement { get; } =
        new SvgGeometry
        {
            Stroke = new SolidColorPaint(Color.Blue),
            SVGPath = SVGPoints.Star,
        };

    protected override void Measure(Chart chart)
    {
        DrawnElement.X = 60;
        DrawnElement.Y = 50;
        DrawnElement.Width = 20;
        DrawnElement.Height = 20;
    }
}

sealed class CustomShape : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    public void Draw(MewDrawingContext context)
    {
        var centerX = X + Width / 2f;
        var centerY = Y + Height / 2f;
        var radius = Width / 2f;

        DrawCircle(context, centerX, centerY, radius);
        DrawCircle(context, centerX, centerY, radius * 0.75f);
        DrawCircle(context, centerX, centerY, radius * 0.5f);
    }

    private static void DrawCircle(MewDrawingContext context, float centerX, float centerY, float radius) =>
        context.DrawEllipse(new Rect(centerX - radius, centerY - radius, radius * 2, radius * 2));
}

sealed class CustomVisual : Visual
{
    protected override CustomShape DrawnElement { get; } =
        new CustomShape { Stroke = new SolidColorPaint(Color.Red) };

    protected override void Measure(Chart chart)
    {
        DrawnElement.X = 85;
        DrawnElement.Y = 50;
        DrawnElement.Width = 20;
        DrawnElement.Height = 20;
    }
}

sealed class AbsoluteVisual : Visual
{
    protected override AbsoluteLayout DrawnElement { get; } =
        new AbsoluteLayout
        {
            Children =
            [
                new RectangleGeometry
                {
                    X = 0,
                    Y = 0,
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorPaint(Color.Gray),
                },
                new LabelGeometry
                {
                    X = 5,
                    Y = 5,
                    Text = "Hello",
                    TextSize = 5,
                    HorizontalAlign = Align.Start,
                    VerticalAlign = Align.Start,
                    Paint = new SolidColorPaint(Color.Black),
                },
            ],
        };

    protected override void Measure(Chart chart)
    {
        DrawnElement.X = 110;
        DrawnElement.Y = 50;
        DrawnElement.Width = 20;
        DrawnElement.Height = 20;
    }
}

sealed class StackedVisual : Visual
{
    protected override StackLayout DrawnElement { get; } =
        new StackLayout
        {
            Orientation = ContainerOrientation.Vertical,
            Children =
            [
                new CircleGeometry
                {
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorPaint(Color.CadetBlue),
                },
                new RectangleGeometry
                {
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorPaint(Color.DeepSkyBlue),
                },
                new DiamondGeometry
                {
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorPaint(Color.LightSteelBlue),
                },
            ],
        };

    protected override void Measure(Chart chart)
    {
        DrawnElement.X = 135;
        DrawnElement.Y = 50;
    }
}

sealed class TableVisual : Visual
{
    protected override TableLayout DrawnElement { get; } =
        new TableLayout
        {
            Cells =
            [
                new(0, 0, GetLabel("A")),
                new(0, 1, GetLabel("-")),
                new(0, 2, GetLabel("B")),
                new(1, 0, GetLabel("-")),
                new(1, 1, GetLabel("C")),
                new(1, 2, GetLabel("-")),
            ],
        };

    protected override void Measure(Chart chart)
    {
        DrawnElement.X = 160;
        DrawnElement.Y = 50;
    }

    private static LabelGeometry GetLabel(string text) =>
        new()
        {
            Text = text,
            TextSize = 10,
            Padding = new Padding(5),
            Paint = new SolidColorPaint(Color.Black),
            VerticalAlign = Align.Start,
            HorizontalAlign = Align.Start,
        };
}

sealed class ContainerVisual : Visual
{
    private readonly Container<RectangleGeometry> _container;

    public ContainerVisual()
    {
        _container = new Container<RectangleGeometry>
        {
            Content = new LabelGeometry
            {
                Text = "Hello",
                TextSize = 10,
                Padding = new Padding(5),
                Paint = new SolidColorPaint(Color.Black),
                VerticalAlign = Align.Start,
                HorizontalAlign = Align.Start,
            },
        };

        _container.Geometry.Stroke = new SolidColorPaint(Color.Black, 3);
        _container.Geometry.Fill = new SolidColorPaint(Color.LightGray);
    }

    protected override Container<RectangleGeometry> DrawnElement => _container;

    protected override void Measure(Chart chart)
    {
        DrawnElement.X = 210;
        DrawnElement.Y = 50;
    }
}

sealed class ThemedVisual : Visual
{
    private static bool _isThemeRegistered;
    private object _themeId = new();

    protected override RectangleGeometry DrawnElement { get; } =
        new RectangleGeometry();

    protected override void Measure(Chart chart)
    {
        if (!_isThemeRegistered) RegisterTheme();

        ApplyTheme(chart.GetTheme());

        DrawnElement.X = 260;
        DrawnElement.Y = 50;
        DrawnElement.Width = 20;
        DrawnElement.Height = 20;
    }

    private void ApplyTheme(LiveChartsCore.Themes.Theme theme)
    {
        if (theme.ThemeId == _themeId) return;

        theme.ApplyStyleTo<ThemedVisual>(this);
        _themeId = theme.ThemeId;
    }

    private static void RegisterTheme()
    {
        var theme = LiveCharts.DefaultSettings.GetTheme();

        theme.HasRuleFor<ThemedVisual>(visual =>
        {
            visual.DrawnElement.Fill =
                theme.IsDark
                    ? new SolidColorPaint(Color.White)
                    : new SolidColorPaint(Color.Black);
        });

        _isThemeRegistered = true;
    }
}
