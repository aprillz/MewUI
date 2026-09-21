using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement TogglesPage()
    {
        var doneEnabled = new ObservableValue<bool>(false);

        return CardGrid(
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
                "ToggleSwitch",
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
                "CheckBox",
                new Grid()
                    .Columns("Auto,Auto")
                    .Rows("Auto,Auto,Auto")
                    .Spacing(8)
                    .Children(
                        new CheckBox()
                            .Content("CheckBox"),

                        new CheckBox()
                            .Content("Disabled")
                            .Disable(),

                        new CheckBox()
                            .Content("Checked")
                            .IsChecked(true),

                        new CheckBox()
                            .Content("Disabled (Checked)")
                            .IsChecked(true)
                            .Disable(),

                        new CheckBox()
                            .Content("Three-state")
                            .IsThreeState(true)
                            .IsChecked(null),

                        new CheckBox()
                            .Content("Disabled (Indeterminate)")
                            .IsThreeState(true)
                            .IsChecked(null)
                            .Disable()
                    )
            ),

            Card(
                "RadioButton",
                new Grid()
                    .Columns("Auto,Auto")
                    .Rows("Auto,Auto")
                    .Spacing(8)
                    .Children(
                        new RadioButton()
                            .Content("A")
                            .GroupName("g"),

                        new RadioButton()
                            .Content("C (Disabled)")
                            .GroupName("g2")
                            .Disable(),

                        new RadioButton()
                            .Content("B")
                            .GroupName("g")
                            .IsChecked(true),

                        new RadioButton()
                            .Content("Disabled (Checked)")
                            .GroupName("g2")
                            .IsChecked(true)
                            .Disable()
                    )
            ),

            Card(
                "SegmentedControl",
                new StackPanel()
                    .Vertical()
                    .Spacing(12)
                    .Children(
                        // Text only.
                        new StackPanel()
                            .Vertical()
                            .Spacing(4)
                            .Children(
                                new TextBlock().Text("Text").FontSize(ThemeFontSize.Small),
                                new SegmentedControl()
                                    .Items("Day", "Week", "Month")
                                    .SelectedIndex(0)),

                        // Text + icon.
                        new StackPanel()
                            .Vertical()
                            .Spacing(4)
                            .Children(
                                new TextBlock().Text("Text + Icon").FontSize(ThemeFontSize.Small),
                                new SegmentedControl()
                                    .Items(
                                        new[]
                                        {
                                            new SegmentItem("apps_list_regular", "List"),
                                            new SegmentItem("table_regular", "Table"),
                                            new SegmentItem("data_pie_regular", "Chart"),
                                        },
                                        v => v.Label)
                                    .ItemTemplate<SegmentItem>(
                                        build: ctx =>
                                        {
                                            var icon = SegmentIconShape(16).CenterVertical();
                                            var label = new TextBlock().CenterVertical();
                                            ctx.Register("icon", icon);
                                            ctx.Register("label", label);
                                            return new StackPanel()
                                                .Horizontal()
                                                .Spacing(6)
                                                .Center()
                                                .Children(icon, label);
                                        },
                                        bind: (view, item, _, ctx) =>
                                        {
                                            BindNamedIcon(ctx.Get<PathShape>("icon"), item.Icon);
                                            ctx.Get<TextBlock>("label").Text = item.Label;
                                        })
                                    .SelectedIndex(0)),

                        // Icon only.
                        new StackPanel()
                            .Vertical()
                            .Spacing(4)
                            .Children(
                                new TextBlock().Text("Icon").FontSize(ThemeFontSize.Small),
                                new SegmentedControl()
                                    .Items(
                                        new[]
                                        {
                                            new SegmentItem("home_regular", "Home"),
                                            new SegmentItem("settings_regular", "Settings"),
                                            new SegmentItem("calendar_regular", "Calendar"),
                                        },
                                        v => v.Label)
                                    .ItemTemplate<SegmentItem>(
                                        build: _ => SegmentIconShape(16).Center(),
                                        bind: (view, item, _, _) => BindNamedIcon((PathShape)view, item.Icon))
                                    .SelectedIndex(1)),

                        // One segment enabled via binding (PrepareContainer + BindIsEnabled).
                        new StackPanel()
                            .Vertical()
                            .Spacing(4)
                            .Children(
                                new TextBlock().Text("Disabled segment (bound)").FontSize(ThemeFontSize.Small),
                                new SegmentedControl()
                                    .Items("All", "Active", "Done")
                                    .PrepareContainer<string>((c, item, _) =>
                                    {
                                        if (item == "Done") c.BindIsEnabled(doneEnabled);
                                    })
                                    .SelectedIndex(0),
                                new CheckBox().Content("Enable ‘Done’").BindIsChecked(doneEnabled)),

                        // Whole control disabled.
                        new StackPanel()
                            .Vertical()
                            .Spacing(4)
                            .Children(
                                new TextBlock().Text("Disabled").FontSize(ThemeFontSize.Small),
                                new SegmentedControl()
                                    .Items("Day", "Week", "Month")
                                    .SelectedIndex(0)
                                    .Disable())
                    ),
                minWidth: 320
            )
        );
    }
}
