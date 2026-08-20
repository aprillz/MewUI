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

        // Gestures are collected rather than mapped here: the map they go into belongs to the toolbar,
        // which does not exist yet.
        var shortcuts = new List<(Command Command, KeyGesture Gesture)>();

        Command Cmd(
            string id,
            string text,
            string? icon = null,
            string? description = null,
            Key shortcut = Key.None)
        {
            var command = new Command($"gallery.toolbar.{id}", text, icon == null ? null : Icon(icon));
            command.Description(description);
            notified.Add(command);

            if (shortcut != Key.None)
            {
                shortcuts.Add((command, new KeyGesture(shortcut, ModifierKeys.Primary)));
            }

            return command;
        }

        Command Toggle(string id, string text, string icon)
        {
            var command = new Command($"gallery.toolbar.{id}", text, Icon(icon));
            silent.Add(command);
            return command;
        }

        var save = Cmd("save", "_Save", "save_regular", "Writes the document to disk.", Key.S);
        var font = Cmd("font", "Font", "text_font_regular");
        var color = Cmd("color", "Colour", "color_regular");
        var zoomIn = Cmd("zoomIn", "Zoom _in");
        var zoomOut = Cmd("zoomOut", "Zoom _out");

        // Six groups a band, three entries each, so the splitter has plenty to collapse. Every entry kind
        // is here too, which makes the plates differ in width for the drag to move.
        var bar = new ToolBar()
            .Band(
                new ToolBarGroup()
                    .Item(Cmd("new", "_New", "document_add_regular", "Starts a document with nothing in it.", Key.N))
                    .Item(Cmd("open", "_Open", "folder_open_regular", "Opens a document already on disk.", Key.O))
                    .Split(save, new Menu().Item(Cmd("saveAs", "Save _As")).Item(Cmd("saveAll", "Save A_ll"))),
                new ToolBarGroup()
                    .Item(Cmd("cut", "Cu_t", "cut_regular", shortcut: Key.X))
                    .Item(Cmd("copy", "_Copy", "copy_regular", shortcut: Key.C))
                    .Item(Cmd("paste", "_Paste", "clipboard_paste_regular", shortcut: Key.V)),
                // One group where there were two, divided by a splitter: the two runs travel together now.
                new ToolBarGroup()
                    .Item(Cmd("print", "_Print", "print_regular", "Sends the document to a printer.", Key.P))
                    .Item(Cmd("duplicate", "Duplicate", "save_copy_regular"))
                    .Item(Cmd("refresh", "Refresh", "arrow_sync_circle_regular"))
                    .Separator()
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
            // A hosted control on a band of its own: narrow the toolbar and it collapses into the overflow
            // popup as the text box it is, which a band that rebuilt its entries as menu rows could not do.
            .Band(
                new ToolBarGroup()
                    .Label("Find")
                    .Host(new TextBox().Width(140).Placeholder("Search")));

        // What ran goes to a line under the toolbar: a message box would take the focus and cover the band
        // the entry was on, which is the thing being tried out here.
        var log = new TextBlock().Text("Nothing run yet");

        // Entries show icons only here, so what a tooltip says is the only name they have. Every option
        // below is a combination of the parts a command can supply.
        var toolTipModes = new (string Text, CommandToolTipMode Mode)[]
        {
            ("Name, shortcut, description", CommandToolTipMode.Text | CommandToolTipMode.Shortcut | CommandToolTipMode.Description),
            ("Name and shortcut", CommandToolTipMode.Text | CommandToolTipMode.Shortcut),
            ("Name only", CommandToolTipMode.Text),
            ("Description only", CommandToolTipMode.Description),
            ("No tooltip", CommandToolTipMode.None),
        };

        var toolTipMode = new ComboBox()
            .Width(220)
            .Items(toolTipModes, option => option.Text)
            .SelectedIndex(0)
            .CenterVertical();
        toolTipMode.SelectionChanged += _ =>
            bar.ItemToolTipMode = toolTipModes[Math.Max(0, toolTipMode.SelectedIndex)].Mode;

        foreach (var command in notified)
        {
            var captured = command;
            bar.Commands.Register(captured, () => log.Text = $"Ran: {captured.Text ?? captured.Id}");
        }

        // Resolution walks up from the entry, so one map on the toolbar reaches every one of them.
        foreach (var (command, gesture) in shortcuts)
        {
            bar.InputMap.Map(command, gesture);
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

                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Label().Text("Tooltip").CenterVertical(),
                                toolTipMode,
                                new TextBlock()
                                    .TextWrapping(TextWrapping.Wrap)
                                    .Text("New, Open, Save and Print carry both a shortcut and a description; Cut, Copy and Paste carry a shortcut only; the rest carry neither. A part with nothing behind it is left out rather than filled from another.")
                                    .CenterVertical()),

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
                minWidth: 820),
            LegacyToolBarCard());
    }

    /// <summary>
    /// A toolbar built from controls instead of commands: what an application porting from a framework
    /// where a toolbar holds buttons would write. The controls are hosted as they are, so their own
    /// handlers and bindings work and the band still collapses them.
    /// </summary>
    private FrameworkElement LegacyToolBarCard()
    {
        var log = new TextBlock().Text("Nothing run yet");

        Button Emoji(string glyph, string toolTip)
            => new Button().Content(glyph).ToolTip(toolTip).OnClick(() => log.Text = $"Ran: {toolTip}");

        ToggleButton Switch(string glyph, string toolTip)
        {
            var toggle = new ToggleButton().Content(glyph).ToolTip(toolTip);
            toggle.CheckedChanged += isChecked => log.Text = $"{toolTip}: {isChecked}";
            return toggle;
        }

        var zoom = new ComboBox().Width(80).Items(["100%", "150%", "200%"]).SelectedIndex(0);
        zoom.SelectionChanged += _ => log.Text = $"Zoom: {zoom.SelectedIndex}";

        var bar = new ToolBar()
            .Band(
                new ToolBarGroup().Items(
                    Emoji("🆕", "New game"),
                    Emoji("📂", "Open"),
                    Emoji("💾", "Save")),
                new ToolBarGroup().Items(
                    Emoji("✂️", "Cut"),
                    Emoji("📋", "Copy"),
                    Emoji("📌", "Paste")),
                new ToolBarGroup().Items(
                    Switch("🎨", "Colour"),
                    Switch("🔊", "Sound"),
                    new Separator(),
                    Switch("🧭", "Compass")),
                new ToolBarGroup().Items(
                    new Label().Text("Zoom"),
                    zoom));

        return Card(
            "ToolBar from controls",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .TextWrapping(TextWrapping.Wrap)
                        .Text("Entries put in with Items() are the controls themselves. They keep their own content and handlers, and carry the tooltip each one was given rather than one built from a command."),
                    bar,
                    log),
            minWidth: 420);
    }
}
