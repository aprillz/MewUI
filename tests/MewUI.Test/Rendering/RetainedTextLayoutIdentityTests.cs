using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Text laid out again with the same inputs draws the same, whether the layout it gets is the object
/// drawn last frame or a new one. A frame repaints only where the drawing changed, and a text block
/// measured again without a change keeps the layouts it has.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedTextLayoutIdentityTests
{
    private const int WIDTH = 320;
    private const int HEIGHT = 200;
    private const int REMEASURES = 10;

    [TestMethod]
    public void TextLaidOutAnewWithTheSameInputs_IsNotRepainted()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        var lines = new FreshLayoutLines();
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = lines;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            Render(window, surface);
        }

        lines.InvalidateVisual();
        Render(window, surface);

        var dirtyRect = window.LastRetainedDirtyRect;
        Assert.IsTrue(
            dirtyRect is Rect area && (area.Width <= 0 || area.Height <= 0),
            $"the lines were drawn again unchanged and the frame repainted {dirtyRect?.ToString() ?? "everything"} ({window.LastWholeFrameReason})");
    }

    [TestMethod]
    [DataRow(TextAlignment.Left, TextWrapping.NoWrap)]
    [DataRow(TextAlignment.Right, TextWrapping.NoWrap)]
    [DataRow(TextAlignment.Left, TextWrapping.Wrap)]
    public void ATextBlockMeasuredAgain_KeepsItsLayouts(TextAlignment alignment, TextWrapping wrapping)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        var text = new CountingTextBlock { Text = "12.5%", TextAlignment = alignment, TextWrapping = wrapping, Width = 90 };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new StackPanel().Children(text);
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Render(window, surface);
        Render(window, surface);

        int builtBefore = text.LayoutsBuilt;
        int repaints = 0;
        for (int round = 0; round < REMEASURES; round++)
        {
            text.InvalidateMeasure();
            Render(window, surface);
            if (window.LastRetainedDirtyRect is not Rect { Width: 0 } && window.LastRetainedDirtyRect is not Rect { Height: 0 })
            {
                repaints++;
            }
        }

        Assert.AreEqual(0, text.LayoutsBuilt - builtBefore, $"{REMEASURES} measures of unchanged text built {text.LayoutsBuilt - builtBefore} layouts");
        Assert.AreEqual(0, repaints, $"{REMEASURES} measures of unchanged text repainted {repaints} frames");
    }

    private static void Render(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    /// <summary>Counts the layouts the text block builds: it is asked for its geometry runs once per build.</summary>
    private sealed class CountingTextBlock : TextBlock
    {
        public int LayoutsBuilt { get; private set; }

        protected override void OnGetTextGeometryRuns(in TextRunStyle defaultStyle, IList<GeometryStyleRun> output)
        {
            LayoutsBuilt++;
            base.OnGetTextGeometryRuns(in defaultStyle, output);
        }
    }

    /// <summary>Lines whose layouts are built anew on every render.</summary>
    private sealed class FreshLayoutLines : Control
    {
        protected override Size MeasureContent(Size availableSize) => new(300, 160);

        protected override void OnRender(IGraphicsContext context)
        {
            var bounds = Bounds;
            var style = GetTextRunStyle();
            for (int line = 0; line < 5; line++)
            {
                // No cache: every render gets a new layout object for the same text.
                var layout = GetGraphicsFactory().TextEngine.GetOrCreateLayout(
                    new TextLayoutRequest
                    {
                        Text = $"line {line}: the same text every frame".AsMemory(),
                        Dpi = GetDpi(),
                        DefaultStyle = style,
                        Paragraph = new TextParagraphStyle { MaxWidth = 260, MaxHeight = 24 },
                    },
                    TextLayoutCachePolicy.None);
                TextLayoutOperations.DrawInBounds(
                    context, layout, new Rect(bounds.X + 8, bounds.Y + line * 28, 260, 24), Color.FromArgb(255, 30, 30, 30), TextAlignment.Left);
            }
        }
    }
}
