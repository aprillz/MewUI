using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement PanelsPage()
    {
        Button canvasButton = null!;
        var canvasInfo = new ObservableValue<string>("Pos: 20,20");
        double left = 20;
        double top = 20;

        void MoveCanvasButton()
        {
            left = (left + 24) % 140;
            top = (top + 16) % 70;
            Canvas.SetLeft(canvasButton, left);
            Canvas.SetTop(canvasButton, top);
            canvasInfo.Value = $"Pos: {left:0},{top:0}";
        }

        FrameworkElement PanelCard(string title, FrameworkElement content) =>
            Card(title, new Border()
                    .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                    .BorderThickness(1)
                    .CornerRadius(10)
                    .Width(280)
                    .Padding(8)
                    .Child(content));

        return CardGrid(
            PanelCard(
                "StackPanel",
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new Button().Content("A"),
                        new Button().Content("B"),
                        new Button().Content("C")
                    )
            ),

            PanelCard(
                "DockPanel",
                new DockPanel()
                    .Spacing(6)
                    .Children(
                        new Button().Content("Left").DockLeft(),
                        new Button().Content("Top").DockTop(),
                        new Button().Content("Bottom").DockBottom(),
                        new Button().Content("Fill")
                    )
            ),

            PanelCard(
                "WrapPanel",
                new WrapPanel()
                    .Orientation(Orientation.Horizontal)
                    .Spacing(6)
                    .ItemWidth(60)
                    .ItemHeight(28)
                    .Children(Enumerable.Range(1, 8).Select(i => new Button().Content($"#{i}")).ToArray())
            ),

            PanelCard(
                "UniformGrid",
                new UniformGrid()
                    .Columns(3)
                    .Rows(2)
                    .Spacing(6)
                    .Children(
                        new Button().Content("1"),
                        new Button().Content("2"),
                        new Button().Content("3"),
                        new Button().Content("4"),
                        new Button().Content("5"),
                        new Button().Content("6")
                    )
            ),

            PanelCard(
                "Grid (Span)",
                new Grid()
                    .Columns("Auto,*,*")
                    .Rows("Auto,Auto,Auto")
                    .AutoIndexing()
                    .Spacing(6)
                    .Children(
                        new Button().Content("ColSpan 2")
                            .ColumnSpan(2),

                        new Button().Content("R1C1"),

                        new Button().Content("RowSpan 2")
                            .RowSpan(2),

                        new Button().Content("R1C2"),

                        new Button().Content("R1C2"),

                        new Button().Content("R2C1"),

                        new Button().Content("R2C2")
                    )
            ),

            Card(
                "Canvas",
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new Border()
                            .Height(140)
                            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                            .BorderThickness(1)
                            .CornerRadius(10)
                            .Child(
                                new Canvas()
                                    .Children(
                                        new Button()
                                            .Ref(out canvasButton)
                                            .Content("Move")
                                            .OnClick(MoveCanvasButton)
                                            .CanvasPosition(left, top)
                                    )
                            ),

                        new Label()
                            .BindText(canvasInfo)
                            .FontSize(11)
                    ),
                minWidth: 320
            ),

            PanelCard(
                "SplitPanel",
                new SplitPanel()
                    .Horizontal()
                    .SplitterThickness(8)
                    .Height(140)
                    .MinFirst(60)
                    .MinSecond(60)
                    .FirstLength(GridLength.Stars(1))
                    .SecondLength(GridLength.Stars(1))
                    .First(
                        new Border()
                            .WithTheme((t, b) => b.Background(t.Palette.ButtonFace))
                            .BorderThickness(1)
                            .CornerRadius(8)
                            .Padding(8)
                            .Child(new Label().Text("First"))
                    )
                    .Second(
                        new Border()
                            .WithTheme((t, b) => b.Background(t.Palette.ButtonFace))
                            .BorderThickness(1)
                            .CornerRadius(8)
                            .Padding(8)
                            .Child(new Label().Text("Second"))
                    )
            ),

            PanelCard(
                "SplitPanel (Vertical)",
                new SplitPanel()
                    .Vertical()
                    .SplitterThickness(8)
                    .Height(140)
                    .MinFirst(40)
                    .MinSecond(40)
                    .FirstLength(GridLength.Stars(1))
                    .SecondLength(GridLength.Stars(1))
                    .First(
                        new Border()
                            .WithTheme((t, b) => b.Background(t.Palette.ButtonFace))
                            .CornerRadius(8)
                            .Padding(8)
                            .Child(new Label().Text("Top"))
                    )
                    .Second(
                        new Border()
                            .WithTheme((t, b) => b.Background(t.Palette.ButtonFace))
                            .BorderThickness(1)
                            .CornerRadius(8)
                            .Padding(8)
                            .Child(new Label().Text("Bottom"))
                    )
            )
        );
    }
}
