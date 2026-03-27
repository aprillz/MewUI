using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement SelectionPage()
    {
        var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").Append("Item Long Long Long Long Long Long Long").ToArray();
        Calendar calendar = null!;

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
                            .Bind(TextBlock.TextProperty, calendar, Calendar.SelectedDateProperty, x => $"Selected: ({x:yyyy-MM-dd})")
                    )
            ),

            Card(
                "DatePicker",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new DatePicker { Placeholder = "Select a date..." },
                        new DatePicker { SelectedDate = DateTime.Today },
                        new DatePicker { Placeholder = "Disabled" }.Disable()
                    ),
                minWidth: 250
            ),

            Card(
                "TabControl",
                new UniformGrid()
                    .Columns(2)
                    .Spacing(8)
                    .Children(
                        new TabControl()
                            .Height(120)
                            .TabItems(
                                new TabItem().Header("_Home").Content(new TextBlock().Text("Home tab content")),
                                new TabItem().Header("Se_ttings").Content(new TextBlock().Text("Settings tab content")),
                                new TabItem().Header("A_bout").Content(new TextBlock().Text("About tab content"))
                            ),

                        new TabControl()
                            .Height(120)
                            .Disable()
                            .TabItems(
                                new TabItem().Header("Home").Content(new TextBlock().Text("Home tab content")),
                                new TabItem().Header("Settings").Content(new TextBlock().Text("Settings tab content")),
                                new TabItem().Header("About").Content(new TextBlock().Text("About tab content"))
                            )
                    )
            )
        );
    }
}

