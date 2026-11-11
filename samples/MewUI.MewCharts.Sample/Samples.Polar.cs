using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // Polar/Basic
    internal static FrameworkElement PolarBasic() => new PolarChart
    {
        Title = new LabelVisual
        {
            Text = "My chart title",
            Paint = new SolidColorPaint(new Color(48, 48, 48)),
            TextSize = 25,
            Padding = new Padding(15),
        },
        Series =
        [
            new PolarLineSeries<double>(15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1)
            {
                ShowDataLabels = true,
                GeometrySize = 15,
                DataLabelsSize = 8,
                DataLabelsPosition = PolarLabelsPosition.Middle,
                DataLabelsRotation = LiveCharts.CotangentAngle,
                IsClosed = true,
            },
        ],
        RadiusAxes = [new PolarAxis { LabelsAngle = -60, MaxLimit = 30 }],
        AngleAxes = [new PolarAxis { LabelsRotation = LiveCharts.TangentAngle }],
    };

    // Polar/RadialArea
    internal static FrameworkElement PolarRadialArea() => new PolarChart
    {
        InitialRotation = -45,
        Series =
        [
            new PolarLineSeries<int>(7, 5, 7, 5, 6)
            {
                LineSmoothness = 0,
                GeometrySize = 0,
                Fill = new SolidColorPaint(new Color(144, 0, 0, 255)),
            },
            new PolarLineSeries<int>(2, 7, 5, 9, 7)
            {
                LineSmoothness = 1,
                GeometrySize = 0,
                Fill = new SolidColorPaint(new Color(144, 255, 0, 0)),
            },
        ],
        AngleAxes =
        [
            new PolarAxis
            {
                LabelsRotation = LiveCharts.TangentAngle,
                Labels = ["first", "second", "third", "forth", "fifth"],
            },
        ],
    };

    // Polar/Coordinates
    internal static FrameworkElement PolarCoordinates() => new PolarChart
    {
        Series =
        [
            new PolarLineSeries<ObservablePolarPoint>(new ObservablePolarPoint[]
            {
                new(0, 10),
                new(45, 15),
                new(90, 20),
                new(135, 25),
                new(180, 30),
                new(225, 35),
                new(270, 40),
                new(315, 45),
                new(360, 50),
            })
            {
                IsClosed = true,
                Fill = null,
            },
        ],
        AngleAxes =
        [
            new PolarAxis
            {
                MinLimit = 0,
                MaxLimit = 360,
                Labeler = angle => $"{angle}°",
                ForceStepToMin = true,
                MinStep = 30,
            },
        ],
    };

    // Polar/Test: sliders and a check box drive the chart and the radial axis.
    internal static FrameworkElement PolarTest()
    {
        var radialAxis = new PolarAxis
        {
            LabelsAngle = -60,
            MaxLimit = 30,
        };

        var chart = new PolarChart
        {
            Series = new ObservableCollection<ISeries>
            {
                new PolarLineSeries<double>
                {
                    Values = new ObservableCollection<double> { 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 },
                    ShowDataLabels = true,
                    GeometrySize = 30,
                    DataLabelsSize = 15,
                    DataLabelsPosition = PolarLabelsPosition.Middle,
                    DataLabelsRotation = LiveCharts.CotangentAngle,
                    IsClosed = true,
                },
            },
            RadiusAxes = [radialAxis],
            AngleAxes = [new PolarAxis { LabelsRotation = LiveCharts.TangentAngle }],
            FitToBounds = false,
            InitialRotation = 15,
            InnerRadius = 50,
            TotalAngle = 360,
        };

        var fitToBounds = new CheckBox().Content("FitToBounds").OnCheckedChanged(value => chart.FitToBounds = value);
        fitToBounds.FontSize = 11;

        return WithControls(chart,
            PolarSlider("Rotation", -360, 720, 15, value => chart.InitialRotation = value),
            PolarSlider("Inner radius", 0, 150, 50, value => chart.InnerRadius = value),
            PolarSlider("Total angle", 30, 360, 360, value => chart.TotalAngle = value),
            // The upstream caption is "Radial axis labels angle".
            PolarSlider("Labels angle", 0, 360, -60, value => radialAxis.LabelsAngle = value),
            fitToBounds);
    }

    // A caption, the current value and a slider that snaps to multiples of 5 like the upstream tick frequency.
    private static FrameworkElement PolarSlider(string caption, double minimum, double maximum, double initial, Action<double> onChanged)
    {
        var captionText = new TextBlock().Text(caption);
        captionText.FontSize = 11;

        var valueText = new TextBlock().Text(initial.ToString("0")).Width(28);
        valueText.FontSize = 11;

        var slider = new Slider().Minimum(minimum).Maximum(maximum).Value(initial).Width(44);
        slider.ValueChanged += value =>
        {
            var snapped = Math.Round(value / 5) * 5;
            if (snapped != value)
            {
                slider.Value = snapped;
                return;
            }

            valueText.Text = snapped.ToString("0");
            onChanged(snapped);
        };

        return new StackPanel().Horizontal().Spacing(4).Children(captionText, valueText, slider);
    }
}
