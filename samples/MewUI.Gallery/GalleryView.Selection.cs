using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement SelectionPage()
    {
        var items = Enumerable
            .Range(1, 5)
            .Select(i => $"Item {i}")
            .Append("Item Long Long Long Long Long Long Long")
            .ToArray();
            
        Calendar calendar = null!;

        var doneEnabled = new ObservableValue<bool>(false);

        FrameworkElement TabPlacementSample(
            string title,
            TabPlacement placement,
            Rotation? headerRotation = null)
        {
            Element Header(string text)
            {
                var label = new TextBlock().Text(text);
                return headerRotation is { } rotation
                    ? new RotationDecorator()
                        .Rotation(rotation)
                        .Child(label)
                    : label;
            }

            return new StackPanel()
                .Vertical()
                .Spacing(4)
                .Children(
                    new TextBlock()
                        .Text(title)
                        .FontSize(11),
                    new TabControl()
                        .Height(headerRotation is null ? 140 : 180)
                        .TabPlacement(placement)
                        .TabItems(
                            new TabItem()
                                .Header(Header("Home"))
                                .Content(
                                    new TextBlock()
                                        .Text("Home tab content")
                                ),

                            new TabItem()
                                .Header(Header("Settings"))
                                .Content(
                                    new TextBlock()
                                        .Text("Settings tab content")
                                ),

                            new TabItem()
                                .Header(Header("About"))
                                .Content(
                                    new TextBlock()
                                        .Text("About tab content")
                                )
                        )
                );
        }

        return CardGrid(
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
                "ComboBox",
                new StackPanel()
                    .Vertical()
                    .Width(200)
                    .Spacing(8)
                    .Children(
                        new ComboBox()
                            .Items(["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa"])
                            .SelectedIndex(1),

                        new ComboBox()
                            .Placeholder("Select an item...")
                            .Items(items),

                        new ComboBox()
                            .Items(items)
                            .SelectedIndex(1)
                            .Disable()
                    ),
                minWidth: 250
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
                                new TextBlock().Text("Text").FontSize(11),
                                new SegmentedControl()
                                    .Items("Day", "Week", "Month")
                                    .SelectedIndex(0)),

                        // Text + icon.
                        new StackPanel()
                            .Vertical()
                            .Spacing(4)
                            .Children(
                                new TextBlock().Text("Text + Icon").FontSize(11),
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
                                            ctx.Get<PathShape>("icon").Data = SegmentIcon(item.Icon);
                                            ctx.Get<TextBlock>("label").Text = item.Label;
                                        })
                                    .SelectedIndex(0)),

                        // Icon only.
                        new StackPanel()
                            .Vertical()
                            .Spacing(4)
                            .Children(
                                new TextBlock().Text("Icon").FontSize(11),
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
                                        build: _ => SegmentIconShape(18).Center(),
                                        bind: (view, item, _, _) => ((PathShape)view).Data = SegmentIcon(item.Icon))
                                    .SelectedIndex(1)),

                        // One segment enabled via binding (PrepareContainer + BindIsEnabled).
                        new StackPanel()
                            .Vertical()
                            .Spacing(4)
                            .Children(
                                new TextBlock().Text("Disabled segment (bound)").FontSize(11),
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
                                new TextBlock().Text("Disabled").FontSize(11),
                                new SegmentedControl()
                                    .Items("Day", "Week", "Month")
                                    .SelectedIndex(0)
                                    .Disable())
                    ),
                minWidth: 320
            ),

            Card(
                "Calendar",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Calendar()
                            .Ref(out calendar),

                        new TextBlock()
                            .Bind(TextBlock.TextProperty, calendar, Calendar.SelectedDateProperty, x => $"Selected: {x:yyyy-MM-dd}")
                    )
            ),

            Card(
                "DatePicker",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new DatePicker()
                            .Placeholder("Select a date..."),

                        new DatePicker()
                            .SelectedDate(DateTime.Today),

                        new DatePicker()
                            .Placeholder("Disabled")
                            .Disable()
                    ),
                minWidth: 250
            ),

            Card(
                "ColorPicker",
                new Grid()
                    .Rows("Auto,Auto,Auto,Auto,Auto")
                    .Columns("Auto,*")
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("Both"),

                        new ColorPicker()
                            .SelectedColor(Color.FromRgb(255, 0, 0)),

                        new TextBlock()
                            .Text("Wheel"),

                        new ColorPicker()
                            .SelectedColor(Color.FromRgb(0, 128, 255))
                            .Kind(ColorPickerKind.Wheel),

                        new TextBlock()
                            .Text("Panel"),

                        new ColorPicker()
                            .SelectedColor(Color.FromRgb(0, 200, 100))
                            .Kind(ColorPickerKind.Panel),

                        new TextBlock()
                            .Text("Alpha"),

                        new ColorPicker()
                            .SelectedColor(Color.FromArgb(180, 255, 128, 0))
                            .ShowAlpha(),

                        new TextBlock()
                            .Text("Disabled"),

                        new ColorPicker()
                            .SelectedColor(Color.FromRgb(80, 80, 80))
                            .Disable()
                    ),
                minWidth: 250
            ),

            Card(
                "TabControl",
                new UniformGrid()
                    .Columns(2)
                    .Spacing(8)
                    .Children(
                        TabPlacementSample("Top", TabPlacement.Top),
                        TabPlacementSample("Bottom", TabPlacement.Bottom),
                        TabPlacementSample("Left", TabPlacement.Left),
                        TabPlacementSample("Right", TabPlacement.Right)
                    ),
                minWidth: 700
            ),

            Card(
                "TabControl Overflow",
                new TabControl()
                    .Width(260)
                    .Height(140)
                    .TabItems(
                        new TabItem()
                            .Header("Overview")
                            .Content(new TextBlock().Text("Overview content")),
                        new TabItem()
                            .Header("Rendering Pipeline")
                            .Content(new TextBlock().Text("Rendering Pipeline content")),
                        new TabItem()
                            .Header("Input Routing")
                            .Content(new TextBlock().Text("Input Routing content")),
                        new TabItem()
                            .Header("Property Binding")
                            .Content(new TextBlock().Text("Property Binding content")),
                        new TabItem()
                            .Header("Disabled Diagnostics")
                            .Content(new TextBlock().Text("Disabled Diagnostics content"))
                            .IsEnabled(false),
                        new TabItem()
                            .Header("Final Review")
                            .Content(new TextBlock().Text("Final Review content")))
                    .SelectedIndex(5),
                minWidth: 340
            ),

            Card(
                "TabControl + RotationDecorator",
                new UniformGrid()
                    .Columns(2)
                    .Spacing(8)
                    .Children(
                        TabPlacementSample(
                            "Left",
                            TabPlacement.Left,
                            Rotation.CounterClockwise90),
                        TabPlacementSample(
                            "Right",
                            TabPlacement.Right,
                            Rotation.Clockwise90)
                    ),
                minWidth: 700
            )
        );
    }

    private sealed record SegmentItem(string Icon, string Label);

    // Binds the icon fill to the inherited Foreground, so it follows selection, theme, and disabled
    // dimming exactly like the text label. Inherited-value changes now notify property bindings, so
    // this stays in sync; SolidColorBrush is a lightweight, non-disposable value descriptor.
    private static PathShape SegmentIconShape(double size)
    {
        var shape = new PathShape()
            .Stretch(Stretch.Uniform)
            .Width(size).Height(size);

        shape.Bind(Shape.FillProperty, shape, Control.ForegroundProperty,
            (Color color) => new SolidColorBrush(color));
        return shape;
    }

    private static PathGeometry SegmentIcon(string name)
    {
        var all = IconResource.GetAll();
        var entry = Array.Find(all, e => e.Name == name) ?? all[0];
        return PathGeometry.Parse(entry.PathData);
    }
}
