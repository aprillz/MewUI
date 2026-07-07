using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement ShapesPage() =>
        CardGrid(
            Card(
                "Rectangle",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Rectangle()
                            .Width(120).Height(60)
                            .Fill(Color.FromRgb(70, 130, 230))
                            .Stroke(Color.FromRgb(40, 80, 180), 2),
                        new Rectangle()
                            .Width(120).Height(60)
                            .CornerRadius(12)
                            .Fill(Color.FromRgb(100, 200, 120))
                            .Stroke(Color.FromRgb(60, 140, 80), 2)
                    )
            ),

            Card(
                "Ellipse",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Ellipse()
                            .Width(100).Height(100)
                            .Fill(Color.FromRgb(230, 100, 80))
                            .Stroke(Color.FromRgb(180, 60, 50), 2),
                        new Ellipse()
                            .Width(120).Height(60)
                            .Fill(Color.FromRgb(200, 160, 60))
                    )
            ),

            Card(
                "Line",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Line()
                            .Points(0, 0, 120, 40)
                            .Stroke(Color.FromRgb(70, 130, 230), 2),
                        new Line()
                            .Points(0, 0, 120, 0)
                            .Stroke(Color.FromRgb(230, 100, 80), 3)
                            .StrokeStyle(new StrokeStyle { DashArray = [6, 4] })
                    )
            ),

            Card(
                "Path (SVG)",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        // Heart
                        new PathShape()
                            .Data("M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z")
                            .Fill(Color.FromRgb(220, 60, 80))
                            .Stretch(Stretch.Uniform)
                            .Width(64).Height(64),
                        // Star
                        new PathShape()
                            .Data("M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z")
                            .Fill(Color.FromRgb(240, 190, 40))
                            .Stroke(Color.FromRgb(200, 150, 20), 1)
                            .Stretch(Stretch.Uniform)
                            .Width(64).Height(64)
                    )
            ),

            Card(
                "Path (Geometry)",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        // Triangle
                        new PathShape()
                            .Data(BuildTriangle())
                            .Fill(Color.FromRgb(120, 80, 200))
                            .Width(80).Height(70),
                        // Arrow
                        new PathShape()
                            .Data("M4 12h12m0 0l-5-5m5 5l-5 5")
                            .Stroke(Color.FromRgb(70, 130, 230), 2.5)
                            .Stretch(Stretch.Uniform)
                            .Width(64).Height(64)
                    )
            ),

            Card(
                "Stroke Styles",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Line()
                            .Points(0, 0, 160, 0)
                            .Stroke(Color.FromRgb(100, 100, 100), 3),
                        new Line()
                            .Points(0, 0, 160, 0)
                            .Stroke(Color.FromRgb(100, 100, 100), 3)
                            .StrokeStyle(new StrokeStyle { DashArray = [8, 4] }),
                        new Line()
                            .Points(0, 0, 160, 0)
                            .Stroke(Color.FromRgb(100, 100, 100), 3)
                            .StrokeStyle(new StrokeStyle { DashArray = [2, 4] }),
                        new Line()
                            .Points(0, 0, 160, 0)
                            .Stroke(Color.FromRgb(100, 100, 100), 3)
                            .StrokeStyle(new StrokeStyle { DashArray = [8, 4, 2, 4] })
                    )
            ),

            Card(
                "Prompt Icons",
                PromptIconsCard(),
                minWidth: 720
            )
        );

    private FrameworkElement PromptIconsCard()
        => new WrapPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(12)
            .Children(
                PromptIconTile("Question", new PromptIcon { Kind = PromptIconKind.Question }),
                PromptIconTile("Info", new PromptIcon { Kind = PromptIconKind.Info }),
                PromptIconTile("Warning", new PromptIcon { Kind = PromptIconKind.Warning }),
                PromptIconTile("Error", new PromptIcon { Kind = PromptIconKind.Error }),
                PromptIconTile("Success", new PromptIcon { Kind = PromptIconKind.Success }),
                PromptIconTile("Shield", new PromptIcon { Kind = PromptIconKind.Shield }),
                PromptIconTile("Crash", new PromptIcon { Kind = PromptIconKind.Crash })
            );

    private FrameworkElement PromptIconTile(string title, FrameworkElement icon)
        => new StackPanel()
            .Width(90)
            .Vertical()
            .Spacing(6)
            .Children(
                icon.Width(60).Height(60).Center(),

                new TextBlock().Text(title).Center()
            );

    private static PathGeometry BuildTriangle()
    {
        var g = new PathGeometry();
        g.MoveTo(40, 0);
        g.LineTo(80, 70);
        g.LineTo(0, 70);
        g.Close();
        g.Freeze();
        return g;
    }
}





