#:sdk Microsoft.NET.Sdk

#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows
#:property UseWPF=True
#:property UseWindowsForms=False

#:property PublishAot=False

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

var app = new Application();
app.Run(CreateWindow());

static Window CreateWindow()
{
    var items = Enumerable.Range(0, 20)
        .Select(i =>
        {
            var b = new Border
            {
                Width = 130,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Color.FromArgb(40, 220, 120, 20)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                Child = new TextBlock { Text = $"Tile {i:00}", VerticalAlignment = VerticalAlignment.Center }
            };
            return (UIElement)b;
        })
        .ToArray();

    var stack = new StackPanel { Orientation = Orientation.Horizontal };
    foreach (var item in items) stack.Children.Add(item);

    var grid = new Grid { ShowGridLines = true };
    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    grid.Children.Add(stack);

    var sv = new ScrollViewer
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = grid
    };

    return new Window
    {
        Title = "WPF Grid HScroll Test",
        Width = 520,
        Height = 200,
        Content = sv
    };
}
