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
                    new TextBlock().Text(name).FontSize(11),
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
                        new Slider().Minimum(0).Maximum(100).Value(25),
                        new Slider().Minimum(0).Maximum(100).Value(25).Disable()
                    )
            )
        );
    }
}

