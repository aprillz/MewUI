using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>A wrapped text block is laid out tall enough for every line it draws at its arranged width.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WrappedTextHeightTests
{
    private const string LONG_TEXT =
        "Wrapped text reserves a line box for every line it draws, at whatever width its panel arranges it, " +
        "so the element below never paints over the last line.";

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    public void InVerticalStackPanel(uint dpi) => Run(dpi, wrapped =>
        new StackPanel().Vertical().Spacing(4).Children(wrapped, new TextBlock().Text("below")));

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    public void InGridStarColumn(uint dpi) => Run(dpi, wrapped =>
    {
        var grid = new Grid().Columns("*,80").Rows("Auto,Auto");
        grid.Add(wrapped);
        var below = new TextBlock().Text("below");
        Grid.SetRow(below, 1);
        grid.Add(below);
        return grid;
    });

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    public void InScrollViewerWithVisibleBar(uint dpi) => Run(dpi, wrapped =>
        new ScrollViewer
        {
            VerticalScroll = ScrollMode.Visible,
            Padding = new Thickness(9),
            Content = new StackPanel().Vertical().Spacing(4).Children(
                wrapped,
                new Border { Height = 600 }),
        });

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    public void InScrollViewerWithHiddenBar(uint dpi) => Run(dpi, wrapped =>
        new ScrollViewer
        {
            VerticalScroll = ScrollMode.Auto,
            Padding = new Thickness(9),
            Content = new StackPanel().Vertical().Spacing(4).Children(wrapped, new TextBlock().Text("below")),
        });

    private static void Run(uint dpi, Func<TextBlock, Element> build)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var wrapped = new TextBlock().Text(LONG_TEXT).TextWrapping(TextWrapping.Wrap);
            using var window = HeadlessWindow.Create(301, 400);
            window.SetDpi(dpi);
            window.Content = build(wrapped);
            window.PerformLayout();

            var (lineCount, contentHeight) = wrapped.GetRenderLayoutMetrics();
            double devicePixel = 96.0 / dpi;
            Assert.IsGreaterThan(1, lineCount, "the text did not wrap at this width");
            Assert.IsLessThanOrEqualTo(wrapped.Bounds.Height + devicePixel / 2, contentHeight,
                $"{lineCount} lines need {contentHeight} but the box is {wrapped.Bounds.Height} tall at width {wrapped.Bounds.Width}");
            Assert.IsFalse(wrapped.IsMeasureDirty, "the text block was left measure-dirty");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }
}
