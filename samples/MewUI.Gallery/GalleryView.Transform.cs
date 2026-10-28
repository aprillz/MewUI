using System.Numerics;

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

        static float Radians(double degrees) => (float)(degrees * Math.PI / 180.0);

        // --- Translate ---
        var translateBox = new TransformDecorator().Center().Child(CatImage());
        var sliderTX = new Slider().Range(-100, 100).Value(0).Width(150);
        var sliderTY = new Slider().Range(-100, 100).Value(0).Width(150);
        void UpdateTranslate() => translateBox.Transform =
            Matrix3x2.CreateTranslation((float)sliderTX.Value, (float)sliderTY.Value);
        sliderTX.OnValueChanged(_ => UpdateTranslate());
        sliderTY.OnValueChanged(_ => UpdateTranslate());

        // --- Rotate ---
        var rotateBox = new TransformDecorator().Center().Child(CatImage());
        var sliderRotate = new Slider().Range(-180, 180).Value(0).Width(180);
        sliderRotate.OnValueChanged(degrees => rotateBox.Transform = Matrix3x2.CreateRotation(Radians(degrees)));

        // --- Scale ---
        var scaleBox = new TransformDecorator().Center().Child(CatImage());
        var sliderScale = new Slider().Range(10, 300).Value(100).Width(180);
        sliderScale.OnValueChanged(percent => scaleBox.Transform = Matrix3x2.CreateScale((float)(percent / 100.0)));

        // --- Combined ---
        var combinedBox = new TransformDecorator().Center().Child(
            new Image().BindSource(Resources.Logo).Width(200).Height(80)
                .StretchMode(Stretch.Uniform)
                .ImageScaleQuality(ImageScaleQuality.HighQuality));
        var sliderCombRot = new Slider().Range(-180, 180).Value(0).Width(140);
        var sliderCombScale = new Slider().Range(10, 300).Value(100).Width(140);
        void UpdateCombined() => combinedBox.Transform =
            Matrix3x2.CreateScale((float)(sliderCombScale.Value / 100.0)) *
            Matrix3x2.CreateRotation(Radians(sliderCombRot.Value));
        sliderCombRot.OnValueChanged(_ => UpdateCombined());
        sliderCombScale.OnValueChanged(_ => UpdateCombined());

        // --- Transform Origin ---
        var originBox = new TransformDecorator().Center().Child(CatImage());
        var sliderOX = new Slider().Range(0, 100).Value(50).Width(120);
        var sliderOY = new Slider().Range(0, 100).Value(50).Width(120);
        var sliderOAngle = new Slider().Range(-180, 180).Value(30).Width(120);
        void UpdateOrigin()
        {
            originBox.TransformOrigin = new Point(sliderOX.Value / 100.0, sliderOY.Value / 100.0);
            originBox.Transform = Matrix3x2.CreateRotation(Radians(sliderOAngle.Value));
        }
        sliderOX.OnValueChanged(_ => UpdateOrigin());
        sliderOY.OnValueChanged(_ => UpdateOrigin());
        sliderOAngle.OnValueChanged(_ => UpdateOrigin());
        UpdateOrigin();

        // Render mode keeps the outlined layout box at 100 x 40 and draws over A and B;
        // Layout mode grows the box to the rotated bounding box and pushes A and B apart.
        TransformDecorator ModeSample(TransformMode mode) => new TransformDecorator()
            .Mode(mode)
            .Child(
                new Border()
                    .Width(100)
                    .Height(40)
                    .CornerRadius(4)
                    .WithTheme((theme, border) => border.Background(theme.Palette.Accent.WithAlpha(64)))
                    .Child(new TextBlock().Text($"{mode} mode").Center()));

        FrameworkElement ModeRow(TransformDecorator decorator) => new Border()
            .Height(120)
            .Child(
                new StackPanel()
                    .Horizontal()
                    .Spacing(4)
                    .CenterVertical()
                    .Children(
                        new TextBlock().Text("A").CenterVertical(),
                        new Border()
                            .BorderThickness(1)
                            .WithTheme((theme, border) => border.BorderBrush(theme.Palette.ControlBorder))
                            .CenterVertical()
                            .Child(decorator),
                        new TextBlock().Text("B").CenterVertical()));

        var renderModeBox = ModeSample(TransformMode.Render);
        var layoutModeBox = ModeSample(TransformMode.Layout);
        var sliderModeAngle = new Slider().Range(-180, 180).Value(30).Width(180);
        void UpdateMode()
        {
            var rotation = Matrix3x2.CreateRotation(Radians(sliderModeAngle.Value));
            renderModeBox.Transform = rotation;
            layoutModeBox.Transform = rotation;
        }
        sliderModeAngle.OnValueChanged(_ => UpdateMode());
        UpdateMode();

        // Every input below lands where the control is drawn: clicks, slider drags, the drop-down and
        // tool tip placement, and the pointer position reported in the panel's own coordinates.
        var hitClicks = new ObservableValue<int>(0);
        var hitPointer = new ObservableValue<string>("Pointer in panel: -");
        var hitSlider = new Slider().Range(0, 100).Value(40).Width(140);
        var hitPanel = new Border()
            .Padding(8)
            .CornerRadius(6)
            .BorderThickness(1)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new Button()
                            .Content("Click me")
                            .ToolTip("The tool tip opens at the drawn button")
                            .OnClick(() => hitClicks.Value++),
                        hitSlider,
                        new ComboBox()
                            .Width(140)
                            .Items("Red", "Green", "Blue")
                            .SelectedIndex(0)));
        hitPanel.OnMouseMove(args =>
        {
            var position = args.GetPosition(hitPanel);
            hitPointer.Value = $"Pointer in panel: {position.X:0}, {position.Y:0}";
        });

        var hitBox = new TransformDecorator().Center().Child(hitPanel);
        var sliderHitAngle = new Slider().Range(-180, 180).Value(-20).Width(140);
        var sliderHitScale = new Slider().Range(50, 160).Value(120).Width(140);
        void UpdateHit() => hitBox.Transform =
            Matrix3x2.CreateScale((float)(sliderHitScale.Value / 100.0)) *
            Matrix3x2.CreateRotation(Radians(sliderHitAngle.Value));
        sliderHitAngle.OnValueChanged(_ => UpdateHit());
        sliderHitScale.OnValueChanged(_ => UpdateHit());
        UpdateHit();

        // The child keeps its natural size; Stretch picks the scale and StretchDirection limits it to growing or shrinking.
        Stretch[] stretchOptions = [Stretch.None, Stretch.Fill, Stretch.Uniform, Stretch.UniformToFill];
        StretchDirection[] directionOptions = [StretchDirection.UpOnly, StretchDirection.DownOnly, StretchDirection.Both];
        var viewbox = new Viewbox().Child(
            new Border()
                .Padding(8)
                .CornerRadius(6)
                .WithTheme((theme, border) => border.Background(theme.Palette.Accent.WithAlpha(48)))
                .Child(
                    new StackPanel()
                        .Vertical()
                        .Spacing(4)
                        .Children(
                            new TextBlock().Text("Viewbox content").Bold(),
                            new Button().Content("Button"))));
        var viewboxHost = new Border()
            .Width(200)
            .Height(120)
            .BorderThickness(1)
            .ClipToBounds(true)
            .WithTheme((theme, border) => border.BorderBrush(theme.Palette.ControlBorder))
            .Center()
            .Child(viewbox);

        var stretchCombo = new ComboBox()
            .Width(140)
            .Items(stretchOptions, option => option.ToString())
            .SelectedIndex(Array.IndexOf(stretchOptions, viewbox.Stretch));
        stretchCombo.OnSelectionChanged(_ => viewbox.Stretch = stretchOptions[Math.Max(0, stretchCombo.SelectedIndex)]);

        var directionCombo = new ComboBox()
            .Width(140)
            .Items(directionOptions, option => option.ToString())
            .SelectedIndex(Array.IndexOf(directionOptions, viewbox.StretchDirection));
        directionCombo.OnSelectionChanged(_ => viewbox.StretchDirection = directionOptions[Math.Max(0, directionCombo.SelectedIndex)]);

        var sliderViewboxWidth = new Slider().Range(40, 280).Value(200).Width(140);
        var sliderViewboxHeight = new Slider().Range(30, 160).Value(120).Width(140);
        sliderViewboxWidth.OnValueChanged(width => viewboxHost.Width = width);
        sliderViewboxHeight.OnValueChanged(height => viewboxHost.Height = height);

        // Label helper: bind TextBlock.Text to a slider value with format
        TextBlock Label(Slider source, Func<double, string> format, double width = 50) =>
            new TextBlock()
                .Bind(TextBlock.TextProperty, source, RangeBase.ValueProperty, format)
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
                        Label(sliderTX, value => value.ToString("F0"), 40)),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Y:").StretchHorizontal(),
                        sliderTY,
                        Label(sliderTY, value => value.ToString("F0"), 40))
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
                        Label(sliderRotate, value => value.ToString("0\u00b0")))
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
                        Label(sliderScale, value => (value / 100.0).ToString("0.00\u0078")))
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
                        Label(sliderCombRot, value => value.ToString("0\u00b0"))),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Scale:").StretchHorizontal(),
                        sliderCombScale,
                        Label(sliderCombScale, value => (value / 100.0).ToString("0.00\u0078")))
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
                        Label(sliderOX, value => (value / 100.0).ToString("F2"), 40)),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Origin Y:").StretchHorizontal(),
                        sliderOY,
                        Label(sliderOY, value => (value / 100.0).ToString("F2"), 40)),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Angle:").StretchHorizontal(),
                        sliderOAngle,
                        Label(sliderOAngle, value => value.ToString("0\u00b0")))
                )
            ),

            Card(
                "Render vs Layout Mode",
                new StackPanel().Vertical().Spacing(8).Children(
                    ModeRow(renderModeBox),
                    ModeRow(layoutModeBox),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Angle:").StretchHorizontal(),
                        sliderModeAngle,
                        Label(sliderModeAngle, value => value.ToString("0\u00b0")))
                )
            ),

            Card(
                "Hit Testing",
                new StackPanel().Vertical().Spacing(8).Children(
                    new Border().Height(240).ClipToBounds(true)
                        .StretchHorizontal()
                        .Child(hitBox),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Angle:").StretchHorizontal(),
                        sliderHitAngle,
                        Label(sliderHitAngle, value => value.ToString("0\u00b0"))),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Scale:").StretchHorizontal(),
                        sliderHitScale,
                        Label(sliderHitScale, value => (value / 100.0).ToString("0.00\u0078"))),
                    new TextBlock()
                        .BindText(hitClicks, count => $"Clicks: {count}")
                        .FontSize(ThemeFontSize.Small),
                    new TextBlock()
                        .Bind(TextBlock.TextProperty, hitSlider, RangeBase.ValueProperty, (double value) => $"Slider value: {value:0}")
                        .FontSize(ThemeFontSize.Small),
                    new TextBlock()
                        .BindText(hitPointer)
                        .FontSize(ThemeFontSize.Small)
                )
            ),

            Card(
                "Viewbox",
                new StackPanel().Vertical().Spacing(8).Children(
                    new Border().Height(170)
                        .StretchHorizontal()
                        .Child(viewboxHost),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Stretch:").StretchHorizontal(),
                        stretchCombo),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Direction:").StretchHorizontal(),
                        directionCombo),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Width:").StretchHorizontal(),
                        sliderViewboxWidth,
                        Label(sliderViewboxWidth, value => value.ToString("F0"), 40)),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Height:").StretchHorizontal(),
                        sliderViewboxHeight,
                        Label(sliderViewboxHeight, value => value.ToString("F0"), 40))
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
