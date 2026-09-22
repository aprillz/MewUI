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
                        BindNamedIcon(ctx.Get<PathShape>("icon"), item.Icon);
                        ctx.Get<TextBlock>("label").Text = item.Label;
                    });

        // ButtonGroup of icon-only segments.
        static ButtonGroup IconGroup(params SegmentItem[] items) =>
            new ButtonGroup()
                .Items(items, x => x.Label)
                .ItemTemplate<SegmentItem>(
                    build: _ => SegmentIconShape(16).Center(),
                    bind: (view, item, _, _) => BindNamedIcon((PathShape)view, item.Icon));

        // Command-owned icon: each presenter gets a fresh visual, and the icon is looked up when that
        // visual is created rather than here, so a late-arriving icon dictionary still reaches it.
        static IconTemplate CommandIcon(string name)
            => new IconTemplate(size =>
            {
                var icon = SegmentIconShape(size.Dip);
                BindNamedIcon(icon, name);
                return icon;
            });

        // The page is the command scope: the drop-down and split button cards both register on it, so one
        // element owns the handlers, the gate and the shortcut map.
        Border commandScope = new();

        // What ran goes to a line under each card rather than a message box: a dialog takes the focus the
        // buttons are being tried with, and a menu row that runs on close would be judged by the dialog.
        var lastRun = new ObservableValue<string>("Nothing run yet");
        void Log(string what) => lastRun.Value = $"Ran: {what}";
        TextBlock RunLog() => new TextBlock().BindText(lastRun);

        // One set of commands for every button below: what differs between them is how a button presents a
        // command, not the command. Save All carries no handler of its own so a dead menu row shows too.
        var save = new Command("gallery.save", "_Save", CommandIcon("save_regular"));
        var newDocument = new Command("gallery.new", "_New", CommandIcon("document_add_regular"));
        var saveAs = new Command("gallery.saveAs", "Save _As...");
        var saveCopy = new Command("gallery.saveCopy", "Save a _Copy", CommandIcon("save_copy_regular"));
        var saveAll = new Command("gallery.saveAll", "Save All");
        var exportPdf = new Command("gallery.exportPdf", "Export PDF");
        var print = new Command("gallery.print", "_Print", CommandIcon("print_regular"));
        bool canSave = true;

        // Save and Save As share the gate: the primary face and a menu row grey out together, so the
        // checkbox shows command state reaching both surfaces.
        commandScope.Commands.Register(save, () => Log("Save"), () => canSave);
        commandScope.Commands.Register(saveAs, () => Log("Save As"), () => canSave);
        commandScope.Commands.Register(newDocument, () => Log("New document"));
        commandScope.Commands.Register(saveCopy, () => Log("Save a Copy"));
        commandScope.Commands.Register(saveAll, () => Log("Save All"), () => false);
        commandScope.Commands.Register(exportPdf, () => Log("Export PDF"));
        commandScope.Commands.Register(print, () => Log("Print"));

        commandScope.InputMap.Map(save, new KeyGesture(Key.S, ModifierKeys.Primary));
        commandScope.InputMap.Map(newDocument, new KeyGesture(Key.N, ModifierKeys.Primary));
        commandScope.InputMap.Map(saveCopy, new KeyGesture(Key.S, ModifierKeys.Primary | ModifierKeys.Shift));
        commandScope.InputMap.Map(print, new KeyGesture(Key.P, ModifierKeys.Primary));

        // The checkbox gates Save and Save As wherever they appear: the dispatcher re-evaluates command
        // state after each drain, so flipping the flag is enough.
        var saveGate = new CheckBox
        {
            Content = new TextBlock().Text("Save / Save As can execute"),
            IsChecked = true,
        };
        saveGate.CheckedChanged += value => canSave = value == true;

        // One menu for every button here, mixing rows with and without an icon, one that cannot run, and
        // one carrying a shortcut. A menu belongs to the button that opens it, so each gets its own.
        Menu CommandMenu() => new Menu()
            .Item(newDocument)
            .Item(saveAs)
            .Item(saveCopy)
            .Separator()
            .Item(saveAll)
            .Item(print);

        // Content given directly: the button says what it shows, and a disabled primary still leaves the
        // menu reachable.
        var splitButton = new SplitButton()
            .DropDownMenu(CommandMenu())
            .Command(save)
            .Content("Save")
            .Left();

        // No primary action at all: every part of it opens the menu.
        var dropDownButton = new DropDownButton()
            .DropDownMenu(new Menu().Item(exportPdf).Separator().Item(print))
            .Content("More actions")
            .Left();

        // The face and the menu rows materialize text and icons from the Commands themselves, and the menu
        // shortcuts come from the effective InputMap. The accent one differs only by its style.
        var presentedSplitButton = new SplitButton()
            .Command(save, CommandPresentationMode.TextAndIcon)
            .DropDownMenu(CommandMenu())
            .Left();

        var accentSplitButton = new SplitButton()
            .StyleName(BuiltInStyles.AccentSplitButton)
            .Command(save, CommandPresentationMode.TextAndIcon)
            .DropDownMenu(CommandMenu())
            .Left();

        return commandScope.Child(CardGrid(
            Card(
                "Button",
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

            // The style is named once on the panel, so every Button under it takes the look and the buttons
            // themselves say nothing about which one they have.
            Card(
                "Built-in Styles",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new StackPanel()
                            .Vertical()
                            .Spacing(8)
                            .StyleSheet(new StyleSheet().WithName<Button>(BuiltInStyles.FlatButton))
                            .Children(
                                new Button().Content("Flat Button"),
                                new Button().Content("Flat Disabled").Disable()),

                        new StackPanel()
                            .Vertical()
                            .Spacing(8)
                            .StyleSheet(new StyleSheet().WithName<Button>(BuiltInStyles.AccentButton))
                            .Children(
                                new Button().Content("Accent Button"),
                                new Button().Content("Accent Disabled").Disable())
                    )
            ),

            Card(
                "DropDownButton",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        Row("Menu only", dropDownButton),
                        RunLog()
                    )
            ),

            Card(
                "SplitButton",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        Row("Primary + menu", new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(splitButton, saveGate.CenterVertical())),
                        Row("Command presentation", new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(presentedSplitButton, accentSplitButton)),
                        RunLog()
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
            )
        ));
    }
}
