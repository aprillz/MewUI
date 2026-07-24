namespace Aprillz.MewUI.GridLayoutTest;

internal sealed partial class GridLayoutTestView : UserControl
{
    private static readonly Color SampleBorderColor = Color.FromArgb(96, 96, 96, 96);
    private static readonly Color AccentPanelColor = Color.FromArgb(18, 0, 120, 215);
    private static readonly Color AccentFillColor = Color.FromArgb(50, 0, 120, 215);

    private readonly Window _owner;

    public GridLayoutTestView(Window owner)
    {
        _owner = owner;
        Build();
    }

    protected override Element? OnBuild() =>
        new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Padding(16)
            .Content(
                new StackPanel()
                    .Vertical()
                    .Spacing(16)
                    .Children(
                        Header(),
                        Section("Sizing", SizingCases()),
                        Section("Span And Spacing", SpanCases()),
                        Section("Auto Indexing", AutoIndexingCases()),
                        Section("ScrollViewer", ScrollCases()),
                        Section("Fit Content Windows", WindowCases()),
                        Section("Runtime Mutation", RuntimeCases())
                    )
            );

    private Element Header() =>
        new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                new Label().Text("Grid Layout Test").FontSize(22).Bold()
            );

    private static FrameworkElement Section(string title, FrameworkElement content) =>
        new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new Label().Text(title).FontSize(16).Bold(),
                content
            );

    private FrameworkElement CaseCard(string title, string description, FrameworkElement sample, Action? openWindow = null) =>
        new Border()
            .Padding(12)
            .CornerRadius(8)
            .BorderThickness(1)
            .BorderBrush(SampleBorderColor)
            .Background(Color.FromArgb(24, 0, 0, 0))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(10)
                    .Children(
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Label().Text(title).Bold().CenterVertical(),
                                openWindow is null
                                    ? new Border().Width(0).Height(0)
                                    : new Button().Content("Open Window").OnClick(() => openWindow())
                            ),
                        new Label().Text(description),
                        sample
                    )
            );

    private static Border SampleHost(FrameworkElement child, double height = 180) =>
        new Border()
            .Height(height)
            .Padding(8)
            .CornerRadius(6)
            .BorderThickness(1)
            .BorderBrush(SampleBorderColor)
            .Background(AccentPanelColor)
            .Child(child);

    private Window CreateCaseWindow(string title, FrameworkElement content, WindowSize? size = null) =>
        new Window()
            .Title(title)
            .Build(w =>
            {
                if (size.HasValue)
                {
                    w.WindowSize = size.Value;
                }

                w.Padding(12).Content(content);
            });

    private static Border Cell(string text, Color? bg = null) =>
        new Border()
            .MinHeight(30)
            .Padding(6)
            .CornerRadius(4)
            .Background(bg ?? AccentFillColor)
            .BorderThickness(1)
            .BorderBrush(SampleBorderColor)
            .Child(new Label().Text(text).CenterVertical());
}
