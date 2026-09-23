using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement IconsCard()
    {
        IconItem[] AllIcons() => IconResource.GetAll(Resources.Icons.Value)
            .Select(e => new IconItem(e.Name, e.PathData))
            .ToArray();

        var query = new ObservableValue<string>(string.Empty);
        var countText = new ObservableValue<string>("0 icons");

        GridView grid = null!;

        void ApplyFilter()
        {
            var q = (query.Value ?? string.Empty).Trim();
            var allIcons = AllIcons();
            IEnumerable<IconItem> filtered = allIcons;
            if (!string.IsNullOrWhiteSpace(q))
                filtered = filtered.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

            var view = filtered.ToList();
            grid.ItemsSource = ItemsView.Create(view);
            countText.Value = $"{view.Count} / {allIcons.Length} icons";
        }

        Resources.Icons.Changed += ApplyFilter;
        query.Changed += ApplyFilter;

        grid = new GridView()
            .RowHeight(32)
            .Width(300)
            .ItemsSource(AllIcons())
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
                        bind: (view, item) =>
                        {
                            view.Data = item.Geometry;
                            view.ViewBox = IconViewBox(item.Geometry);
                        }),

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
                                new TextBlock()
                                    .BindText(countText)
                                    .CenterVertical()
                                    .FontSize(ThemeFontSize.Small),

                                new TextBlock()
                                    .Text("Fluent System Icons by Microsoft (MIT License)")
                                    .WithTheme((t, c) => c.Foreground(t.Palette.DisabledText))
                                    .CenterVertical()
                                    .FontSize(ThemeFontSize.Small)
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

    private FrameworkElement PromptIconsCard()
        => new WrapPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(12)
            .Children(
                PromptIconTile("Question", new PromptIcon { Kind = PromptIconKind.Question }),
                PromptIconTile("Info", new PromptIcon { Kind = PromptIconKind.Info }),
                PromptIconTile("Warning", new PromptIcon { Kind = PromptIconKind.Warning }),
                PromptIconTile("Error", new PromptIcon { Kind = PromptIconKind.Error }),
                PromptIconTile("Success", new PromptIcon { Kind = PromptIconKind.Success }),
                PromptIconTile("Shield", new PromptIcon { Kind = PromptIconKind.Shield }),
                PromptIconTile("Crash", new PromptIcon { Kind = PromptIconKind.Crash })
            );

    private FrameworkElement PromptIconTile(string title, FrameworkElement icon)
        => new StackPanel()
            .Width(90)
            .Vertical()
            .Spacing(6)
            .Children(
                icon.Width(60).Height(60).Center(),

                new TextBlock().Text(title).Center()
            );
}
