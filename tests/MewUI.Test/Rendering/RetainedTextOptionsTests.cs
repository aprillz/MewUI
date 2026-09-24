using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Colored text hands its spans over in a buffer the drawing code builds anew on every render, as a
/// highlighter or an editor does. Text drawn with the same spans is the same drawing whichever buffer
/// carried them, so a caret blinking under it repaints the caret and not the text.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedTextOptionsTests
{
    private const int WIDTH = 320;
    private const int HEIGHT = 240;
    private const int LINE_HEIGHT = 24;
    private const int LINE_COUNT = 6;
    private const double CARET_HEIGHT = 18;

    // An edge the repaint pads for antialiasing, in DIPs.
    private const double EDGE_SLACK = 2;

    [TestMethod]
    public void ACaretBlinkingUnderColoredText_RepaintsOnlyTheCaret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var lines = new ColoredLines();
        var (window, surface) = Show(factory, lines);
        using (surface)
        {
            lines.CaretVisible = false;
            Render(window, surface);

            var dirtyRect = window.LastRetainedDirtyRect;
            var caret = lines.CaretBounds;
            Assert.IsTrue(
                dirtyRect is Rect area && area.Y >= caret.Y - EDGE_SLACK && area.Bottom <= caret.Bottom + EDGE_SLACK,
                $"the caret at {caret} blinked and the frame repainted {dirtyRect?.ToString() ?? "everything"} ({window.LastWholeFrameReason})");

            AssertMatchesReference(factory, window, surface);
        }
    }

    [TestMethod]
    public void ANewSpanColor_RepaintsItsLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var lines = new ColoredLines();
        var (window, surface) = Show(factory, lines);
        using (surface)
        {
            lines.KeywordColor = Color.FromArgb(255, 200, 40, 40);
            Render(window, surface);

            var dirtyRect = window.LastRetainedDirtyRect;
            Assert.IsNotNull(dirtyRect, $"the frame was drawn whole ({window.LastWholeFrameReason})");
            Assert.IsGreaterThan(0, dirtyRect.Value.Height, "the span color changed and nothing was repainted");
            AssertMatchesReference(factory, window, surface);
        }
    }

    private static (Window Window, IRenderSurface Surface) Show(GdiGraphicsFactory factory, ColoredLines lines)
    {
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = lines;
        window.PerformLayout();
        var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            Render(window, surface);
        }

        return (window, surface);
    }

    private static void Render(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface surface)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int differing = 0;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] || expected[offset + 1] != shown[offset + 1] || expected[offset + 2] != shown[offset + 2])
            {
                differing++;
            }
        }

        Assert.AreEqual(0, differing, $"{differing} pixels differ from a frame drawn straight from the visuals");
    }

    /// <summary>Lines with a colored keyword and a caret under the last one, drawn in one render.</summary>
    private sealed class ColoredLines : Control
    {
        private bool _caretVisible = true;
        private Color _keywordColor = Color.FromArgb(255, 40, 90, 200);

        public bool CaretVisible
        {
            get => _caretVisible;
            set
            {
                _caretVisible = value;
                InvalidateVisual();
            }
        }

        public Color KeywordColor
        {
            get => _keywordColor;
            set
            {
                _keywordColor = value;
                InvalidateVisual();
            }
        }

        public Rect CaretBounds => new(Bounds.X + 40, Bounds.Y + (LINE_COUNT * LINE_HEIGHT) + 3, 2, CARET_HEIGHT);

        protected override Size MeasureContent(Size availableSize) => new(280, (LINE_COUNT + 1) * LINE_HEIGHT);

        protected override void OnRender(IGraphicsContext context)
        {
            var bounds = Bounds;
            context.FillRectangle(bounds, Color.FromArgb(255, 250, 250, 250));
            var style = GetTextRunStyle();
            for (int line = 0; line < LINE_COUNT; line++)
            {
                var lineBounds = new Rect(bounds.X + 8, bounds.Y + (line * LINE_HEIGHT), 260, LINE_HEIGHT);
                var layout = TextLayoutOperations.GetOrCreate(
                    GetGraphicsFactory(), $"var value{line} = compute({line});", GetDpi(), in style, lineBounds.Width, lineBounds.Height);

                // A fresh buffer every render, as a highlighter builds its spans.
                var spans = new[] { new TextPaintSpan(new TextRange(0, 3), Foreground: _keywordColor) };
                TextLayoutOperations.DrawInBounds(
                    context, layout, lineBounds, Color.FromArgb(255, 30, 30, 30), TextAlignment.Left, paintSpans: spans);
            }

            if (_caretVisible)
            {
                context.FillRectangle(CaretBounds, Color.FromArgb(255, 0, 0, 0));
            }
        }
    }
}
