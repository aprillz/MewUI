#:sdk Microsoft.NET.Sdk

#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows
#:property UseWPF=True
#:property UseWindowsForms=False

// WPF cannot use AOT compilation.
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
    return new Window
    {
        Title = "WPF Grid (Span) Test",
        Width = 400,
        Height = 300,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Content = new Border
        {
            Margin = new Thickness(24),
            Padding = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = BuildGridSpanCard(),
        },
    };
}

static UIElement BuildGridSpanCard()
{
    var panel = new StackPanel { Orientation = Orientation.Vertical };

    panel.Children.Add(new TextBlock
    {
        Text = "Grid (Span)",
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 0, 8),
    });

    var grid = new Grid();

    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    // Row 0: ColSpan 2 button + R1C1
    var colSpan2 = Btn("ColSpan 2");
    Grid.SetRow(colSpan2, 0);
    Grid.SetColumn(colSpan2, 0);
    Grid.SetColumnSpan(colSpan2, 2);
    grid.Children.Add(colSpan2);

    var r1c1 = Btn("R1C1");
    Grid.SetRow(r1c1, 0);
    Grid.SetColumn(r1c1, 2);
    grid.Children.Add(r1c1);

    // Row 1-2: RowSpan 2 button + R1C2 + R1C2
    var rowSpan2 = Btn("RowSpan 2");
    Grid.SetRow(rowSpan2, 1);
    Grid.SetColumn(rowSpan2, 0);
    Grid.SetRowSpan(rowSpan2, 2);
    grid.Children.Add(rowSpan2);

    var r1c2a = Btn("R1C2");
    Grid.SetRow(r1c2a, 1);
    Grid.SetColumn(r1c2a, 1);
    grid.Children.Add(r1c2a);

    var r1c2b = Btn("R1C2");
    Grid.SetRow(r1c2b, 1);
    Grid.SetColumn(r1c2b, 2);
    grid.Children.Add(r1c2b);

    // Row 2: R2C1 + R2C2
    var r2c1 = Btn("R2C1");
    Grid.SetRow(r2c1, 2);
    Grid.SetColumn(r2c1, 1);
    grid.Children.Add(r2c1);

    var r2c2 = Btn("R2C2");
    Grid.SetRow(r2c2, 2);
    Grid.SetColumn(r2c2, 2);
    grid.Children.Add(r2c2);

    panel.Children.Add(grid);
    return panel;
}

static Button Btn(string text) => new Button
{
    Content = text,
    Margin = new Thickness(3),
    Padding = new Thickness(8, 4, 8, 4),
};
