using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement TransformPage()
    {
        // Shared image factory
        Image CatImage() => new Image()
            .BindSource(Resources.April).Width(120).Height(120)
            .StretchMode(Stretch.Uniform)
            .ImageScaleQuality(ImageScaleQuality.HighQuality);

        // --- Translate ---
        var translateBox = new TransformBox().Center().Child(CatImage());
        var sliderTX = new Slider().Range(-100, 100).Value(0).Width(150);
        var sliderTY = new Slider().Range(-100, 100).Value(0).Width(150);
        translateBox.Bind(TransformBox.TranslateXProperty, sliderTX, RangeBase.ValueProperty);
        translateBox.Bind(TransformBox.TranslateYProperty, sliderTY, RangeBase.ValueProperty);

        // --- Rotate ---
        var rotateBox = new TransformBox().Center().Child(CatImage());
        var sliderRotate = new Slider().Range(-180, 180).Value(0).Width(180);
        rotateBox.Bind(TransformBox.RotationDegreesProperty, sliderRotate, RangeBase.ValueProperty);

        // --- Scale ---
        var scaleBox = new TransformBox().Center().Child(CatImage());
        var sliderScale = new Slider().Range(10, 300).Value(100).Width(180);
        scaleBox.Bind(TransformBox.ScaleXProperty, sliderScale, RangeBase.ValueProperty,
            v => v / 100.0, v => v * 100.0);
        scaleBox.Bind(TransformBox.ScaleYProperty, sliderScale, RangeBase.ValueProperty,
            v => v / 100.0, v => v * 100.0);

        // --- Combined ---
        var combinedBox = new TransformBox().Center().Child(
            new Image().BindSource(Resources.Logo).Width(200).Height(80)
                .StretchMode(Stretch.Uniform)
                .ImageScaleQuality(ImageScaleQuality.HighQuality));
        var sliderCombRot = new Slider().Range(-180, 180).Value(0).Width(140);
        var sliderCombScale = new Slider().Range(10, 300).Value(100).Width(140);
        combinedBox.Bind(TransformBox.RotationDegreesProperty, sliderCombRot, RangeBase.ValueProperty);
        combinedBox.Bind(TransformBox.ScaleXProperty, sliderCombScale, RangeBase.ValueProperty,
            v => v / 100.0, v => v * 100.0);
        combinedBox.Bind(TransformBox.ScaleYProperty, sliderCombScale, RangeBase.ValueProperty,
            v => v / 100.0, v => v * 100.0);

        // --- Transform Origin ---
        var originBox = new TransformBox().Center().Child(CatImage());
        var sliderOX = new Slider().Range(0, 100).Value(50).Width(120);
        var sliderOY = new Slider().Range(0, 100).Value(50).Width(120);
        var sliderOAngle = new Slider().Range(-180, 180).Value(30).Width(120);
        originBox.Bind(TransformBox.OriginXProperty, sliderOX, RangeBase.ValueProperty,
            v => v / 100.0, v => v * 100.0);
        originBox.Bind(TransformBox.OriginYProperty, sliderOY, RangeBase.ValueProperty,
            v => v / 100.0, v => v * 100.0);
        originBox.Bind(TransformBox.RotationDegreesProperty, sliderOAngle, RangeBase.ValueProperty);

        // Label helper: bind TextBlock.Text to a double MewProperty with format
        TextBlock Label(MewObject source, MewProperty<double> prop, string format, double width = 50) =>
            new TextBlock()
                .Bind(TextBlock.TextProperty, source, prop, v => v.ToString(format))
                .Width(width)
                .CenterVertical();

        return CardGrid(
            Card(
                "Translate",
                new StackPanel().Vertical().Spacing(8).Children(
                    new Border().Height(160).ClipToBounds(true)
                        .StretchHorizontal()
                        .Child(translateBox),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("X:").StretchHorizontal(),
                        sliderTX,
                        Label(translateBox, TransformBox.TranslateXProperty, "F0", 40)),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Y:").StretchHorizontal(),
                        sliderTY,
                        Label(translateBox, TransformBox.TranslateYProperty, "F0", 40))
                )
            ),

            Card(
                "Rotate",
                new StackPanel().Vertical().Spacing(8).Children(
                    new Border().Height(160).ClipToBounds(true)
                        .StretchHorizontal()
                        .Child(rotateBox),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Angle:").StretchHorizontal(),
                        sliderRotate,
                        Label(rotateBox, TransformBox.RotationDegreesProperty, "0\u00b0"))
                )
            ),

            Card(
                "Scale",
                new StackPanel().Vertical().Spacing(8).Children(
                    new Border().Height(160).ClipToBounds(true)
                        .StretchHorizontal()
                        .Child(scaleBox),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Scale:").StretchHorizontal(),
                        sliderScale,
                        Label(scaleBox, TransformBox.ScaleXProperty, "0.00\u0078"))
                )
            ),

            Card(
                "Rotate + Scale",
                new StackPanel().Vertical().Spacing(8).Children(
                    new Border().Height(160).ClipToBounds(true)
                        .StretchHorizontal()
                        .Child(combinedBox),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Angle:").StretchHorizontal(),
                        sliderCombRot,
                        Label(combinedBox, TransformBox.RotationDegreesProperty, "0\u00b0")),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Scale:").StretchHorizontal(),
                        sliderCombScale,
                        Label(combinedBox, TransformBox.ScaleXProperty, "0.00\u0078"))
                )
            ),

            Card(
                "Transform Origin",
                new StackPanel().Vertical().Spacing(8).Children(
                    new Border().Height(160).ClipToBounds(true)
                        .StretchHorizontal()
                        .Child(originBox),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Origin X:").StretchHorizontal(),
                        sliderOX,
                        Label(originBox, TransformBox.OriginXProperty, "F2", 40)),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Origin Y:").StretchHorizontal(),
                        sliderOY,
                        Label(originBox, TransformBox.OriginYProperty, "F2", 40)),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Angle:").StretchHorizontal(),
                        sliderOAngle,
                        Label(originBox, TransformBox.RotationDegreesProperty, "0\u00b0"))
                )
            ),

            Card(
                "Zoom & Pan Canvas",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("Open Zoom & Pan Window")
                            .OnClick(OpenZoomPanWindow),
                        new TextBlock()
                            .FontSize(ThemeFontSize.Small)
                            .TextWrapping(TextWrapping.Wrap)
                            .Text("Wheel to zoom (anchored to cursor),\ndrag to pan. Animated zoom with\nReset/Fit controls.")
                    )
            )
        );

        // --- Zoom & Pan Canvas ---
        void OpenZoomPanWindow()
        {
            var image = new Image()
                .StretchMode(Stretch.None)
                .ImageScaleQuality(ImageScaleQuality.HighQuality)
                .BindSource(Resources.Soonduk);

            var canvas = new ZoomPanCanvas { Child = image, CenterContent = true };

            var scrollViewer = new ScrollViewer()
                .Padding(0)
                .HorizontalScroll(ScrollMode.Auto)
                .VerticalScroll(ScrollMode.Auto);
            scrollViewer.Content = canvas;

            double logRatio = Math.Log(ZoomPanCanvas.MaxZoom / ZoomPanCanvas.MinZoom);
            var slider = new Slider().Width(150)
                .CenterVertical()
                .Range(0, 1)
                .SmallChange(0.01)
                .Value(Math.Log(1.0 / ZoomPanCanvas.MinZoom) / logRatio)
                .Bind(Slider.ValueProperty, canvas, ZoomPanCanvas.ZoomProperty,
                    zoom => Math.Log(zoom / ZoomPanCanvas.MinZoom) / logRatio,
                    t => ZoomPanCanvas.MinZoom * Math.Exp(t * logRatio));

            var zoomLabel = new TextBlock()
                .Text("100%")
                .CenterVertical()
                .Width(70);

            canvas.ZoomChanged += zoom => zoomLabel.Text = $"{zoom * 100:0}%";

            var resetButton = new Button()
                .Content("Reset")
                .Width(70)
                .OnClick(() => canvas.AnimateZoomWithViewCenter(scrollViewer, 1.0));

            var fitButton = new Button()
                .Content("Fit")
                .Width(70)
                .OnClick(() =>
                {
                    var viewportW = scrollViewer.ViewportWidth;
                    var viewportH = scrollViewer.ViewportHeight;
                    if (viewportW <= 0 || viewportH <= 0) return;

                    var childSize = canvas.ChildNaturalSize;
                    if (childSize.Width <= 0 || childSize.Height <= 0) return;

                    var fitZoom = Math.Min(viewportW / childSize.Width, viewportH / childSize.Height);
                    canvas.AnimateZoomWithViewCenter(scrollViewer, Math.Clamp(fitZoom, ZoomPanCanvas.MinZoom, ZoomPanCanvas.MaxZoom));
                });

            var toolbar = new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Margin(new Thickness(8))
                .Children(
                    new TextBlock().Text("Zoom:").CenterVertical(),
                    slider,
                    zoomLabel,
                    resetButton,
                    fitButton,
                    new CheckBox()
                        .Content("Center")
                        .Check()
                        .CenterVertical()
                        .OnCheckedChanged(isChecked => canvas.CenterContent = isChecked),
                    new TextBlock()
                        .Text("Wheel to zoom, Drag or Scrollbar to pan")
                        .Foreground(Color.FromRgb(120, 120, 120))
                        .CenterVertical()
                );

            new Window()
                .Resizable(900, 650)
                .Title("Zoom & Pan Canvas")
                .Content(new DockPanel()
                    .Children(
                        toolbar.DockTop(),
                        scrollViewer
                    ))
                .Show(window);
        }
    }
}
