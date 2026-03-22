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

        return Card(
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
                            new Label()
                                .BindText(countText)
                                .CenterVertical()
                                .FontSize(11),

                            new Label()
                                .Text("Fluent System Icons by Microsoft (MIT License)")
                                .WithTheme((t, c) => c.Foreground(t.Palette.DisabledText))
                                .CenterVertical()
                                .FontSize(11)
                        ),

                    grid
                ),
            minWidth: 460
        );
    }

    private sealed class IconItem(string name, string pathData)
    {
        public string Name { get; } = name;
        private PathGeometry? _geometry;
        public PathGeometry Geometry => _geometry ??= PathGeometry.Parse(pathData);
    }
}
