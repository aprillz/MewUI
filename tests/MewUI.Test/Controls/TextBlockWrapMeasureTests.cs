using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class TextBlockWrapMeasureTests
{
    private const string LongText =
        "The Slider writes the number back. The other targets only render it, " +
        "and the TextBlock uses a converter.";

    private sealed class MeasureWidthProbe : TextBlock
    {
        public double LastAvailableWidth = double.NaN;

        protected override Size MeasureContent(Size availableSize)
        {
            LastAvailableWidth = availableSize.Width;
            return base.MeasureContent(availableSize);
        }
    }

    [TestMethod]
    public void WrapPanelMeasuresChildrenWithTheLineWidth()
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
            var probe = new MeasureWidthProbe();
            probe.Text = LongText;
            probe.TextWrapping = TextWrapping.Wrap;

            var panel = new WrapPanel().Orientation(Orientation.Horizontal).Children(probe);
            using var window = HeadlessWindow.Create(320, 400);
            window.Content = panel;
            window.PerformLayout();

            Assert.IsFalse(double.IsPositiveInfinity(probe.LastAvailableWidth),
                "WrapPanel measured a child with an unbounded width, so self-wrapping content reports one line.");
            Assert.IsGreaterThanOrEqualTo(probe.DesiredSize.Height, probe.Bounds.Height,
                "The arranged box is shorter than the wrapped text needs.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void WrappedBlockRemeasuresWhenArrangedNarrowerThanMeasured()
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
            var wrapped = new TextBlock().Text(LongText).TextWrapping(TextWrapping.Wrap);

            // Containers commonly measure with an unbounded width and then arrange narrower.
            wrapped.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double unconstrainedHeight = wrapped.DesiredSize.Height;

            wrapped.Arrange(new Rect(0, 0, 380, wrapped.DesiredSize.Height));
            wrapped.Measure(new Size(380, double.PositiveInfinity));

            Assert.IsGreaterThan(unconstrainedHeight, wrapped.DesiredSize.Height,
                $"Re-measuring at the arranged width kept the single-line height ({wrapped.DesiredSize.Height}).");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void WrappedBlockReservesEveryLineInAStackPanel()
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
            var wrapped = new TextBlock().Text(LongText).TextWrapping(TextWrapping.Wrap);
            var below = new TextBlock().Text("TwoWay target (Slider):");
            var panel = new StackPanel().Vertical().Spacing(8).Children(wrapped, below);

            using var window = HeadlessWindow.Create(380, 400);
            window.Content = panel;
            window.PerformLayout();

            double singleLine = below.Bounds.Height;
            Assert.IsGreaterThan(singleLine * 1.5, wrapped.Bounds.Height,
                $"Wrapped text kept a single line box (wrapped={wrapped.Bounds.Height}, single={singleLine}).");
            Assert.IsGreaterThanOrEqualTo(wrapped.Bounds.Bottom + 8, below.Bounds.Y,
                "The next element overlaps the wrapped text.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }
}
