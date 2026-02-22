using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private ObservableValue<string> name = new ObservableValue<string>("This is my name");
    private ObservableValue<int> intBinding = new ObservableValue<int>(1);
    private ObservableValue<double> doubleBinding = new ObservableValue<double>(42.5);

    private FrameworkElement InputsPage() =>
            CardGrid(
                Card(
                    "TextBox",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBox(),
                            new TextBox().Placeholder("Type your name..."),
                            new TextBox().BindText(name),
                            new TextBox().Text("Disabled").Disable()
                        )
                ),

                Card(
                    "NumericUpDown (int/double)",
                    new Grid()
                        .Columns("Auto,Auto,Auto")
                        .Rows("Auto,Auto")
                        .Spacing(8)
                        .AutoIndexing()
                        .Children(
                            new Label()
                                .Text("Int")
                                .CenterVertical(),

                            new NumericUpDown()
                                .Width(140)
                                .Minimum(0)
                                .Maximum(100)
                                .Step(1)
                                .Format("0")
                                .BindValue(intBinding)
                                .CenterVertical(),

                            new Label()
                                .BindText(intBinding, value => $"Value: {value}")
                                .CenterVertical(),

                            new Label()
                                .Text("Double")
                                .CenterVertical(),

                            new NumericUpDown()
                                .Width(140)
                                .Minimum(0)
                                .Maximum(100)
                                .Step(0.1)
                                .Format("0.##")
                                .BindValue(doubleBinding)
                                .CenterVertical(),

                            new Label()
                                .BindText(doubleBinding, value => $"Value: {value:0.##}")
                                .CenterVertical()
                        )
                ),

                Card(
                    "MultiLineTextBox",
                    new MultiLineTextBox()
                        .Height(120)
                        .Text("The quick brown fox jumps over the lazy dog.\n\n- Wrap supported\n- Selection supported\n- Scroll supported")
                ),

                Card(
                    "ToolTip / ContextMenu",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new Label()
                                .Text("Hover to show a tooltip. Right-click to open a context menu.")
                                .FontSize(11),

                            new Button()
                                .Content("Hover / Right-click me")
                                .ToolTip("ToolTip text")
                                .ContextMenu(
                                    new ContextMenu()
                                        .Item("Copy", "Ctrl+C")
                                        .Item("Paste", "Ctrl+V")
                                        .Separator()
                                        .SubMenu("Transform", new ContextMenu()
                                            .Item("Uppercase")
                                            .Item("Lowercase")
                                            .Separator()
                                            .SubMenu("More", new ContextMenu()
                                                .Item("Trim")
                                                .Item("Normalize")
                                                .Item("Sort"))
                                        )
                                        .SubMenu("View", new ContextMenu()
                                            .Item("Zoom In", "Ctrl++")
                                            .Item("Zoom Out", "Ctrl+-")
                                            .Item("Reset Zoom", "Ctrl+0")
                                        )
                                        .Separator()
                                        .Item("Disabled", isEnabled: false)
                                )
                         )
                 )
             );
}
