using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement SelectionPage()
    {
        var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").Append("Item Long Long Long Long Long Long Long").ToArray();
        Calendar calendar = null!;

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
                            new TabItem().Header(Header("Home")).Content(new TextBlock().Text("Home tab content")),
                            new TabItem().Header(Header("Settings")).Content(new TextBlock().Text("Settings tab content")),
                            new TabItem().Header(Header("About")).Content(new TextBlock().Text("About tab content"))
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
                        new CheckBox().Content("CheckBox"),
                        new CheckBox().Content("Disabled").Disable(),
                        new CheckBox().Content("Checked").IsChecked(true),
                        new CheckBox().Content("Disabled (Checked)").IsChecked(true).Disable(),
                        new CheckBox().Content("Three-state").IsThreeState(true).IsChecked(null),
                        new CheckBox().Content("Disabled (Indeterminate)").IsThreeState(true).IsChecked(null).Disable()
                    )
            ),

            Card(
                "RadioButton",
                new Grid()
                    .Columns("Auto,Auto")
                    .Rows("Auto,Auto")
                    .Spacing(8)
                    .Children(
                        new RadioButton().Content("A").GroupName("g"),
                        new RadioButton().Content("C (Disabled)").GroupName("g2").Disable(),
                        new RadioButton().Content("B").GroupName("g").IsChecked(true),
                        new RadioButton().Content("Disabled (Checked)").GroupName("g2").IsChecked(true).Disable()
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
                        new DatePicker().Placeholder("Select a date..."),
                        new DatePicker().SelectedDate(DateTime.Today),
                        new DatePicker().Placeholder("Disabled").Disable()
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
                        new TextBlock().Text("Both"),
                        new ColorPicker().SelectedColor(Color.FromRgb(255, 0, 0)),
                        new TextBlock().Text("Wheel"),
                        new ColorPicker().SelectedColor(Color.FromRgb(0, 128, 255)).Kind(ColorPickerKind.Wheel),
                        new TextBlock().Text("Panel"),
                        new ColorPicker().SelectedColor(Color.FromRgb(0, 200, 100)).Kind(ColorPickerKind.Panel),
                        new TextBlock().Text("Alpha"),
                        new ColorPicker().SelectedColor(Color.FromArgb(180, 255, 128, 0)).ShowAlpha(),
                        new TextBlock().Text("Disabled"),
                        new ColorPicker().SelectedColor(Color.FromRgb(80, 80, 80)).Disable()
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
}
