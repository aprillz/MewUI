using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement IconsPage()
    {
        var allIcons = IconResource.GetAll()
            .Select(e => new IconItem(e.Name, e.PathData))
            .ToArray();

        var query = new ObservableValue<string>(string.Empty);
        var countText = new ObservableValue<string>($"{allIcons.Length} icons");

        GridView grid = null!;

        void ApplyFilter()
        {
            var q = (query.Value ?? string.Empty).Trim();
            IEnumerable<IconItem> filtered = allIcons;
            if (!string.IsNullOrWhiteSpace(q))
                filtered = filtered.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

            var view = filtered.ToList();
            grid.SetItemsSource(view);
            countText.Value = $"{view.Count} / {allIcons.Length} icons";
        }

        query.Changed += ApplyFilter;

        grid = new GridView()
            .RowHeight(32)
            .Width(300)
            .ItemsSource(allIcons)
            .Columns(
                new GridViewColumn<IconItem>()
                    .Header("")
                    .Width(40)
                    .Template(
                        build: _ => new PathShape()
                            .Stretch(Stretch.Uniform)
                            .Width(24).Height(24)
                            .Center()
                            .WithTheme((t, p) => p.Fill(t.Palette.WindowText)),
                        bind: (view, item) => view.Data = item.Geometry),

                new GridViewColumn<IconItem>()
                    .Header("Name")
                    .Width(360)
                    .Text(item => item.Name)
            );

        var iconsCard = Card(
            "Icons (Path)",
            new DockPanel()
                .Height(400)
                .Spacing(6)
                .Children(
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(8)
                        .Children(
                            new TextBox()
                                .Width(200)
                                .Placeholder("Filter icons...")
                                .BindText(query),
                            new TextBlock()
                                .BindText(countText)
                                .CenterVertical()
                                .FontSize(11),

                            new TextBlock()
                                .Text("Fluent System Icons by Microsoft (MIT License)")
                                .WithTheme((t, c) => c.Foreground(t.Palette.DisabledText))
                                .CenterVertical()
                                .FontSize(11)
                        ),

                    grid
                ),
            minWidth: 460
        );

        return new StackPanel().Vertical().Spacing(8).Children(
            iconsCard,
            new WrapPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(8)
                .Children(
                    FontSizeCard(),
                    ColorCard(),
                    DisabledCard()
                )
        );
    }

    private static PathGeometry[] LoadIcons()
    {
        var names = new[] { "home_regular", "heart_regular", "settings_regular", "bookmark_regular", "star_off_regular" };
        return names
            .Select(n => IconResource.GetAll().FirstOrDefault(e => e.Name == n))
            .Where(e => e != null)
            .Select(e => PathGeometry.Parse(e!.PathData))
            .ToArray();
    }

    private FrameworkElement FontSizeCard()
    {
        var geo = LoadIcons();
        return Card("FontSize",
            new StackPanel().Vertical().Spacing(6).Children(
                ThemeBox().Child(new StackPanel().Horizontal().Spacing(10).Children(
                    new Icon().Data(geo[0]).FontSize(12),
                    new Icon().Data(geo[1]).FontSize(12),
                    new Icon().Data(geo[2]).FontSize(12),
                    new Icon().Data(geo[3]).FontSize(12),
                    new Icon().Data(geo[4]).FontSize(12)
                )),
                ThemeBox().Child(new StackPanel().Horizontal().Spacing(10).Children(
                    new Icon().Data(geo[0]).FontSize(16),
                    new Icon().Data(geo[1]).FontSize(16),
                    new Icon().Data(geo[2]).FontSize(16),
                    new Icon().Data(geo[3]).FontSize(16),
                    new Icon().Data(geo[4]).FontSize(16)
                )),
                ThemeBox().Child(new StackPanel().Horizontal().Spacing(10).Children(
                    new Icon().Data(geo[0]).FontSize(22),
                    new Icon().Data(geo[1]).FontSize(22),
                    new Icon().Data(geo[2]).FontSize(22),
                    new Icon().Data(geo[3]).FontSize(22),
                    new Icon().Data(geo[4]).FontSize(22)
                ))
            ), minWidth: 200);
    }

    private FrameworkElement ColorCard()
    {
        var geo = LoadIcons();
        return Card("Color",
            new StackPanel().Vertical().Spacing(6).Children(
                new StackPanel().Horizontal().Spacing(10).Children(
                    new Icon().Data(geo[0]).FontSize(24),
                    new Icon().Data(geo[1]).FontSize(24),
                    new Icon().Data(geo[2]).FontSize(24),
                    new Icon().Data(geo[3]).FontSize(24),
                    new Icon().Data(geo[4]).FontSize(24)
                ),
                new StackPanel().Horizontal().Spacing(10).Children(
                    new Icon().Data(geo[0]).FontSize(24).WithTheme((t, c) => c.Foreground = t.Palette.Accent),
                    new Icon().Data(geo[1]).FontSize(24).WithTheme((t, c) => c.Foreground = t.Palette.Accent),
                    new Icon().Data(geo[2]).FontSize(24).WithTheme((t, c) => c.Foreground = t.Palette.Accent),
                    new Icon().Data(geo[3]).FontSize(24).WithTheme((t, c) => c.Foreground = t.Palette.Accent),
                    new Icon().Data(geo[4]).FontSize(24).WithTheme((t, c) => c.Foreground = t.Palette.Accent)
                ),
                new StackPanel().Horizontal().Spacing(10).Children(
                    new Icon().Data(geo[0]).FontSize(24).Foreground(Color.FromRgb(200, 60, 60)),
                    new Icon().Data(geo[1]).FontSize(24).Foreground(Color.FromRgb(60, 160, 60)),
                    new Icon().Data(geo[2]).FontSize(24).Foreground(Color.FromRgb(60, 60, 200)),
                    new Icon().Data(geo[3]).FontSize(24).Foreground(Color.FromRgb(200, 160, 0)),
                    new Icon().Data(geo[4]).FontSize(24).Foreground(Color.FromRgb(160, 60, 160))
                )
            ), minWidth: 200);
    }

    private FrameworkElement DisabledCard()
    {
        var geo = LoadIcons();
        return Card("Disabled",
            new StackPanel().Vertical().Spacing(6).Children(
                new Button().Content(new StackPanel().Horizontal().Spacing(5).Center().Children(
                    new Icon().Data(geo[0]),
                    new TextBlock().Text("Default").CenterVertical()
                )),
                new Button().Disable().Content(new StackPanel().Horizontal().Spacing(5).Center().Children(
                    new Icon().Data(geo[0]),
                    new TextBlock().Text("Disabled").CenterVertical()
                )),
                new Button().StyleName(BuiltInStyles.AccentButton)
                    .Content(new StackPanel().Horizontal().Spacing(5).Center().Children(
                        new Icon().Data(geo[0]),
                        new TextBlock().Text("Accent").CenterVertical()
                    )),
                new Button().Disable().StyleName(BuiltInStyles.AccentButton)
                    .Content(new StackPanel().Horizontal().Spacing(5).Center().Children(
                        new Icon().Data(geo[0]),
                        new TextBlock().Text("Accent Disabled").CenterVertical()
                    ))
            ), minWidth: 200);
    }

    private static Border ThemeBox() =>
        new Border()
            .Padding(6).CornerRadius(4)
            .WithTheme((t, c) => c.Background = t.Palette.ControlBackground);

    private sealed class IconItem(string name, string pathData)
    {
        public string Name { get; } = name;
        private PathGeometry? _geometry;
        public PathGeometry Geometry => _geometry ??= PathGeometry.Parse(pathData);
    }
}
