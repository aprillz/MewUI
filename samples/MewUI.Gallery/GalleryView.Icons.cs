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

        var iconControlCard = BuildIconControlCard();

        return new StackPanel().Vertical().Spacing(8).Children(iconsCard, iconControlCard);
    }

    private FrameworkElement BuildIconControlCard()
    {
        var iconNames = new[] { "home_regular", "heart_regular", "settings_regular", "bookmark_regular", "star_off_regular" };

        var icons = iconNames
            .Select(n => IconResource.GetAll().FirstOrDefault(e => e.Name == n))
            .Where(e => e != null)
            .ToArray();

        Icon Get(int i) => new Icon().Data(PathGeometry.Parse(icons[i]!.PathData));
        Icon GetSized(int i, double s) => Get(i).Width(s).Height(s);

        Button MakeBtn(string text, bool disabled, bool flat)
        {
            var btn = new Button()
                .Content(new StackPanel().Horizontal().Spacing(5).Center().Children(
                    GetSized(0, 16), new TextBlock().Text(text).CenterVertical()
                ));
            if (disabled) btn.Disable();
            if (flat) btn.Apply(b => b.StyleName = BuiltInStyles.FlatButton);
            return btn;
        }

        FrameworkElement SubCard(string title, FrameworkElement content) =>
            new Border()
                .Padding(10)
                .BorderThickness(1)
                .CornerRadius(8)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(
                    new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Children(new TextBlock().Text(title).FontSize(12).Bold(), content)
                );

        return Card(
            "Icon Control",
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    SubCard("FontSize",
                        new StackPanel().Vertical().Spacing(6).Children(
                            ThemeBorder(12).Child(row(Get(0), Get(1), Get(2), Get(3), Get(4))),
                            ThemeBorder(16).Child(row(Get(0), Get(1), Get(2), Get(3), Get(4))),
                            ThemeBorder(22).Child(row(Get(0), Get(1), Get(2), Get(3), Get(4)))
                        )),

                    SubCard("Color",
                        new StackPanel().Vertical().Spacing(6).Children(
                            row(GetSized(0, 24), GetSized(1, 24), GetSized(2, 24), GetSized(3, 24), GetSized(4, 24)),
                            row(
                                Accented(GetSized(0, 24)),
                                Accented(GetSized(1, 24)),
                                Accented(GetSized(2, 24)),
                                Accented(GetSized(3, 24)),
                                Accented(GetSized(4, 24))
                            ),
                            row(
                                GetSized(0, 24).Foreground(Color.FromArgb(255, 200, 60, 60)),
                                GetSized(1, 24).Foreground(Color.FromArgb(255, 60, 160, 60)),
                                GetSized(2, 24).Foreground(Color.FromArgb(255, 60, 60, 200)),
                                GetSized(3, 24).Foreground(Color.FromArgb(255, 200, 160, 0)),
                                GetSized(4, 24).Foreground(Color.FromArgb(255, 160, 60, 160))
                            )
                        )),

                    SubCard("Disabled",
                        new StackPanel().Vertical().Spacing(6).Children(
                            MakeBtn("Default", false, false),
                            MakeBtn("Disabled", true, false),
                            MakeBtn("Accent", false, false).Apply(b => b.StyleName = BuiltInStyles.AccentButton),
                            MakeBtn("Accent Disabled", true, false).Apply(b => b.StyleName = BuiltInStyles.AccentButton)
                        ))
                ),
            minWidth: 700
        );
    }

    private static Icon Accented(Icon icon)
    {
        icon.WithTheme((Theme t, Icon c) => c.Foreground = t.Palette.Accent);
        return icon;
    }

    private static Border ThemeBorder(double fontSize) =>
        new Border()
            .Padding(6).CornerRadius(4)
            .FontSize(fontSize)
            .WithTheme((t, c) => c.Background = t.Palette.ControlBackground);

    private static StackPanel row(params Icon[] icons)
    {
        var panel = new StackPanel().Horizontal().Spacing(10);
        foreach (var icon in icons)
            panel.Children(icon);
        return panel;
    }

    private sealed class IconItem(string name, string pathData)
    {
        public string Name { get; } = name;
        private PathGeometry? _geometry;
        public PathGeometry Geometry => _geometry ??= PathGeometry.Parse(pathData);
    }
}
