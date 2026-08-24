extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;
using System.Text;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextEngineWindowsBackendTests
{
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public void Direct2D_TenMegabyteEditorTransitionsRemainRenderable()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new Direct2DGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            const int width = 640;
            const int height = 300;
            var editor = new MultiLineTextBox { Width = width, Height = height, Wrap = true };
            using var window = HeadlessWindow.Create(width, height);
            window.Content = editor;
            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));

            const string line = "The quick brown fox jumps over the lazy dog. 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ\n";
            var builder = new StringBuilder(10_000_000 + line.Length);
            while (builder.Length < 10_000_000)
            {
                builder.Append(line);
            }
            editor.Text = builder.ToString(0, 10_000_000);
            window.PerformLayout();
            window.RenderFrameToSurface(surface);

            string singleLine = new('x', 10_000_000);
            editor.Wrap = false;
            editor.Text = singleLine;
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            Assert.IsLessThan(4 * 1024, editor.MaterializedCharacterCount);

            for (int step = 1; step <= 64; step++)
            {
                editor.CaretPosition = singleLine.Length * step / 64;
                window.RenderFrameToSurface(surface);
            }

            editor.Wrap = true;
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            Assert.IsLessThan(4 * 1024, editor.MaterializedCharacterCount);
            for (int step = 1; step <= 64; step++)
            {
                editor.CaretPosition = singleLine.Length * step / 64;
                window.RenderFrameToSurface(surface);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            window.RenderFrameToSurface(surface);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void Direct2D_FullPathMeasureHitAndDrawAreSelfConsistent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        AssertBackend(factory);
        AssertTenMegabyteFastPath(factory);
        AssertEditorBackend(factory);
    }

    [TestMethod]
    public void Direct2D_RealizationCache_BoundsDistinctLayouts()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(320, 48, 1));
        using var context = factory.CreateContext(surface);
        var renderContext = (ManagedTextRenderContext)context.Text;
        context.BeginFrame(surface);
        for (int index = 0; index < 192; index++)
        {
            string text = $"cache entry {index}";
            var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest
            {
                Text = text.AsMemory(),
                DefaultStyle = new TextRunStyle("Segoe UI", 16),
                Paragraph = new TextParagraphStyle { MaxWidth = 300, Wrapping = TextWrapping.NoWrap },
                Runs = [new GeometryStyleRun(0, text.Length, new TextRunStyle("Segoe UI", 16, FontWeight.Bold))]
            });
            renderContext.Draw(layout, Point.Zero, new TextDrawOptions(Color.White));
        }
        context.EndFrame();

        Assert.AreEqual(128, renderContext.CachedLayoutCount,
            "Direct2D text-layout realizations grew beyond the bounded cache capacity.");
    }

    [TestMethod]
    public void Direct2D_ClippedColorEmoji_RemainsColor()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        const string text = "😀";
        using var factory = new Direct2DGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(64, 64, 1));
        using (var context = factory.CreateContext(surface))
        {
            var style = new TextRunStyle("Segoe UI Emoji", 40);
            var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest
            {
                Text = text.AsMemory(),
                DefaultStyle = style,
                Paragraph = new TextParagraphStyle { MaxWidth = 64, Wrapping = TextWrapping.NoWrap },
                Runs = [new GeometryStyleRun(0, text.Length, style)]
            });

            context.BeginFrame(surface);
            context.Clear(Color.Transparent);
            context.IntersectClip(new Rect(0, 0, 64, 64));
            context.Text.Draw(layout, new Point(-8, 4), new TextDrawOptions(Color.White));
            context.EndFrame();
        }

        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int colorfulPixels = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];
            if (a > 32 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 24)
            {
                colorfulPixels++;
            }
        }

        Assert.IsGreaterThan(8, colorfulPixels,
            "A partially clipped color emoji was rendered as a monochrome fallback glyph.");
    }

    [TestMethod]
    public void Gdi_FullPathMeasureHitAndDrawAreSelfConsistent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertBackend(factory);
        AssertTenMegabyteFastPath(factory);
        AssertEditorBackend(factory);
    }

    [TestMethod]
    public void MewVGWin32_FullPathMeasureHitAndDrawAreSelfConsistent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("MewVG Win32 is Windows-only.");
            return;
        }

        using var factory = new MewVGWin32GraphicsFactory();
        using var scope = factory.AcquireBackgroundRenderScope();
        AssertBackend(factory);
        AssertTenMegabyteFastPath(factory);
        AssertEditorBackend(factory);
    }

    private static void AssertBackend(IGraphicsFactory factory)
    {
        const string text = "office 한글 😀";
        const int width = 320;
        const int height = 72;
        var request = new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 18),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = width - 8,
                Wrapping = TextWrapping.Wrap
            }
        };
        var layout = factory.TextEngine.CreateLayout(request);

        Assert.HasCount(1, layout.Lines);
        var endCaret = layout.GetCaretBounds(new CharacterHit(text.Length, 0));
        Assert.AreEqual(layout.MeasuredSize.Width, endCaret.X, 1.5,
            $"{factory.Backend}: end caret and measured width diverged.");
        var endHit = layout.HitTestPoint(new Point(endCaret.X, endCaret.Y + endCaret.Height * 0.5));
        Assert.AreEqual(text.Length, endHit.InsertionIndex);

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
        ITextBackendRun? nativeRealization = null;
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.Transparent);
            context.Text.Draw(layout, new Point(4, 4), new TextDrawOptions(Color.White));
            context.EndFrame();
            if (factory is Direct2DGraphicsFactory)
            {
                nativeRealization = ((ManagedTextRenderContext)context.Text).CachedRuns.First();
            }
        }

        if (nativeRealization is not null)
        {
            Assert.AreEqual(0, nativeRealization.NativeHandle,
                "Disposing the graphics context did not release its DirectWrite text layout realization.");
        }

        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int covered = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 16)
            {
                covered++;
            }
        }
        Assert.IsGreaterThanOrEqualTo(5, covered, $"{factory.Backend}: new text draw surface produced no ink.");
    }

    private static void AssertEditorBackend(IGraphicsFactory factory)
    {
        const int width = 260;
        const int height = 120;
        var previousFactory = Application.DefaultGraphicsFactory;
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var editor = new MultiLineTextBox
            {
                Width = width,
                Height = height,
                Text = "first office 한글 😀\n" + new string('W', 80),
                Wrap = true
            };
            using var window = HeadlessWindow.Create(width, height);
            window.Content = editor;
            window.PerformLayout();
            editor.Focus();
            editor.CaretPosition = 1;
            Rect before = editor.GetCharRectInWindow(editor.CaretPosition);
            window.SendKeyPress(Key.Down);
            Rect after = editor.GetCharRectInWindow(editor.CaretPosition);

            Assert.IsGreaterThan(1, editor.CaretPosition, $"{factory.Backend}: editor caret did not move.");
            Assert.IsGreaterThan(before.Y, after.Y, $"{factory.Backend}: editor did not move by a visual row.");

            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
            window.RenderFrameToSurface(surface);
            var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
            int covered = 0;
            for (int index = 3; index < pixels.Length; index += 4)
            {
                if (pixels[index] > 16) covered++;
            }
            Assert.IsGreaterThan(5, covered, $"{factory.Backend}: MultiLineTextBox produced no ink.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private static void AssertTenMegabyteFastPath(IGraphicsFactory factory)
    {
        const int width = 320;
        const int height = 48;
        string text = new('x', 10_000_000);
        var owner = new object();
        var layout = (ManagedTextLayout)factory.TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = text.AsMemory(),
                Dpi = 96,
                DefaultStyle = new TextRunStyle("Segoe UI", 16),
                Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
                Revision = 1
            },
            TextLayoutCachePolicy.Owner,
            owner);

        Assert.IsTrue(layout.IsFastPath);
        Assert.IsGreaterThan(width, layout.MeasuredSize.Width,
            $"{factory.Backend}: 10MB line width was not measured.");
        Rect endCaret = layout.GetCaretBounds(new CharacterHit(text.Length, 0));
        Assert.AreEqual(layout.MeasuredSize.Width, endCaret.X, 0.01);

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        context.Text.Draw(layout, Point.Zero, new TextDrawOptions(Color.White, Owner: owner));
        context.EndFrame();

        Assert.IsFalse(layout.HasMaterializedColumns,
            $"{factory.Backend}: 10MB fast draw built columns for the whole line.");
        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int covered = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] > 16) covered++;
        }
        Assert.IsGreaterThan(5, covered, $"{factory.Backend}: 10MB fast path produced no ink.");
    }
}
