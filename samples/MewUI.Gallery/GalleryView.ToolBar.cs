using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement ToolBarPage()
    {
        static IconTemplate Icon(string name)
            => new IconTemplate(size =>
            {
                var shape = SegmentIconShape(size.Dip);
                BindNamedIcon(shape, name);
                return shape;
            });

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

        // Six groups a band, three entries each, so the splitter has plenty to collapse. Every entry kind
        // is here too, which makes the plates differ in width for the drag to move.
        var bar = new ToolBar()
            .CanReorderGroups()
            .Band(
                new ToolBarGroup()
                    .Item(Cmd("new", "_New", "document_add_regular"))
                    .Item(Cmd("open", "_Open", "folder_open_regular"))
                    .Split(save, new Menu().Item(Cmd("saveAs", "Save _As")).Item(Cmd("saveAll", "Save A_ll"))),
                new ToolBarGroup()
                    .Item(Cmd("cut", "Cu_t", "cut_regular"))
                    .Item(Cmd("copy", "_Copy", "copy_regular"))
                    .Item(Cmd("paste", "_Paste", "clipboard_paste_regular")),
                // One group where there were two, divided by a splitter: the two runs travel together now.
                new ToolBarGroup()
                    .Item(Cmd("print", "_Print", "print_regular"))
                    .Item(Cmd("duplicate", "Duplicate", "save_copy_regular"))
                    .Item(Cmd("refresh", "Refresh", "arrow_sync_circle_regular"))
                    .Splitter()
                    .Item(Cmd("image", "Image", "image_library_regular"))
                    .Item(Cmd("shapes", "Shapes", "shapes_regular"))
                    .Item(Cmd("table", "Table", "table_regular")),
                new ToolBarGroup()
                    .Item(Cmd("grid", "Grid", "grid_regular"))
                    .Item(Cmd("layer", "Layers", "layer_regular"))
                    .Item(Cmd("dock", "Dock", "dock_regular")),
                new ToolBarGroup()
                    .Item(Cmd("window", "Window", "window_regular"))
                    .Item(Cmd("windowNew", "New window", "window_new_regular"))
                    .Item(Cmd("resize", "Resize", "resize_regular")))
            .Band(
                new ToolBarGroup()
                    .Toggle(Toggle("alignLeft", "Align left", "text_align_left_regular"), isChecked: true)
                    .Toggle(Toggle("alignCenter", "Align centre", "text_align_center_regular"))
                    .Toggle(Toggle("alignRight", "Align right", "text_align_right_regular")),
                new ToolBarGroup()
                    .Label("Style")
                    .Toggle(Toggle("wrap", "_Word wrap", "text_wrap_regular"), isChecked: true)
                    .Menu("Font", new Menu().Item(font).Item(color), font.Icon)
                    .Menu(
                        "Zoom",
                        new Menu().Item(zoomIn).Item(zoomOut).Separator().Item(Cmd("reset", "Reset")),
                        color.Icon),
                new ToolBarGroup()
                    .Item(Cmd("list", "List", "list_regular"))
                    .Item(Cmd("tree", "Tree", "text_bullet_list_tree_regular"))
                    .Item(Cmd("collections", "Collections", "collections_regular")),
                new ToolBarGroup()
                    .Item(Cmd("home", "Home", "home_regular"))
                    .Item(Cmd("navigate", "Navigate", "navigation_regular"))
                    .Item(Cmd("link", "Link", "link_regular")),
                new ToolBarGroup()
                    .Item(Cmd("tools", "Tools", "wrench_regular"))
                    .Item(Cmd("paint", "Paint", "paint_brush_regular"))
                    .Item(Cmd("select", "Select", "multiselect_regular")),
                new ToolBarGroup()
                    .Item(Cmd("settings", "Settings", "settings_regular"))
                    .Item(Cmd("options", "Options", "options_regular"))
                    .Item(Cmd("alerts", "Alerts", "alert_on_regular")))
            // A hosted control on a band of its own: a host is the one entry an overflow menu cannot collapse.
            .Band(
                new ToolBarGroup()
                    .Label("Find")
                    .Host(new TextBox().Width(140).Placeholder("Search")));

        // What ran goes to a line under the toolbar: a message box would take the focus and cover the band
        // the entry was on, which is the thing being tried out here.
        var log = new TextBlock().Text("Nothing run yet");

        foreach (var command in notified)
        {
            var captured = command;
            bar.Commands.Register(captured, () => log.Text = $"Ran: {captured.Text ?? captured.Id}");
        }

        foreach (var command in silent)
        {
            bar.Commands.Register(command, () => { });
        }

        // The splitter is what makes overflow visible: drag it left and each band gives up its trailing
        // entries one at a time, then whole groups, behind that band's own chevron. The card states a
        // minimum width because a split panel wants what its two panes want: without one, narrowing the
        // toolbar pane would narrow the card itself and the panel would walk left as it is dragged.
        return CardGrid(
            Card(
                "ToolBar",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("Drag a grip to move its group. Below the last band opens a new one. Drag the splitter to narrow the toolbar."),

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
                        log
                        ),
                minWidth: 820));
    }
}
