using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private ObservableValue<int> intBinding = new ObservableValue<int>(1);
    private ObservableValue<double> doubleBinding = new ObservableValue<double>(42.5);

    private FrameworkElement RangePage() =>
        CardGrid(
            Card(
                "NumericUpDown (int/double)",
                new Grid()
                    .Columns("Auto,Auto,Auto")
                    .Rows("Auto,Auto,Auto")
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
                            .CenterVertical(),

                        new TextBlock()
                            .Text("Disabled")
                            .CenterVertical(),

                        new NumericUpDown()
                            .Disable()
                            .Width(140)
                            .Minimum(0)
                            .Maximum(100)
                            .Step(0.1)
                            .Format("0.##")
                            .BindValue(doubleBinding)
                            .CenterVertical()
                    )
            ),

            Card(
                "Slider",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Slider().Minimum(0).Maximum(100).Value(25),
                        new Slider().Minimum(0).Maximum(100).Value(25).Disable()
                    )
            )
        );
}
