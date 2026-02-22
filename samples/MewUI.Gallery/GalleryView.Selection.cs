using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement SelectionPage() =>
        CardGrid(
            Card(
                "CheckBox",
                new Grid()
                    .Columns("Auto,Auto")
                    .Rows("Auto,Auto,Auto")
                    .Spacing(8)
                    .Children(
                        new CheckBox().Text("CheckBox"),
                        new CheckBox().Text("Disabled").Disable(),
                        new CheckBox().Text("Checked").IsChecked(true),
                        new CheckBox().Text("Disabled (Checked)").IsChecked(true).Disable(),
                        new CheckBox().Text("Three-state").IsThreeState(true).IsChecked(null),
                        new CheckBox().Text("Disabled (Indeterminate)").IsThreeState(true).IsChecked(null).Disable()
                    )
            ),

            Card(
                "RadioButton",
                new Grid()
                    .Columns("Auto,Auto")
                    .Rows("Auto,Auto")
                    .Spacing(8)
                    .Children(
                        new RadioButton().Text("A").GroupName("g"),
                        new RadioButton().Text("C (Disabled)").GroupName("g2").Disable(),
                        new RadioButton().Text("B").GroupName("g").IsChecked(true),
                        new RadioButton().Text("Disabled (Checked)").GroupName("g2").IsChecked(true).Disable()
                    )
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
                                new TabItem().Header("Home").Content(new Label().Text("Home tab content")),
                                new TabItem().Header("Settings").Content(new Label().Text("Settings tab content")),
                                new TabItem().Header("About").Content(new Label().Text("About tab content"))
                            ),

                        new TabControl()
                            .Height(120)
                            .Disable()
                            .TabItems(
                                new TabItem().Header("Home").Content(new Label().Text("Home tab content")),
                                new TabItem().Header("Settings").Content(new Label().Text("Settings tab content")),
                                new TabItem().Header("About").Content(new Label().Text("About tab content"))
                            )
                    )
            )
        );
}
