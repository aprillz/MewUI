#:sdk Microsoft.NET.Sdk

#:property OutputType=WinExe
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property AllowUnsafeBlocks=true

#:project ../../src/MewUI/MewUI.csproj
#:project ../../src/MewUI.Platform.Win32/MewUI.Platform.Win32.csproj
#:project ../../src/MewUI.Backend.Direct2D/MewUI.Backend.Direct2D.csproj

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

Win32Platform.Register();
Direct2DBackend.Register();

var window = new Window()
    .Title("MewUI Grid Star Content-Sizing Reference")
    .Resizable(900, 700)
    .OnBuild(w => w.Content(BuildContent()));

Application.Run(window);

static FrameworkElement BuildContent() =>
    new ScrollViewer()
        .VerticalScroll(ScrollMode.Auto)
        .Padding(16)
        .Content(
            new StackPanel()
                .Vertical()
                .Children(
                    Title("Grid Star Sizing — content with different widths"),
                    Note("Columns: Auto,*,Auto,*,Auto,*  TextBox texts: \"가\" / \"가나\" / \"가나다\""),
                    Note("Question: do the three Star columns size to fit each (or the max) TextBox?"),

                    SectionLabel("1. Pure Star — no MinWidth   [Auto,*,Auto,*,Auto,*]"),
                    BuildGrid(useMinWidth: false),

                    SectionLabel("2. Star + MinWidth=80 (manual)   [Auto,*(min80),Auto,*(min80),Auto,*(min80)]"),
                    BuildGrid(useMinWidth: true),

                    SectionLabel("3. ShareStarSize=true   [Auto,*,Auto,*,Auto,*]"),
                    BuildGrid(useMinWidth: false, shareStar: true),

                    SectionLabel("4. All Auto (baseline)   [Auto,Auto,Auto,Auto,Auto,Auto]"),
                    BuildGrid(useAuto: true),

                    SectionLabel("5. Pure Star + HorizontalAlignment=Left   [Auto,*,Auto,*,Auto,*]"),
                    BuildGrid(useMinWidth: false, alignLeft: true),

                    SectionLabel("6. Star + MinWidth=80 + HorizontalAlignment=Left   [Auto,*(min80),Auto,*(min80),Auto,*(min80)]"),
                    BuildGrid(useMinWidth: true, alignLeft: true)
                )
        );

static FrameworkElement BuildGrid(bool useMinWidth = false, bool useAuto = false, bool alignLeft = false, bool shareStar = false)
{
    var grid = new Grid().ShowGridLine();
    if (alignLeft) grid.Left();
    if (shareStar) grid.ShareStarSize();

    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    GridLength colA = useAuto ? GridLength.Auto : new GridLength(1, GridUnitType.Star);

    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(MakeStar(colA, useMinWidth));
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(MakeStar(colA, useMinWidth));
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(MakeStar(colA, useMinWidth));

    grid.Children(
        // Row 0
        MakeLabel("A:").GridPosition(0, 0),
        MakeBox("가").GridPosition(0, 1),
        MakeLabel("B:").GridPosition(0, 2),
        MakeBox("가나").GridPosition(0, 3),
        MakeLabel("C:").GridPosition(0, 4),
        MakeBox("가나다").GridPosition(0, 5),

        // Row 1: single-char content
        MakeLabel("A:").GridPosition(1, 0),
        MakeBox("가").GridPosition(1, 1),
        MakeLabel("B:").GridPosition(1, 2),
        MakeBox("가").GridPosition(1, 3),
        MakeLabel("C:").GridPosition(1, 4),
        MakeBox("가").GridPosition(1, 5),

        // Row 2: single-char content
        MakeLabel("A:").GridPosition(2, 0),
        MakeBox("가").GridPosition(2, 1),
        MakeLabel("B:").GridPosition(2, 2),
        MakeBox("가").GridPosition(2, 3),
        MakeLabel("C:").GridPosition(2, 4),
        MakeBox("가").GridPosition(2, 5)
    );

    return new Border()
        .Margin(new Thickness(0, 4, 0, 16))
        .Child(grid);
}

static ColumnDefinition MakeStar(GridLength width, bool useMinWidth)
{
    var col = new ColumnDefinition { Width = width };
    if (useMinWidth) col.MinWidth = 80;
    return col;
}

static Label MakeLabel(string text) =>
    new Label()
        .Text(text)
        .CenterVertical()
        .Margin(new Thickness(8, 4, 4, 4));

static TextBox MakeBox(string text) =>
    new TextBox()
        .Text(text)
        .Margin(new Thickness(2))
        .Padding(new Thickness(4, 2, 4, 2));

static Label Title(string text) =>
    new Label()
        .Text(text)
        .FontSize(16)
        .Bold()
        .Margin(new Thickness(0, 0, 0, 8));

static Label SectionLabel(string text) =>
    new Label()
        .Text(text)
        .FontSize(13)
        .Bold()
        .Margin(new Thickness(0, 12, 0, 4));

static Label Note(string text) =>
    new Label()
        .Text(text)
        .FontSize(12)
        .Foreground(Color.FromRgb(128, 128, 128))
        .Margin(new Thickness(0, 0, 0, 2));
