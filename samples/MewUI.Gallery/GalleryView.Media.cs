using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{

    private Image peekImage = null!;
    private ObservableValue<string> imagePeekText = new ObservableValue<string>("Color: -");

    private FrameworkElement MediaPage() =>
        CardGrid(
            Card(
                "Image",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Image()
                            .BindSource(Resources.April)
                            .Width(120)
                            .Height(120)
                            .StretchMode(Stretch.Uniform)
                            .Center(),
                        new TextBlock()
                            .Text("april.jpg")
                            .FontSize(ThemeFontSize.Small)
                            .Center()
                    )
            ),

            Card(
                "Peek Color",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Image()
                            .Ref(out peekImage)
                            .OnMouseMove(e => imagePeekText.Value = peekImage.TryPeekColor(e.GetPosition(peekImage), out var c)
                                ? $"Color: #{c.ToArgb():X8}"
                                : "Color: #--------")
                            .BindSource(Resources.Logo)
                            .ImageScaleQuality(ImageScaleQuality.HighQuality)
                            .Width(200)
                            .Height(120)
                            .StretchMode(Stretch.Uniform)
                            .Center(),
                        new TextBlock()
                            .BindText(imagePeekText)
                            .FontFamily("Consolas")
                            .Center()
                    )
            ),

            Card(
                "Image ViewBox",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new WrapPanel()
                            .Orientation(Orientation.Horizontal)
                            .Spacing(8)
                            .ItemWidth(140)
                            .ItemHeight(90)
                            .Children(
                                new Image()
                                    .BindSource(Resources.April)
                                    .StretchMode(Stretch.Uniform)
                                    .ImageScaleQuality(ImageScaleQuality.HighQuality),

                                new Image()
                                    .BindSource(Resources.April)
                                    .ViewBoxRelative(new Rect(0.25, 0.25, 0.5, 0.5))
                                    .StretchMode(Stretch.UniformToFill)
                                    .ImageScaleQuality(ImageScaleQuality.HighQuality)
                            ),

                        new TextBlock()
                            .Text("Left: full image (Uniform). Right: ViewBox (center 50%) + UniformToFill.")
                            .FontSize(ThemeFontSize.Small)
                    )
            )
        );
}
