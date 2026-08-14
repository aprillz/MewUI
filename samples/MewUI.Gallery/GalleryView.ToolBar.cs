using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement ToolBarPage()
    {
        static IconTemplate Icon(string name)
        {
            var geometry = SegmentIcon(name);
            geometry.Freeze();
            return new IconTemplate(size =>
            {
                var shape = SegmentIconShape(size.Dip);
                shape.Data = geometry;
                ApplyIconViewBox(shape, geometry);
                return shape;
            });
        }

        var notified = new List<Command>();
        var silent = new List<Command>();

        Command Cmd(string id, string text, string? icon = null)
        {
            var command = new Command($"gallery.toolbar.{id}", text, icon == null ? null : Icon(icon));
            notified.Add(command);
            return command;
        }

        Command Toggle(string id, string text, string icon)
        {
            var command = new Command($"gallery.toolbar.{id}", text, Icon(icon));
            silent.Add(command);
            return command;
        }

        var save = Cmd("save", "_Save", "save_regular");
        var font = Cmd("font", "Font", "text_font_regular");
        var color = Cmd("color", "Colour", "color_regular");
        var zoomIn = Cmd("zoomIn", "Zoom _in");
        var zoomOut = Cmd("zoomOut", "Zoom _out");

        var bar = new ToolBar { CanReorderGroups = true };

        // Six groups a band, three entries each, so the splitter has plenty to collapse. Every entry kind
        // is here too, which makes the plates differ in width for the drag to move.
        bar.Bands.Add(new ToolBarBand(
            new ToolBarGroup(
                new ToolBarItem(Cmd("new", "_New", "document_add_regular")),
                new ToolBarItem(Cmd("open", "_Open", "folder_open_regular")),
                new ToolBarSplitItem(save)
                {
                    DropDownMenu = new Menu().Item(Cmd("saveAs", "Save _As")).Item(Cmd("saveAll", "Save A_ll")),
                }),
            new ToolBarGroup(
                new ToolBarItem(Cmd("cut", "Cu_t", "cut_regular")),
                new ToolBarItem(Cmd("copy", "_Copy", "copy_regular")),
                new ToolBarItem(Cmd("paste", "_Paste", "clipboard_paste_regular"))),
            new ToolBarGroup(
                new ToolBarItem(Cmd("print", "_Print", "print_regular")),
                new ToolBarItem(Cmd("duplicate", "Duplicate", "save_copy_regular")),
                new ToolBarItem(Cmd("refresh", "Refresh", "arrow_sync_circle_regular"))),
            new ToolBarGroup(
                new ToolBarItem(Cmd("image", "Image", "image_library_regular")),
                new ToolBarItem(Cmd("shapes", "Shapes", "shapes_regular")),
                new ToolBarItem(Cmd("table", "Table", "table_regular"))),
            new ToolBarGroup(
                new ToolBarItem(Cmd("grid", "Grid", "grid_regular")),
                new ToolBarItem(Cmd("layer", "Layers", "layer_regular")),
                new ToolBarItem(Cmd("dock", "Dock", "dock_regular"))),
            new ToolBarGroup(
                new ToolBarItem(Cmd("window", "Window", "window_regular")),
                new ToolBarItem(Cmd("windowNew", "New window", "window_new_regular")),
                new ToolBarItem(Cmd("resize", "Resize", "resize_regular")))));

        bar.Bands.Add(new ToolBarBand(
            new ToolBarGroup(
                new ToolBarToggleItem(Toggle("alignLeft", "Align left", "text_align_left_regular")) { IsChecked = true },
                new ToolBarToggleItem(Toggle("alignCenter", "Align centre", "text_align_center_regular")),
                new ToolBarToggleItem(Toggle("alignRight", "Align right", "text_align_right_regular"))),
            new ToolBarGroup(
                new ToolBarLabelItem("Style"),
                new ToolBarToggleItem(Toggle("wrap", "_Word wrap", "text_wrap_regular")) { IsChecked = true },
                new ToolBarMenuItem
                {
                    Text = "Font",
                    Icon = font.Icon,
                    DropDownMenu = new Menu().Item(font).Item(color),
                },
                new ToolBarMenuItem
                {
                    Text = "Zoom",
                    Icon = color.Icon,
                    DropDownMenu = new Menu().Item(zoomIn).Item(zoomOut).Separator().Item(Cmd("reset", "Reset")),
                }),
            new ToolBarGroup(
                new ToolBarItem(Cmd("list", "List", "list_regular")),
                new ToolBarItem(Cmd("tree", "Tree", "text_bullet_list_tree_regular")),
                new ToolBarItem(Cmd("collections", "Collections", "collections_regular"))),
            new ToolBarGroup(
                new ToolBarItem(Cmd("home", "Home", "home_regular")),
                new ToolBarItem(Cmd("navigate", "Navigate", "navigation_regular")),
                new ToolBarItem(Cmd("link", "Link", "link_regular"))),
            new ToolBarGroup(
                new ToolBarItem(Cmd("tools", "Tools", "wrench_regular")),
                new ToolBarItem(Cmd("paint", "Paint", "paint_brush_regular")),
                new ToolBarItem(Cmd("select", "Select", "multiselect_regular"))),
            new ToolBarGroup(
                new ToolBarItem(Cmd("settings", "Settings", "settings_regular")),
                new ToolBarItem(Cmd("options", "Options", "options_regular")),
                new ToolBarItem(Cmd("alerts", "Alerts", "alert_on_regular")))));

        foreach (var command in notified)
        {
            var captured = command;
            bar.Commands.Register(captured, () => _ = MessageBox.NotifyAsync(captured.Text ?? captured.Id));
        }

        foreach (var command in silent)
        {
            bar.Commands.Register(command, () => { });
        }

        var layout = new TextBlock { Text = Describe(bar) };
        bar.GroupsReordered += () => layout.Text = Describe(bar);

        // The splitter is what makes overflow visible: drag it left and each band gives up its trailing
        // entries one at a time, then whole groups, behind that band's own chevron.
        return CardGrid(
            Card(
                "ToolBar",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock { Text = "Drag a grip to move its group. Below the last band opens a new one. Drag the splitter to narrow the toolbar." },
                        new SplitPanel()
                            .Horizontal()
                            .SplitterThickness(8)
                            .MinFirst(200)
                            .MinSecond(80)
                            .FirstLength(GridLength.Stars(3))
                            .SecondLength(GridLength.Stars(1))
                            .First(bar)
                            .Second(
                                new Border()
                                    .WithTheme((t, b) => b.Background(t.Palette.ButtonFace))
                                    .CornerRadius(8)
                                    .Child(new TextBlock().TextWrapping(TextWrapping.Wrap).Text("Drag left").Center())),
                        layout)));
    }

    private static string Describe(ToolBar bar)
    {
        var lines = bar.Bands.Select(band => string.Join(
            "   |   ",
            band.Groups.Select(group => string.Join(
                ", ",
                group.Items.Select(DescribeEntry)))));

        return string.Join(Environment.NewLine, lines);
    }

    private static string DescribeEntry(ToolBarEntry entry) => entry switch
    {
        ToolBarItem item => item.Command?.Text ?? "?",
        ToolBarMenuItem menu => menu.Text,
        ToolBarLabelItem label => label.Text,
        _ => "?",
    };
}
