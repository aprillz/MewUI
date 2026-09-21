using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement ProgressPage()
    {
        var ring = new ProgressRing { IsActive = false };

        return CardGrid(
            Card(
                "ProgressBar",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new ProgressBar().Value(20),
                        new ProgressBar().Value(65),
                        new ProgressBar().Value(65).Disable(),
                        new ProgressBar().IsIndeterminate()
                    )
            ),

            Card(
                "ProgressRing",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Height(60)
                            .HorizontalAlignment(HorizontalAlignment.Center)
                            .Child(
                                ring
                                    .Width(48)
                                    .Height(48)
                                    .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
                            ),
                        new Button()
                            .Content("Toggle")
                            .OnClick(() => ring.IsActive = !ring.IsActive)
                    )
            )
        );
    }
}
