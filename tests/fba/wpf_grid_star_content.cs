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

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

var app = new Application();
app.Run(CreateWindow());

static Window CreateWindow()
{
    return new Window
    {
        Title = "WPF Grid Star Content-Sizing Reference",
        Width = 900,
        Height = 700,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildContent(),
        },
    };
}

static UIElement BuildContent()
{
    var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(16) };

    root.Children.Add(Title("Grid Star Sizing — content with different widths"));
    root.Children.Add(Note("Columns: Auto,*,Auto,*,Auto,*  TextBox texts: \"가\" / \"가나\" / \"가나다\""));
    root.Children.Add(Note("Question: do the three Star columns size to fit each (or the max) TextBox?"));

    // Case 1: pure Star (no MinWidth) — observe WPF default behavior
    root.Children.Add(SectionLabel("1. Pure Star — no MinWidth   [Auto,*,Auto,*,Auto,*]"));
    root.Children.Add(BuildGrid(useMinWidth: false, useSharedSize: false));

    // Case 2: Star + MinWidth = content desired
    root.Children.Add(SectionLabel("2. Star + MinWidth=80 (manual)   [Auto,*(min80),Auto,*(min80),Auto,*(min80)]"));
    root.Children.Add(BuildGrid(useMinWidth: true, useSharedSize: false));

    // Case 3: SharedSizeGroup
    root.Children.Add(SectionLabel("3. SharedSizeGroup=\"inputs\" (Star + group)   [Auto,*,Auto,*,Auto,*]"));
    var sharedHost = new Grid();
    Grid.SetIsSharedSizeScope(sharedHost, true);
    sharedHost.Children.Add(BuildGrid(useMinWidth: false, useSharedSize: true));
    root.Children.Add(sharedHost);

    // Case 4: All-Auto baseline — content fits exactly per column
    root.Children.Add(SectionLabel("4. All Auto (baseline)   [Auto,Auto,Auto,Auto,Auto,Auto]"));
    root.Children.Add(BuildGrid(useAuto: true));

    // Case 5: Pure Star + HorizontalAlignment=Left
    root.Children.Add(SectionLabel("5. Pure Star + HorizontalAlignment=Left   [Auto,*,Auto,*,Auto,*]"));
    root.Children.Add(BuildGrid(useMinWidth: false, useSharedSize: false, alignLeft: true));

    // Case 6: Star + MinWidth=80 + HorizontalAlignment=Left
    root.Children.Add(SectionLabel("6. Star + MinWidth=80 + HorizontalAlignment=Left   [Auto,*(min80),Auto,*(min80),Auto,*(min80)]"));
    root.Children.Add(BuildGrid(useMinWidth: true, useSharedSize: false, alignLeft: true));

    return root;
}

static UIElement BuildGrid(bool useMinWidth = false, bool useSharedSize = false, bool useAuto = false, bool alignLeft = false)
{
    var grid = new Grid
    {
        Margin = new Thickness(0, 4, 0, 16),
        ShowGridLines = true,
        HorizontalAlignment = alignLeft ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
    };

    GridLength colA = useAuto ? GridLength.Auto : new GridLength(1, GridUnitType.Star);

    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(MakeStar(colA, useMinWidth, useSharedSize));
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(MakeStar(colA, useMinWidth, useSharedSize));
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(MakeStar(colA, useMinWidth, useSharedSize));

    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    // Row 0: labels A, B, C with single-char TextBox content
    AddCell(grid, 0, 0, MakeLabel("A:"));
    AddCell(grid, 0, 1, MakeBox("가"));
    AddCell(grid, 0, 2, MakeLabel("B:"));
    AddCell(grid, 0, 3, MakeBox("가나"));
    AddCell(grid, 0, 4, MakeLabel("C:"));
    AddCell(grid, 0, 5, MakeBox("가나다"));

    // Row 1: single-char content
    AddCell(grid, 1, 0, MakeLabel("A:"));
    AddCell(grid, 1, 1, MakeBox("가"));
    AddCell(grid, 1, 2, MakeLabel("B:"));
    AddCell(grid, 1, 3, MakeBox("가"));
    AddCell(grid, 1, 4, MakeLabel("C:"));
    AddCell(grid, 1, 5, MakeBox("가"));

    // Row 2: single-char content
    AddCell(grid, 2, 0, MakeLabel("A:"));
    AddCell(grid, 2, 1, MakeBox("가"));
    AddCell(grid, 2, 2, MakeLabel("B:"));
    AddCell(grid, 2, 3, MakeBox("가"));
    AddCell(grid, 2, 4, MakeLabel("C:"));
    AddCell(grid, 2, 5, MakeBox("가"));

    return grid;
}

static ColumnDefinition MakeStar(GridLength width, bool useMinWidth, bool useSharedSize)
{
    var col = new ColumnDefinition { Width = width };
    if (useMinWidth) col.MinWidth = 80;
    if (useSharedSize) col.SharedSizeGroup = "inputs";
    return col;
}

static void AddCell(Grid grid, int row, int col, UIElement element)
{
    Grid.SetRow(element, row);
    Grid.SetColumn(element, col);
    grid.Children.Add(element);
}

static TextBlock MakeLabel(string text) => new TextBlock
{
    Text = text,
    VerticalAlignment = VerticalAlignment.Center,
    Margin = new Thickness(8, 4, 4, 4),
};

static TextBox MakeBox(string text) => new TextBox
{
    Text = text,
    Margin = new Thickness(2),
    Padding = new Thickness(4, 2, 4, 2),
};

static TextBlock Title(string text) => new TextBlock
{
    Text = text,
    FontSize = 16,
    FontWeight = FontWeights.Bold,
    Margin = new Thickness(0, 0, 0, 8),
};

static TextBlock SectionLabel(string text) => new TextBlock
{
    Text = text,
    FontSize = 13,
    FontWeight = FontWeights.SemiBold,
    Margin = new Thickness(0, 12, 0, 4),
};

static TextBlock Note(string text) => new TextBlock
{
    Text = text,
    FontSize = 12,
    Foreground = Brushes.Gray,
    Margin = new Thickness(0, 0, 0, 2),
};
