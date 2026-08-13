using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement ButtonsPage()
    {
        // Labeled variant (label above), mirroring the SegmentedControl samples.
        static FrameworkElement Row(string name, FrameworkElement content) =>
            new StackPanel()
                .Vertical()
                .Spacing(4)
                .Children(
                    new TextBlock().Text(name).FontSize(ThemeFontSize.Small),
                    content);

        // ButtonGroup of icon + text segments (recycle-safe template).
        static ButtonGroup IconTextGroup(params SegmentItem[] items) =>
            new ButtonGroup()
                .Items(items, x => x.Label)
                .ItemTemplate<SegmentItem>(
                    build: ctx =>
                    {
                        var icon = SegmentIconShape(16).CenterVertical();
                        var label = new TextBlock().CenterVertical();
                        ctx.Register("icon", icon);
                        ctx.Register("label", label);
                        return new StackPanel().Horizontal().Spacing(6).Center().Children(icon, label);
                    },
                    bind: (view, item, _, ctx) =>
                    {
                        ctx.Get<PathShape>("icon").Data = SegmentIcon(item.Icon);
                        ctx.Get<TextBlock>("label").Text = item.Label;
                    });

        // ButtonGroup of icon-only segments.
        static ButtonGroup IconGroup(params SegmentItem[] items) =>
            new ButtonGroup()
                .Items(items, x => x.Label)
                .ItemTemplate<SegmentItem>(
                    build: _ => SegmentIconShape(18).Center(),
                    bind: (view, item, _, _) => ((PathShape)view).Data = SegmentIcon(item.Icon));

        // Command-owned icon: each presenter gets a fresh visual while the frozen geometry is shared.
        static IconTemplate CommandIcon(string name)
        {
            var geometry = SegmentIcon(name);
            geometry.Freeze();
            return new IconTemplate(size =>
            {
                var icon = SegmentIconShape(size.Dip);
                icon.Data = geometry;
                return icon;
            });
        }

        // Drop-down family: the split button keeps a primary command, the drop-down button has none.
        // Save is gated by the checkbox below so the disabled primary still leaves the menu reachable,
        // and Save All stays unavailable and iconless to exercise mixed command presentation rows.
        var save = new Command("gallery.save", "Save");
        var saveAs = new Command("gallery.saveAs", "Save _As...");
        var saveAll = new Command("gallery.saveAll", "Save All");
        var exportPdf = new Command("gallery.exportPdf", "Export PDF");
        var print = new Command("gallery.print", "_Print");
        bool canSave = true;

        var splitButton = new SplitButton
        {
            Command = save
        }
            .DropDownMenu(new Menu().Item(saveAs).Item(saveAll))
            .Left()
            .Content(new TextBlock().Text("Save"));
        splitButton.Commands.Register(save, () => _ = MessageBox.NotifyAsync("Save"), () => canSave);
        splitButton.Commands.Register(saveAs, () => _ = MessageBox.NotifyAsync("Save As"));
        splitButton.Commands.Register(saveAll, () => _ = MessageBox.NotifyAsync("Save All"), () => false);

        // The dispatcher re-evaluates command state after each drain, so flipping the flag is enough.
        var saveGate = new CheckBox { Content = new TextBlock().Text("Save can execute"), IsChecked = true };
        saveGate.CheckedChanged += value => canSave = value == true;

        var dropDownButton = new DropDownButton()
            .DropDownMenu(new Menu().Item(exportPdf).Separator().Item(print))
            .Content(new TextBlock().Text("More actions"))
            .Left();
        dropDownButton.Commands.Register(exportPdf, () => _ = MessageBox.NotifyAsync("Export PDF"));
        dropDownButton.Commands.Register(print, () => _ = MessageBox.NotifyAsync("Print"));

        // Separate Command-presentation case: the SplitButton primary face and its menu rows both
        // materialize text and icons from Commands. Menu shortcuts come from the effective InputMap.
        var commandSave = new Command(
            "gallery.commandSplit.save",
            "_Save",
            CommandIcon("save_regular"));
        var newDocument = new Command(
            "gallery.commandSplit.new",
            "_New",
            CommandIcon("document_add_regular"));
        var saveCopy = new Command(
            "gallery.commandSplit.saveCopy",
            "Save a _Copy",
            CommandIcon("save_copy_regular"));
        var commandPrint = new Command(
            "gallery.commandSplit.print",
            "_Print",
            CommandIcon("print_regular"));
        var commandSplitButton = new SplitButton
        {
            Command = commandSave,
        }
            .DropDownMenu(new Menu().Item(newDocument).Item(saveCopy).Separator().Item(commandPrint))
            .Left();
        commandSplitButton.Commands.Register(
            commandSave,
            () => _ = MessageBox.NotifyAsync("Save"));
        commandSplitButton.Commands.Register(
            newDocument,
            () => _ = MessageBox.NotifyAsync("New document"));
        commandSplitButton.Commands.Register(
            saveCopy,
            () => _ = MessageBox.NotifyAsync("Save a Copy"));
        commandSplitButton.Commands.Register(
            commandPrint,
            () => _ = MessageBox.NotifyAsync("Print"));
        commandSplitButton.InputMap.Map(
            commandSave,
            new KeyGesture(Key.S, ModifierKeys.Primary));
        commandSplitButton.InputMap.Map(
            newDocument,
            new KeyGesture(Key.N, ModifierKeys.Primary));
        commandSplitButton.InputMap.Map(
            saveCopy,
            new KeyGesture(Key.S, ModifierKeys.Primary | ModifierKeys.Shift));
        commandSplitButton.InputMap.Map(
            commandPrint,
            new KeyGesture(Key.P, ModifierKeys.Primary));

        return CardGrid(
            Card(
                "Buttons",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button().Content("Default"),
                        new Button().Content("Disabled").Disable(),
                        new Button()
                            .Content("Double Click")
                            .OnDoubleClick(() => _ = MessageBox.NotifyAsync("Double Click"))
                    )
            ),

            Card(
                "Built-in Styles",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button().Content("Flat Button").Apply(b => b.StyleName = BuiltInStyles.FlatButton),
                        new Button().Content("Flat Disabled").Apply(b => b.StyleName = BuiltInStyles.FlatButton).Disable(),
                        new Button().Content("Accent Button").Apply(b => b.StyleName = BuiltInStyles.AccentButton),
                        new Button().Content("Accent Disabled").Apply(b => b.StyleName = BuiltInStyles.AccentButton).Disable()
                    )
            ),

            Card(
                "ToggleButton",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new ToggleButton().Content("Toggle"),
                        new ToggleButton().Content("Checked").IsChecked(true),
                        new ToggleButton().Content("Disabled").Disable(),
                        new ToggleButton().Content("Disabled (Checked)").IsChecked(true).Disable()
                    )
            ),

            Card(
                "Drop-down buttons",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        Row("DropDownButton (menu only)", dropDownButton),
                        Row("SplitButton (primary + menu)", splitButton),
                        Row("SplitButton command presentation", commandSplitButton),
                        saveGate
                    )
            ),

            Card(
                "ButtonGroup",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        Row("Text",
                            new ButtonGroup().Items("Cut", "Copy", "Paste").Left()),

                        Row("Text + Icon",
                            IconTextGroup(
                                new SegmentItem("cut_regular", "Cut"),
                                new SegmentItem("copy_regular", "Copy"),
                                new SegmentItem("clipboard_paste_regular", "Paste")).Left()),

                        Row("Icon",
                            IconGroup(
                                new SegmentItem("text_align_left_regular", "Left"),
                                new SegmentItem("text_align_center_regular", "Center"),
                                new SegmentItem("text_align_right_regular", "Right")).Left()),

                        Row("Toggle",
                            new ButtonGroup()
                                .Items("Bold", "Italic", "Underline")
                                .PrepareContainer<string>((seg, name, _) =>
                                {
                                    seg.IsCheckable = true;
                                    if (name == "Italic") seg.IsChecked = true;
                                })
                                .Left()),

                        Row("Uniform",
                            new ButtonGroup()
                                .Sizing(SegmentSizing.Uniform)
                                .Items("Left", "Center", "Right")
                                .Left()),

                        Row("Disabled",
                            IconGroup(
                                new SegmentItem("text_align_left_regular", "Left"),
                                new SegmentItem("text_align_center_regular", "Center"),
                                new SegmentItem("text_align_right_regular", "Right"))
                                .Disable()
                                .Left())
                    )
            ),

            Card(
                "Toggle / Switch",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new ToggleSwitch().IsChecked(true),
                        new ToggleSwitch().IsChecked(false),
                        new ToggleSwitch().IsChecked(true).Disable(),
                        new ToggleSwitch().IsChecked(false).Disable()
                    )
            ),

            Card(
                "Progress",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new ProgressBar().Value(20),
                        new ProgressBar().Value(65),
                        new ProgressBar().Value(65).Disable(),
                        new ProgressBar().IsIndeterminate(),
                        new Slider().Minimum(0).Maximum(100).Value(25),
                        new Slider().Minimum(0).Maximum(100).Value(25).Disable()
                    )
            )
        );
    }
}
