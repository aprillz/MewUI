using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private ImageSource april = ImageSource.FromFile("april.jpg");
    private ImageSource logo = ImageSource.FromFile("logo_h-1280.png");

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
                            .Source(april)
                            .Width(120)
                            .Height(120)
                            .StretchMode(Stretch.Uniform)
                            .Center(),
                        new Label()
                            .Text("april.jpg")
                            .FontSize(11)
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
                            .Source(logo)
                            .ImageScaleQuality(ImageScaleQuality.HighQuality)
                            .Width(200)
                            .Height(120)
                            .StretchMode(Stretch.Uniform)
                            .Center(),
                        new Label()
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
                                    .Source(april)
                                    .StretchMode(Stretch.Uniform)
                                    .ImageScaleQuality(ImageScaleQuality.HighQuality),

                                new Image()
                                    .Source(april)
                                    .ViewBoxRelative(new Rect(0.25, 0.25, 0.5, 0.5))
                                    .StretchMode(Stretch.UniformToFill)
                                    .ImageScaleQuality(ImageScaleQuality.HighQuality)
                            ),

                        new Label()
                            .Text("Left: full image (Uniform). Right: ViewBox (center 50%) + UniformToFill.")
                            .FontSize(11)
                    )
            )
        );
}
