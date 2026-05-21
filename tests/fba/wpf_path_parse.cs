#:sdk Microsoft.NET.Sdk

#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows
#:property UseWPF=True
#:property UseWindowsForms=False

// WPF cannot use AOT compilation.
#:property PublishAot=False

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

var app = new Application();
app.Run(CreateWindow());

static Window CreateWindow()
{
    return new Window
    {
        Title = "WPF Path Parse Test (Lucide a-arrow-down, issue #135)",
        Width = 720,
        Height = 480,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Content = BuildContent(),
    };
}

static UIElement BuildContent()
{
    // Lucide a-arrow-down combined path from issue #135
    const string PathData = "m14 12 4 4 4-4 M18 16V7 m2 16 4.039-9.69a.5.5 0 0 1 .923 0L11 16 M3.304 13h6.392";

    var stroke = new SolidColorBrush(Color.FromRgb(70, 130, 230));

    var root = new StackPanel
    {
        Orientation = Orientation.Vertical,
        Margin = new Thickness(24),
    };

    root.Children.Add(new TextBlock
    {
        Text = "Lucide a-arrow-down — combined SVG path data",
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 0, 4),
    });

    root.Children.Add(new TextBlock
    {
        Text = PathData,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
        Margin = new Thickness(0, 0, 0, 16),
    });

    // Row of size variants — same path data, different render boxes. Helps spot stretch /
    // bounding-box / parser-survival differences across scales.
    var row = new StackPanel
    {
        Orientation = Orientation.Horizontal,
    };

    foreach (var size in new[] { 64.0, 128.0, 256.0 })
    {
        row.Children.Add(LabeledPath($"{size:0}×{size:0}", PathData, stroke, size));
    }

    root.Children.Add(row);

    return root;
}

static UIElement LabeledPath(string label, string data, Brush stroke, double size)
{
    var path = new Path
    {
        Data = Geometry.Parse(data),
        Stroke = stroke,
        StrokeThickness = 2,
        Stretch = Stretch.Uniform,
        Width = size,
        Height = size,
    };

    var frame = new Border
    {
        BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
        BorderThickness = new Thickness(1),
        Child = path,
    };

    return new StackPanel
    {
        Orientation = Orientation.Vertical,
        Margin = new Thickness(0, 0, 16, 0),
        Children =
        {
            frame,
            new TextBlock
            {
                Text = label,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            },
        },
    };
}
