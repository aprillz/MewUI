using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Geometry;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

internal sealed partial class GeometryOperationsWindow : Window
{
    private static readonly Color _sourceColor = Color.FromRgb(55, 125, 225);
    private static readonly Color _otherColor = Color.FromRgb(235, 115, 65);
    private static readonly Color _resultColor = Color.FromRgb(55, 175, 110);
    private static readonly Color _queryColor = Color.FromRgb(165, 80, 205);

    protected override void OnBuild()
    {
        base.OnBuild();

        this.Resizable(1320, 900)
            .StartCenterScreen()
            .Title("MewUI Geometry Operations")
            .Content(
                new ScrollViewer()
                    .NoHorizontalScroll()
                    .AutoVerticalScroll()
                    .Padding(24)
                    .Content(
                        new StackPanel()
                            .Vertical()
                            .Spacing(16)
                            .Children(
                                new TextBlock().Text("MewUI.Geometry operations").FontSize(ThemeFontSize.Large).Bold(),
                                new TextBlock()
                                    .Text("Each card shows one operation. Click or drag the purple query point; drag orange operands in relationship examples and both operands in Boolean examples.")
                                    .TextWrapping(TextWrapping.Wrap),
                                new WrapPanel()
                                    .Orientation(Orientation.Horizontal)
                                    .Spacing(18)
                                    .Children(
                                        CreateBoundsCard(false),
                                        CreateBoundsCard(true),
                                        CreatePathCard(PathOperation.Flatten),
                                        CreatePointCard(false),
                                        CreateGeometryQueryCard(QueryOperation.FillContains),
                                        CreateGeometryQueryCard(QueryOperation.FillContainsWithDetail),
                                        CreateGeometryQueryCard(QueryOperation.Intersects),
                                        CreateGeometryQueryCard(QueryOperation.Encloses),
                                        CreateAreaCard(),
                                        CreatePointCard(true),
                                        CreateGeometryQueryCard(QueryOperation.StrokeContainsWithDetail),
                                        CreatePathCard(PathOperation.Widen),
                                        CreatePathCard(PathOperation.Outline),
                                        CreateCombineCard(GeometryCombineMode.Union),
                                        CreateCombineCard(GeometryCombineMode.Intersect),
                                        CreateCombineCard(GeometryCombineMode.Xor),
                                        CreateCombineCard(GeometryCombineMode.Exclude),
                                        CreateShapeHitTestCard()))));
    }

    private static FrameworkElement Card(string title, DraggablePreview preview, TextBlock result, params FrameworkElement[] controls)
    {
        var content = new StackPanel().Vertical().Spacing(8);
        content.Add(new TextBlock().Text(title).SemiBold().TextWrapping(TextWrapping.Wrap));
        content.Add(new Border()
            .Width(DraggablePreview.PREVIEW_WIDTH + 2)
            .Height(DraggablePreview.PREVIEW_HEIGHT + 2)
            .BorderThickness(1)
            .WithTheme((theme, border) => border
                .Background(theme.Palette.WindowBackground)
                .BorderBrush(theme.Palette.ControlBorder))
            .Child(preview));

        foreach (FrameworkElement control in controls)
        {
            content.Add(control);
        }

        content.Add(result);
        return new Border()
            .Width(270)
            .Padding(12)
            .BorderThickness(1)
            .WithTheme((theme, border) => border
                .Background(theme.Palette.ContainerBackground)
                .BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }

    private static TextBlock ResultText() => new TextBlock()
        .FontFamily("Consolas")
        .FontSize(ThemeFontSize.Small)
        .TextWrapping(TextWrapping.Wrap);

    private static FrameworkElement LabeledControl(string title, FrameworkElement control) =>
        new StackPanel().Vertical().Spacing(2).Children(
            new TextBlock().Text(title).FontSize(ThemeFontSize.Small),
            control);

    private static PathGeometry Curve()
    {
        var path = new PathGeometry();
        path.MoveTo(25, 105);
        path.BezierTo(35, 5, 145, 5, 155, 105);
        path.Freeze();
        return path;
    }

    private static GeometryGroup Ring(double scale = 1)
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Add(new RectangleGeometry(new Rect(30, 22, 120 * scale, 85 * scale)));
        group.Add(new EllipseGeometry(new Rect(67, 43, 45 * scale, 43 * scale)));
        return group;
    }

    private static PathGeometry Marker(double x, double y) =>
        PathGeometry.FromEllipse(x, y, 5, 5);

    private static string RectText(Rect rect) =>
        $"({rect.X:F1}, {rect.Y:F1}, {rect.Width:F1}, {rect.Height:F1})";

    private enum PathOperation { Flatten, Widen, Outline }
    private enum QueryOperation { FillContains, FillContainsWithDetail, Intersects, Encloses, StrokeContainsWithDetail }
}
