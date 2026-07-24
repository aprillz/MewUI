using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.WindowSizeTest;

partial class WindowSizeTestView : UserControl
{
    private readonly Window _owner;

    public WindowSizeTestView(Window owner)
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
                    .Spacing(20)
                    .Children(
                        new Label().Text("WindowSize API Test").FontSize(20).Bold(),
                        Section("Group A: Initial WindowSize", GroupAContent()),
                        Section("Group B: Runtime Transitions", GroupBContent())
                    )
            );

    private static FrameworkElement Section(string title, FrameworkElement content) =>
        new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new Label().Text(title).FontSize(16).Bold(),
                content
            );

    private static FrameworkElement SizedContent(double w, double h) =>
        new Border()
            .Width(w)
            .Height(h)
            .Background(Color.FromArgb(40, 0, 120, 215))
            .Child(new Label().Text($"Content: {w} x {h}").CenterHorizontal().CenterVertical());

    private static Window CreateTestWindow(string id, string desc, FrameworkElement? extraContent = null) =>
        new Window()
            .Title($"[{id}] {desc}")
            .Build(x => x
                .Padding(12)
                .Content(
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new Label().Text($"[{id}] {desc}").Bold(),
                            extraContent ?? new Label().Text("Resize this window to test constraints.")
                        )
                )
            );
}
