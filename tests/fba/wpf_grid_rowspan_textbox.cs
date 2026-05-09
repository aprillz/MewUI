#:sdk Microsoft.NET.Sdk

#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows
#:property UseWPF=True
#:property UseWindowsForms=False

// WPF cannot use AOT compilation.
#:property PublishAot=False

using System.Windows;
using System.Windows.Controls;

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

var app = new Application();
app.Run(CreateWindow());

static Window CreateWindow()
{
    return new Window
    {
        SizeToContent = SizeToContent.Height,
        Title = "WPF Grid (*,* / Auto,Auto) — RowSpan TextBox",
        Width = 600,
        Height = 400,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Content = BuildTabs(),
    };
}

static UIElement BuildTabs()
{
    var tab = new TabControl();
    tab.VerticalContentAlignment = VerticalAlignment.Stretch;
    tab.VerticalAlignment = VerticalAlignment.Top;
    tab.Items.Add(new TabItem
    {
        Header = "Fixed height (no ScrollViewer)",
        Content = BuildGrid(),
    });

    tab.Items.Add(new TabItem
    {
        Header = "Inside ScrollViewer",
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = BuildGrid(),
        },
    });

    return tab;
}

static UIElement BuildGrid()
{
    var grid = new Grid { Margin = new Thickness(12) };

    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

    var gb1 = new GroupBox
    {
        Header = "Group 1 (Height=120)",
        Height = 120,
        Margin = new Thickness(0, 0, 6, 6),
        Content = new TextBlock { Text = "고정 높이 120", Margin = new Thickness(6) },
    };
    Grid.SetRow(gb1, 0);
    Grid.SetColumn(gb1, 0);
    grid.Children.Add(gb1);

    var gb2 = new GroupBox
    {
        Header = "Group 2 (Height=140)",
        Height = 140,
        Margin = new Thickness(0, 0, 6, 0),
        Content = new TextBlock { Text = "고정 높이 140", Margin = new Thickness(6) },
    };
    Grid.SetRow(gb2, 1);
    Grid.SetColumn(gb2, 0);
    grid.Children.Add(gb2);

    var textBox = new TextBox
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(6, 0, 0, 0),
        Text = string.Join('\n', Enumerable.Range(1, 60).Select(i => $"Line {i}: lorem ipsum dolor sit amet.")),
    };
    Grid.SetRow(textBox, 0);
    Grid.SetColumn(textBox, 1);
    Grid.SetRowSpan(textBox, 3);
    grid.Children.Add(textBox);

    return grid;
}
