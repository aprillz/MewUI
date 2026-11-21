using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private const double STAGE_WIDTH = 320;

    private FrameworkElement BackdropPage()
    {
        // Stripes behind the glass show how far the blur reaches.
        FrameworkElement Stripes()
        {
            var stripes = new StackPanel().Horizontal();
            Color[] colors =
            [
                Color.FromRgb(230, 80, 60), Color.FromRgb(240, 180, 40), Color.FromRgb(70, 170, 90),
                Color.FromRgb(50, 130, 220), Color.FromRgb(140, 80, 200),
            ];
            for (int index = 0; index < 10; index++)
            {
                stripes.Children(new Border().Width(32).Background(colors[index % colors.Length]));
            }

            return stripes;
        }

        Border Glass(double radius) => new Border()
            .CornerRadius(12)
            .Padding(12)
            .Background(Color.FromArgb(70, 255, 255, 255))
            .BackdropFilter(new BlurFilter(radius, radius))
            .Child(new TextBlock().Text("Frosted glass").Bold().Center());

        // --- Blur radius and position ---
        var movableGlass = Glass(12).Width(150).Height(90).Left().Top();
        var sliderRadius = new Slider().Range(0, 40).Value(12).Width(160);
        var sliderX = new Slider().Range(0, 170).Value(80).Width(160);
        sliderRadius.OnValueChanged(radius => movableGlass.BackdropFilter = radius > 0 ? new BlurFilter(radius, radius) : null);
        void Place() => movableGlass.Margin = new Thickness(sliderX.Value, 35, 0, 0);
        sliderX.OnValueChanged(_ => Place());
        Place();

        // --- Over changing content ---
        // Tall enough that the bar, the ring and the text box all pass under it.
        var changingGlass = Glass(10).Width(180).Height(130).Center();

        // --- Inside a faded group ---
        var fadedHost = new Grid().Children(Glass(10).Width(180).Height(70).Center());
        var sliderOpacity = new Slider().Range(0, 100).Value(70).Width(160);
        sliderOpacity.OnValueChanged(percent => fadedHost.Opacity = percent / 100.0);
        fadedHost.Opacity = 0.7;

        // --- Fallback brush ---
        var fallbackGlass = new Border()
            .Width(180)
            .Height(70)
            .CornerRadius(12)
            .Center()
            .Background(Color.FromArgb(40, 255, 255, 255))
            .BackdropFallbackBrush(new Rendering.SolidColorBrush(Color.FromArgb(200, 120, 120, 140)))
            .BackdropFilter(new BlurFilter(10, 10))
            .Child(new TextBlock().Text("Fallback when unreadable").Center());

        // As wide as the stripes, so a centered glass sits over them however wide the card grows.
        FrameworkElement Stage(params Element[] layers) => new Grid()
            .Width(STAGE_WIDTH)
            .Height(160)
            .ClipToBounds(true)
            .Left()
            .Children(layers);

        return CardGrid(
            Card(
                "Blur radius and position",
                new StackPanel().Vertical().Spacing(8).Children(
                    Stage(Stripes(), movableGlass),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Radius:").Width(60).CenterVertical(),
                        sliderRadius),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("X:").Width(60).CenterVertical(),
                        sliderX))),

            Card(
                "Over changing content",
                Stage(
                    new StackPanel().Vertical().Spacing(12).Center().Children(
                        new ProgressBar().IsIndeterminate(true).Width(260),
                        new ProgressRing().Width(40).Height(40),
                        new TextBox().Text("Type behind the glass").Width(220)),
                    changingGlass)),

            Card(
                "Inside a faded group",
                new StackPanel().Vertical().Spacing(8).Children(
                    Stage(Stripes(), fadedHost),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new TextBlock().Text("Opacity:").Width(60).CenterVertical(),
                        sliderOpacity))),

            Card(
                "Fallback brush",
                new StackPanel().Vertical().Spacing(8).Children(
                    Stage(Stripes(), fallbackGlass),
                    new TextBlock()
                        .Text("Where the backend cannot read what is behind the element, the fallback brush fills the backdrop instead.")
                        .Width(STAGE_WIDTH)
                        .Left()
                        .TextWrapping(TextWrapping.Wrap))),

            SystemBackdropMenuCard(),
            SystemBackdropWindowCard()
        );
    }

    private FrameworkElement SystemBackdropWindowCard()
    {
        Window? sample = null;

        void OpenSample()
        {
            if (sample != null)
            {
                sample.Activate();
                return;
            }

            Window sampleWindow = null!;

            Button MaterialButton(SystemBackdrop material)
                => new Button()
                    .Content(material.ToString())
                    .OnClick(() => sampleWindow.SystemBackdrop = material);

            var header = new TextBlock()
                .Text("Drag here to move over other windows")
                .FontWeight(FontWeight.SemiBold);

            new Window()
                .Ref(out sampleWindow)
                .Title("Backdrop material sample")
                .AllowsTransparency()
                .Fixed(420, 260)
                .WithTheme((t, w) => w
                    .Background(t.Palette.WindowBackground.WithAlpha(96))
                    .BackdropFallbackBrush(t.Palette.WindowBackground))
                .SystemBackdrop(SystemBackdrop.Acrylic)
                .Padding(16)
                .Content(
                    new StackPanel()
                        .Vertical()
                        .Spacing(10)
                        .Children(
                            header,
                            new WrapPanel()
                                .Spacing(6)
                                .Children(
                                    MaterialButton(SystemBackdrop.None),
                                    MaterialButton(SystemBackdrop.Blur),
                                    MaterialButton(SystemBackdrop.Acrylic),
                                    MaterialButton(SystemBackdrop.Mica)),
                            new TextBlock()
                                .FontSize(ThemeFontSize.Small)
                                .TextWrapping(TextWrapping.Wrap)
                                .Text("A platform that cannot show a material lowers it to the nearest one it can; with none the fallback brush fills the window."),
                            new Button()
                                .Content("Close")
                                .OnClick(() => sampleWindow.Close())));

            EnableWindowDrag(sampleWindow, header);
            sampleWindow.Closed += () => sample = null;
            sample = sampleWindow;
            sampleWindow.Show(window);
        }

        return Card(
            "System backdrop: window",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(ThemeFontSize.Small)
                        .Text("A transparent window filled by the platform's\nblur, acrylic or Mica material."),
                    new Button()
                        .Content("Open backdrop window")
                        .OnClick(OpenSample)));
    }

    private FrameworkElement SystemBackdropMenuCard()
    {
        var menu = new ContextMenu()
            .SystemBackdrop(SystemBackdrop.Acrylic)
            .Item("Copy")
            .Item("Paste")
            .Separator()
            .SubMenu("Transform", new ContextMenu()
                .Item("Uppercase")
                .Item("Lowercase"));

        Button MaterialButton(SystemBackdrop material)
            => new Button()
                .Content(material.ToString())
                .OnClick(() => menu.SystemBackdrop = material);

        // The bar keeps moving under an open menu, so the material is seen following what is behind it.
        var stage = new Border()
            .Width(STAGE_WIDTH)
            .Height(160)
            .ClipToBounds(true)
            .Left()
            .ContextMenu(menu)
            .Child(new Grid().Children(
                new VividStage(),
                new ProgressBar().IsIndeterminate(true).Width(260).Center(),
                new TextBlock().Text("Right-click here").Bold().Foreground(Color.White).CenterHorizontal().Top().Margin(0, 12, 0, 0)));

        return Card(
            "System backdrop: context menu",
            new StackPanel().Vertical().Spacing(8).Children(
                stage,
                new StackPanel().Horizontal().Spacing(6).Children(
                    MaterialButton(SystemBackdrop.None),
                    MaterialButton(SystemBackdrop.Blur),
                    MaterialButton(SystemBackdrop.Acrylic),
                    MaterialButton(SystemBackdrop.Mica))));
    }

    /// <summary>Strong colors with hard edges, which show how much of what is behind a material comes through it.</summary>
    private sealed class VividStage : FrameworkElement
    {
        protected override void OnRender(IGraphicsContext context)
        {
            var box = Bounds;
            void Blob(double x, double y, double size, Color color)
                => context.FillEllipse(new Rect(box.X + x, box.Y + y, size, size), color);

            context.FillRectangle(box, Color.FromRgb(18, 22, 70));
            Blob(-40, -50, 170, Color.FromRgb(255, 40, 130));
            Blob(95, -60, 150, Color.FromRgb(255, 190, 20));
            Blob(205, 10, 160, Color.FromRgb(0, 210, 230));
            Blob(30, 75, 140, Color.FromRgb(120, 70, 255));
            Blob(150, 90, 110, Color.FromRgb(40, 230, 110));
            context.FillRectangle(new Rect(box.X + 120, box.Y + 20, 16, 120), Color.White);
            context.FillRectangle(new Rect(box.X + 190, box.Y + 10, 10, 140), Color.Black);
        }
    }
}
