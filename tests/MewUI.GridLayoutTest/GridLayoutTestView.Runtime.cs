namespace Aprillz.MewUI.GridLayoutTest;

internal sealed partial class GridLayoutTestView
{
    private FrameworkElement RuntimeCases()
    {
        var showSecond = new ObservableValue<bool>(true);
        var dynamicHost = BuildRuntimeMutationCase(showSecond);

        return new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                CaseCard(
                    "Visibility Toggle",
                    "Checks auto/desired recalculation when a child becomes hidden or visible.",
                    SampleHost(
                        new StackPanel()
                            .Vertical()
                            .Spacing(8)
                            .Children(
                                new CheckBox()
                                    .Content("Show second item")
                                    .IsChecked(true)
                                    .OnCheckedChanged(v => showSecond.Value = v),
                                dynamicHost
                            ),
                        220
                    ))
            );
    }

    private static FrameworkElement BuildRuntimeMutationCase(ObservableValue<bool> showSecond) =>
        new Grid().ShowGridLine()
            .Columns("Auto,*")
            .Rows("Auto,Auto,*")
            .Spacing(8)
            .Children(
                Cell("Title").GridPosition(0, 0),
                Cell("Editor").GridPosition(0, 1),
                Cell("Always visible").GridPosition(1, 0, 1, 2),
                Cell("Toggled item").GridPosition(2, 0, 1, 2).BindIsVisible(showSecond)
            );
}
