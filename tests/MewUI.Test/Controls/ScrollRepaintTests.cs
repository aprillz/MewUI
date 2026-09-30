using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Scrolling has to move what is on screen, whichever stage of a frame stands the lines up. Each
/// case drives whole frames - layout then paint, as a window does - so it says the same thing
/// whether the lines are built while arranging or while painting.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ScrollRepaintTests
{
    private const int WIDTH = 300;
    private const int HEIGHT = 200;

    [TestMethod]
    public void TheWheelMovesTheVisibleLines()
    {
        using var host = MultiLineHost();

        host.Frame();
        int topBefore = host.TopLine;
        for (int notch = 0; notch < 10; notch++)
        {
            host.Window.SendMouseWheel(host.Element.CenterOf(), -3);
        }
        host.Frame();

        Assert.IsGreaterThan(topBefore, host.TopLine, "the wheel moved the offset but not the content");
    }

    /// <summary>
    /// The offset alone is not evidence: it is a field the scroll sets, so a viewport that stopped
    /// following it would still report the new value.
    /// </summary>
    [TestMethod]
    public void TheWheelMovesTheContentAsFarAsTheOffset()
    {
        using var host = MultiLineHost();

        host.Frame();
        var topBefore = host.TopLineY;
        for (int notch = 0; notch < 10; notch++)
        {
            host.Window.SendMouseWheel(host.Element.CenterOf(), -3);
        }
        host.Frame();

        double offsetMoved = host.Element.VerticalOffset;
        double contentMoved = host.TopLineY - topBefore;
        Assert.AreEqual(offsetMoved, contentMoved, ((ITextViewHost)host.Element).DefaultLineHeight,
            "the content did not travel as far as the scroll offset says it did");
    }

    /// <summary>
    /// Standing the lines up belongs to the layout pass, so a wheel notch is not on screen until
    /// one runs. A caller that scrolls and reads the viewport in the same breath lays out first;
    /// a window does that every frame.
    /// </summary>
    [TestMethod]
    public void APaintAloneDoesNotStandTheScrolledLinesUp()
    {
        using var host = MultiLineHost();

        host.Frame();
        int topBefore = host.TopLine;
        for (int notch = 0; notch < 10; notch++)
        {
            host.Window.SendMouseWheel(host.Element.CenterOf(), -3);
        }
        host.Paint();

        Assert.AreEqual(topBefore, host.TopLine, "the paint stood the lines up, which is the layout pass's work");

        host.Frame();

        Assert.IsGreaterThan(topBefore, host.TopLine, "the layout that followed never caught up");
    }

    [TestMethod]
    public void WrappedLinesKeepTheirAnchorAcrossAScroll()
    {
        using var host = MultiLineHost(wrap: true);

        host.Frame();
        for (int notch = 0; notch < 10; notch++)
        {
            host.Window.SendMouseWheel(host.Element.CenterOf(), -3);
        }
        host.Frame();
        int anchor = host.TopLine;
        double offset = host.Element.VerticalOffset;

        for (int frame = 0; frame < 3; frame++)
        {
            host.Frame();
        }

        Assert.AreEqual(anchor, host.TopLine, "the wrapped anchor drifted while the frames settled");
        Assert.AreEqual(offset, host.Element.VerticalOffset);
    }

    [TestMethod]
    public void TheSyntaxViewerScrollsItsContentToo()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        var viewer = new SyntaxViewer().Width(WIDTH).Height(HEIGHT);
        viewer.Text = LongText();
        window.Content = viewer;
        window.PerformLayout();
        using var surface = Application.DefaultGraphicsFactory
            .CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        Frame(window, viewer, surface);
        int topBefore = ((ITextViewHost)viewer).VisibleTextLines[0].LogicalLine.LineNumber;
        for (int notch = 0; notch < 10; notch++)
        {
            window.SendMouseWheel(viewer.CenterOf(), -3);
        }
        Frame(window, viewer, surface);

        Assert.IsGreaterThan(topBefore, ((ITextViewHost)viewer).VisibleTextLines[0].LogicalLine.LineNumber,
            "the viewer moved the offset but not the content");
    }

    /// <summary>
    /// A line long enough to be laid out in slices is cut around the horizontal offset. Dragging the
    /// scroll bar across it has to cut it again there, or the text ends where the old slice ended.
    /// </summary>
    [TestMethod]
    public void TheHorizontalScrollBarMovesALongLinesSliceAlong()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var textBox = new MultiLineTextBox().Width(WIDTH).Height(HEIGHT).Text(SlicedText());
        textBox.Wrap = false;
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = textBox;
        window.PerformLayout();
        using var surface = Application.DefaultGraphicsFactory
            .CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        Frame(window, textBox, surface);
        Assert.IsTrue(textBox.IsHorizontalScrollBarVisible, "the long line did not make the box scroll sideways");
        DragHorizontalScrollBar(textBox, 3_000);
        Frame(window, textBox, surface);

        AssertSliceCoversViewport(textBox, textBox.HorizontalOffset, "the text box");
    }

    [TestMethod]
    public void TheSyntaxViewersHorizontalScrollBarMovesALongLinesSliceAlong()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var viewer = new SyntaxViewer().Width(WIDTH).Height(HEIGHT);
        viewer.Text = SlicedText();
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = viewer;
        window.PerformLayout();
        using var surface = Application.DefaultGraphicsFactory
            .CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        Frame(window, viewer, surface);
        Assert.IsTrue(viewer.IsHorizontalScrollBarVisible, "the long line did not make the viewer scroll sideways");
        DragHorizontalScrollBar(viewer, 3_000);
        Frame(window, viewer, surface);

        AssertSliceCoversViewport(viewer, viewer.HorizontalOffset, "the viewer");
    }

    /// <summary>Sets the horizontal scroll bar's value the way dragging its thumb does.</summary>
    private static void DragHorizontalScrollBar(FrameworkElement element, double value)
    {
        var field = element.GetType().GetField("_horizontalScrollBar",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((ScrollBar)field.GetValue(element)!).Value = value;
    }

    private static void AssertSliceCoversViewport(FrameworkElement element, double horizontalOffset, string what)
    {
        Assert.IsGreaterThan(1_000, horizontalOffset, $"{what} did not scroll far enough to need another slice");
        var line = ((ITextViewHost)element).VisibleTextLines.Single(static line => line.LogicalLine.LineNumber == 1);
        double left = line.DocumentX;
        double right = left + line.VisualLines.Max(static visual => visual.Bounds.Right);
        Assert.IsLessThanOrEqualTo(horizontalOffset, left,
            $"{what} draws the long line from {left:0}, right of the offset {horizontalOffset:0}");
        Assert.IsGreaterThanOrEqualTo(horizontalOffset + WIDTH / 2, right,
            $"{what} ends the long line at {right:0}, short of the viewport at {horizontalOffset:0}: the slice was not cut again");
    }

    /// <summary>Short lines around one far past the slicing threshold.</summary>
    private static string SlicedText()
        => "first\n" + string.Join(' ', Enumerable.Range(0, 800).Select(static word => $"w{word}")) + "\nlast";

    private static string LongText()
        => string.Join('\n', Enumerable.Range(0, 4_000)
            .Select(static line => $"line {line} " + new string('W', line % 3 == 0 ? 120 : 8)));

    private static void Frame(Window window, FrameworkElement element, IRenderSurface surface)
    {
        window.PerformLayout();
        using var context = Application.DefaultGraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        element.Render(context);
        context.EndFrame();
    }

    private static Host MultiLineHost(bool wrap = false)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }
        var textBox = new MultiLineTextBox().Width(WIDTH).Height(HEIGHT).Text(LongText());
        textBox.Wrap = wrap;
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = textBox;
        window.PerformLayout();
        return new Host(window, textBox);
    }

    private sealed class Host(Window window, MultiLineTextBox element) : IDisposable
    {
        private readonly IRenderSurface _surface = Application.DefaultGraphicsFactory
            .CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        public Window Window { get; } = window;

        public MultiLineTextBox Element { get; } = element;

        public int TopLine => ((ITextViewHost)Element).VisibleTextLines[0].LogicalLine.LineNumber;

        public double TopLineY => ((ITextViewHost)Element).VisibleTextLines[0].DocumentY;

        public void Frame() => ScrollRepaintTests.Frame(Window, Element, _surface);

        /// <summary>Paints without laying out, which is what a repaint-only frame does.</summary>
        public void Paint()
        {
            using var context = Application.DefaultGraphicsFactory.CreateContext(_surface);
            context.BeginFrame(_surface);
            Element.Render(context);
            context.EndFrame();
        }

        public void Dispose() => _surface.Dispose();
    }
}
