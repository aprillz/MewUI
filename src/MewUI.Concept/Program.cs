using System.Diagnostics;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (OperatingSystem.IsWindows())
        {
            Win32Platform.Register();

            MewVGWin32Backend.Register();
        }
        else
        {
            X11Platform.Register();
        }

        try
        {
            var app = Application.Create().UseMetrics(ThemeMetrics.Default with { ControlCornerRadius = 10 });

            GridView simpleGrid = null!;
            GridView complexGrid = null!;
            GridView complexCellsGrid = null!;
            var numericValue = new ObservableValue<double>(12);

            // Demo: toggle some values periodically to validate observable updates.
            int tick = 0;
            var timer = new DispatcherTimer()
                .IntervalMs(500);

            var window = new Window()
                .Title("MewUI.Concept")
                .Resizable(800, 600)
                .Content(
                    new DockPanel()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                            .DockTop()
                                .Text("GridView concept (delegate templates + recycling)GridView concept (delegate templates + recycling)GridView concept (delegate templates + recycling)GridView concept (delegate templates + recycling)GridView concept (delegate templates + recycling)").TextWrapping(TextWrapping.Wrap),

                            new StackPanel()
                                .DockTop()
                                .Horizontal()
                                .Spacing(8)
                                .Children(
                                    new TextBlock()
                                        .Text("NumericUpDown")
                                        .CenterVertical(),

                                    new NumericUpDown()
                                        .Width(140)
                                        .Minimum(0)
                                        .Maximum(100)
                                        .Step(1)
                                        .BindValue(numericValue)
                                        .CenterVertical(),

                                    new TextBlock()
                                        .BindText(numericValue, v => $"Value: {v:0.##}")
                                        .CenterVertical(),

                                    new Button()
                                        .Content("Click me")
                                        .CenterVertical()
                                ),

                            new Grid()
                                .Columns("*,*")
                                .Rows("Auto,*")
                                .Spacing(8)
                                .Children(
                                    new TextBlock()
                                        .Text("Simple cells")
                                        .Bold(),

                                    new TextBlock()
                                        .Text("Complex cell binding")
                                        .Bold(),

                                    SimpleGridSample()
                                        .Ref(out simpleGrid),

                                    new TabControl()
                                        .TabItems(
                                            new TabItem()
                                                .Header("User card")
                                                .Content(ComplexGridSample().Ref(out complexGrid)),
                                            new TabItem()
                                                .Header("Complex cells")
                                                .Content(ComplexCellsGridSample().Ref(out complexCellsGrid)),
                                            new TabItem()
                                                .Header("ListBox")
                                                .Content(ListBoxThemeSample())
                                        )
                                )
                        )
                );

            using (var iconStream = typeof(Program).Assembly.GetManifestResourceStream("Aprillz.MewUI.Concept.appicon.ico"))
            {
                if (iconStream is not null)
                {
                    window.Icon = IconSource.FromStream(iconStream);
                }
            }

            Application.DispatcherUnhandledException += e =>
            {
                Console.WriteLine("Unhandled UI exception: " + e.Exception);
                e.Handled = true;
            };

            window.Closed += () =>
            {
                timer.Dispose();
                if (Application.IsRunning)
                {
                    Application.Current.PlatformHost.Quit(Application.Current);
                }
            };

            var people = new List<Person>(capacity: 200000);
            for (int i = 0; i < 200000; i++)
            {
                var p = new Person
                {
                    Name = $"User {i:000}"
                };
                p.Status.Value = i % 3 == 0;
                p.Progress.Value = i % 101;
                people.Add(p);
            }
            simpleGrid.ItemsSource(people);
            complexGrid.ItemsSource(people);

            var complexRows = new List<ComplexPersonRow>(capacity: 1000);
            for (int i = 0; i < 1000; i++)
            {
                complexRows.Add(new ComplexPersonRow(
                    name: $"User {i:0000}",
                    roleIndex: i % 3,
                    isOnline: i % 5 != 0,
                    progress: i % 101,
                    score: (i * 7.3) % 100));
            }
            complexCellsGrid.ItemsSource(complexRows);

            timer.OnTick(() =>
            {
                tick++;
                int idx = tick % people.Count;
                people[idx].IsChecked.Value = !people[idx].IsChecked.Value;
                people[idx].Status.Value = people[idx].IsChecked.Value;
                people[idx].Progress.Value = (people[idx].Progress.Value + 7) % 101;

                int cidx = tick % complexRows.Count;
                var row = complexRows[cidx];
                row.IsSelected.Value = !row.IsSelected.Value;
                row.IsOnline.Value = !row.IsOnline.Value;
                row.Progress.Value = (row.Progress.Value + 11) % 101;
                row.Score.Value = (row.Score.Value + 3.3) % 100;
            });

            timer.Start();
            app.Run(window);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Unhandled exception: " + ex);
        }
    }



    private static GridView SimpleGridSample()
    {
        var grid = new GridView()
            .HeaderHeight(28)
            .RowHeight(28)
            .OnMouseDown(x => Debug.WriteLine("MouseDown"))
            .OnMouseUp(x => Debug.WriteLine("MouseUp"))
            .OnMouseDoubleClick(x => Debug.WriteLine("MouseDoubleClick"))
            .ZebraStriping()
            .Columns(
            new GridViewColumn<Person>()
                .Header("")
                .Width(36)
                .Bind(
                    build: _ => new CheckBox().Padding(0).Center(),
                    bind: (view, item) => ((CheckBox)view).BindIsChecked(item.IsChecked)
                ),

            new GridViewColumn<Person>()
                .Header("Name")
                .Width(120)
                .Bind(
                    build: _ => new TextBlock().Margin(6, 0).CenterVertical(),
                    bind: (view, item) => ((TextBlock)view).Text = item.Name
                ),

            new GridViewColumn<Person>()
                .Header("Status")
                .Width(80)
                .Bind(
                    build: _ => new TextBlock().Margin(6, 0).CenterVertical().CenterHorizontal(),
                    bind: (view, item) => ((TextBlock)view).BindText(item.Status, x => x ? "Online" : "Offline")
                ),

            new GridViewColumn<Person>()
                .Header("Progress")
                .Width(80)
                .Bind(
                    build: _ => new ProgressBar().Minimum(0).Maximum(100).CenterVertical().Margin(6, 0),
                    bind: (view, item) => ((ProgressBar)view).BindValue(item.Progress)
                )
        );

        return grid;
    }

    private static GridView ComplexCellsGridSample()
    {
        var grid = new GridView()
            .HeaderHeight(28)
            .RowHeight(44)
            .ZebraStriping();

        static string RoleText(int index) => index switch
        {
            1 => "Admin",
            2 => "Guest",
            _ => "User"
        };

        static Color StatusColor(Theme t, bool online) => online
            ? t.Palette.Accent
            : t.Palette.DisabledText;

        grid.Columns(
            new GridViewColumn<ComplexPersonRow>()
                .Header("")
                .Width(36)
                .Bind(
                    build: _ => new CheckBox().Padding(0).Center(),
                    bind: (view, item) => ((CheckBox)view).BindIsChecked(item.IsSelected)),

            new GridViewColumn<ComplexPersonRow>()
                .Header("User")
                .Width(220)
                .Bind(
                    build: ctx => new StackPanel()
                        .Vertical()
                        .Spacing(2)
                        .Padding(6, 2)
                        .Children(
                            new TextBlock()
                                .Register(ctx, "Name")
                                .Bold(),
                            new StackPanel()
                                .Horizontal()
                                .Spacing(8)
                                .Children(
                                    new TextBlock()
                                        .Register(ctx, "Role")
                                        .FontSize(11),
                                    new TextBlock()
                                        .Register(ctx, "Online")
                                        .FontSize(11),
                                    new TextBlock()
                                        .Register(ctx, "Score")
                                        .FontSize(11)
                                )
                        ),
                    bind: (_, item, _, ctx) =>
                    {
                        ctx.Get<TextBlock>("Name").BindText(item.Name);

                        var role = ctx.Get<TextBlock>("Role");
                        role.BindText(item.RoleIndex, RoleText);

                        var online = ctx.Get<TextBlock>("Online");
                        online.BindText(item.IsOnline, v => v ? "Online" : "Offline");
                        online.WithTheme((t, c) => c.Foreground(StatusColor(t, item.IsOnline.Value)));

                        ctx.Get<TextBlock>("Score").BindText(item.Score, v => $"Score: {v:0.#}");
                    }),

            new GridViewColumn<ComplexPersonRow>()
                .Header("Role")
                .Width(120)
                .Bind(
                    build: _ => new ComboBox()
                        .Items(["User", "Admin", "Guest"])
                        .Padding(6, 0)
                        .CenterVertical(),
                    bind: (view, item) => ((ComboBox)view).BindSelectedIndex(item.RoleIndex)),

            new GridViewColumn<ComplexPersonRow>()
                .Header("Progress")
                .Width(130)
                .Bind(
                    build: _ => new ProgressBar()
                        .Minimum(0)
                        .Maximum(100)
                        .Height(10)
                        .Margin(6, 0)
                        .CenterVertical(),
                    bind: (view, item) => ((ProgressBar)view).BindValue(item.Progress)),

            new GridViewColumn<ComplexPersonRow>()
                .Header("Online")
                .Width(80)
                .Bind(
                    build: _ => new ToggleSwitch().Center(),
                    bind: (view, item) => ((ToggleSwitch)view).BindIsChecked(item.IsOnline))
        );

        return grid;
    }

    private static UIElement ListBoxThemeSample()
        => new DockPanel()
            .Spacing(8)
            .Children(
                new StackPanel()
                    .DockTop()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Light")
                            .OnClick(() => Application.Current.SetTheme(ThemeVariant.Light)),
                        new Button()
                            .Content("Dark")
                            .OnClick(() => Application.Current.SetTheme(ThemeVariant.Dark)),
                        new Button()
                            .Content("System")
                            .OnClick(() => Application.Current.SetTheme(ThemeVariant.System))
                    ),
                new ListBox()
                    .FixedHeightPresenter()
                    .Height(240)
                    .Items(
                        "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot",
                        "Golf", "Hotel", "India", "Juliett", "Kilo", "Lima",
                        "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo",
                        "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray",
                        "Yankee", "Zulu")
                    .SelectedIndex(2)
            );

    private static GridView ComplexGridSample()
    {
        var grid = new GridView()
            .HeaderHeight(28)
            .RowHeight(38)
            .ZebraStriping();

        grid.Columns(
            new GridViewColumn<Person>()
                .Header("User card")
                .Width(260)
                .Bind(
                    build: ctx => new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Padding(6, 2)
                            .Children(
                                new CheckBox()
                                    .Register(ctx, "CheckBox")
                                    .Padding(0)
                                    .Center()
                                    .CenterVertical(),

                                new StackPanel()
                                    .Vertical()
                                    .Spacing(2)
                                    .Children(
                                        new TextBlock()
                                            .Register(ctx, "NameText")
                                            .Bold()
                                            .FontSize(12),

                                        new StackPanel()
                                            .Horizontal()
                                            .Spacing(8)
                                            .CenterVertical()
                                            .Children(
                                                new TextBlock()
                                                    .Register(ctx, "StatusText")
                                                    .FontSize(11),

                                                new ProgressBar()
                                                    .Register(ctx, "ProgressBar")
                                                    .Minimum(0)
                                                    .Maximum(100)
                                                    .Height(10)
                                                    .Width(140)
                                                    .CenterVertical()
                                            )
                                    )
                            )
                    ,
                    bind: (_, item, _, ctx) =>
                    {
                        ctx.Get<CheckBox>("CheckBox").BindIsChecked(item.IsChecked);
                        ctx.Get<TextBlock>("NameText").Text(item.Name);
                        ctx.Get<TextBlock>("StatusText").BindText(item.Status, x => x ? "Online" : "Offline");
                        ctx.Get<ProgressBar>("ProgressBar").BindValue(item.Progress);
                    }),

            new GridViewColumn<Person>()
                .Header("Name")
                .Width(180)
                .Bind(
                    build: ctx => new TextBlock().Margin(6, 0).CenterVertical(),
                    bind: (view, item) => ((TextBlock)view).Text(item.Name))
    );

        return grid;
    }
}
