using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept;

internal static class HScrollGridTest
{
    internal static Window Create()
    {
        var window = new Window()
            .Resizable(520, 200)
            .Title("HScroll Grid Test");

        var items = Enumerable.Range(0, 20)
            .Select(i => (Element)new Border
            {
                Width = 130,
                CornerRadius = 4,
                Padding = new Thickness(6),
                Background = Color.FromArgb(40, 220, 120, 20),
                BorderThickness = 1,
                BorderBrush = Color.FromRgb(180, 180, 180),
                Child = new TextBlock { Text = $"Tile {i:00}" },
            })
            .ToArray();

        window.Content = new ScrollViewer()
            .HorizontalScroll(ScrollMode.Auto)
            .VerticalScroll(ScrollMode.Disabled)
            .Content(
                new Grid().ShowGridLine()
                    .Rows("*")
                    .Columns("*")
                    .Children(
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(items)
                    )
            );

        return window;
    }
}
