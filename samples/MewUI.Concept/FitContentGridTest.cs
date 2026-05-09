using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept;

internal static class FitContentGridTest
{
    internal static Window Create()
    {
        var window = new Window()
            .Title("FitContentSize Grid Test");

        window.Content = new Grid().ShowGridLine()
            .Margin(12)
            .Columns("Auto,*")
            .Rows("Auto,Auto,*")
            .Spacing(8)
            .Children(
                new TextBlock { Text = "Name", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center },
                new TextBox().Column(1).Width(180),
                new TextBlock { Text = "Description", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }.Row(1),
                new MultiLineTextBox().GridPosition(1, 1).Width(240).Height(120),
                new Button().Content("Save").GridPosition(2, 0, 1, 2)
            );

        window.WindowSize = WindowSize.FitContentSize(900, 700);
        return window;
    }
}
