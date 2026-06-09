using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private ObservableValue<string> name = new ObservableValue<string>("This is my name");
    private ObservableValue<int> intBinding = new ObservableValue<int>(1);
    private ObservableValue<double> doubleBinding = new ObservableValue<double>(42.5);

    // Multi-line text box demo that shows the live selection (start / length) bound to the read-only
    // SelectionStart/SelectionLength MewProperties — used to inspect selection geometry.
    private FrameworkElement MultiLineTextBoxDemo()
    {
        var box = new MultiLineTextBox()
            .Height(120)
            .Width(290)
            .Wrap(false)
            .Text("The quick brown fox jumps over the lazy dog.\n\n- Wrap supported\n- Selection supported\n- Scroll supported");

        return new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                box,
                new TextBlock()
                    .FontSize(11)
                    .Bind(TextBlock.TextProperty, box, TextBase.SelectionStartProperty,
                        (int start) => $"selection start: {start}"),
                new TextBlock()
                    .FontSize(11)
                    .Bind(TextBlock.TextProperty, box, TextBase.SelectionLengthProperty,
                        (int length) => $"selection length: {length}")
            );
    }

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
                    "PasswordBox",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new PasswordBox().Placeholder("Password"),
                            new PasswordBox { PasswordChar = '★' }.Placeholder("Custom mask"),
                            new PasswordBox().Password("Disabled").Disable()
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
                            new TextBlock()
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

                            new TextBlock()
                                .BindText(intBinding, value => $"Value: {value}")
                                .CenterVertical(),

                            new TextBlock()
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

                            new TextBlock()
                                .BindText(doubleBinding, value => $"Value: {value:0.##}")
                                .CenterVertical()
                        )
                ),

                Card(
                    "Emoji",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(24),
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(20),
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(16),
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(12),
                            new TextBox()
                                .Placeholder("Type or paste emoji here...")
                                .Text("\U0001F36B\U0001F600\U0001F389"),
                            new TextBlock()
                                .Text("Mixed: Hello \U0001F30D World \U0001F680!")
                                .FontSize(14)
                        )
                ),

                Card(
                    "MultiLineTextBox",
                    MultiLineTextBoxDemo()
                ),

                Card(
                    "ToolTip / ContextMenu",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .Text("Hover to show a tooltip. Right-click to open a context menu.")
                                .TextWrapping(TextWrapping.Wrap)
                                .Width(290)
                                .FontSize(11),

                            new Button()
                                .Content("Hover / Right-click me")
                                .ToolTip("ToolTip text")
                                .ContextMenu(
                                    new ContextMenu()
                                        .Item("Copy", new KeyGesture(Key.C, ModifierKeys.Primary))
                                        .Item("Paste", new KeyGesture(Key.V, ModifierKeys.Primary))
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
                                            .Item("Zoom In", new KeyGesture(Key.Add, ModifierKeys.Primary))
                                            .Item("Zoom Out", new KeyGesture(Key.Subtract, ModifierKeys.Primary))
                                            .Item("Reset Zoom", new KeyGesture(Key.D0, ModifierKeys.Primary))
                                        )
                                        .Separator()
                                        .Item("Disabled", isEnabled: false)
                                )
                         )
                 ),

                WindowDragDropCard(),

                ElementDragDropCard(),

                CrossWindowDragDropCard()
             );
}
