extern alias MewVGWin32;

using System;
using System.Collections.Generic;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// Glyph ink that reaches past a run's advance box (an italic f) or line box (a flush descender) has to
/// survive on every Windows backend. The tight run is compared with a reference draw of the same glyphs
/// whose run box has room to spare: trailing spaces widen the box, a taller line height deepens it by a
/// known half-leading, so any pixel the tight draw loses shows up as a shorter ink extent.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextRunInkOverhangTests
{
    private const double FONT_SIZE = 29.12;
    private const double ORIGIN = 20;
    private const double EXTRA_LINE_HEIGHT = 8;

    private static IEnumerable<object[]> Backends()
    {
        yield return ["Direct2D"];
        yield return ["Gdi"];
        yield return ["MewVGWin32"];
    }

    [TestMethod]
    [DynamicData(nameof(Backends), DynamicDataSourceType.Method)]
    public void ItalicOverhang_SurvivesTheRunBox(string backend)
    {
        RunOnBackend(backend, factory =>
        {
            foreach (double scale in new[] { 1.0, 1.25, 1.5 })
            {
                foreach (string family in new[] { "Segoe UI", "Malgun Gothic" })
                {
                    var style = new TextRunStyle(family, FONT_SIZE, FontWeight.Bold, true);
                    var tight = InkExtent(factory, scale, "ff", style, null);
                    var reference = InkExtent(factory, scale, "ff  ", style, null);
                    Console.WriteLine($"[{backend} {family} x{scale}] ff right tight={tight.Right} reference={reference.Right} runWidthPx={tight.RunWidthPx:F1}");
                    Assert.AreEqual(reference.Right, tight.Right,
                        $"{backend} {family} x{scale}: the italic overhang was clipped at the run box.");
                }

                var segoe = new TextRunStyle("Segoe UI", FONT_SIZE, FontWeight.Bold, true);
                var advance = InkExtent(factory, scale, "ff", segoe, null);
                var widened = InkExtent(factory, scale, "ff  ", segoe, null);
                Assert.IsGreaterThan(advance.RunWidthPx, widened.Right,
                    $"{backend} x{scale}: the reference glyphs do not overhang their advance box, so this check proves nothing.");
            }
        });
    }

    [TestMethod]
    [DynamicData(nameof(Backends), DynamicDataSourceType.Method)]
    public void FlushDescender_SurvivesTheLineBox(string backend)
    {
        RunOnBackend(backend, factory =>
        {
            foreach (double scale in new[] { 1.0, 1.25, 1.5 })
            {
                foreach (string family in new[] { "Malgun Gothic", "Segoe UI" })
                {
                    var style = new TextRunStyle(family, FONT_SIZE, FontWeight.Bold);
                    // A paint span keeps both draws on the run path, whose box follows the line baseline;
                    // the taller reference line lowers that baseline by half the extra height.
                    var paints = new[] { new TextPaintSpan(new TextRange(0, 1), Color.Black) };
                    var tight = InkExtent(factory, scale, "Heading g", style, null, paints);
                    var reference = InkExtent(factory, scale, "Heading g", style, tight.LineHeight + EXTRA_LINE_HEIGHT, paints);
                    int halfLeadingPx = (int)Math.Round(EXTRA_LINE_HEIGHT / 2 * scale);
                    Console.WriteLine($"[{backend} {family} x{scale}] g bottom tight={tight.Bottom} reference={reference.Bottom} shift={halfLeadingPx}");
                    Assert.AreEqual(reference.Bottom - halfLeadingPx, tight.Bottom,
                        $"{backend} {family} x{scale}: the descender was clipped at the line box.");
                }
            }
        });
    }

    [TestMethod]
    [DynamicData(nameof(Backends), DynamicDataSourceType.Method)]
    public void ColourSplitRun_KeepsTheOverhangOfItsOuterSegments(string backend)
    {
        RunOnBackend(backend, factory =>
        {
            var style = new TextRunStyle("Segoe UI", FONT_SIZE, FontWeight.Bold, true);
            var split = new[]
            {
                new TextPaintSpan(new TextRange(0, 1), Color.FromArgb(255, 200, 0, 0)),
                new TextPaintSpan(new TextRange(1, 1), Color.FromArgb(255, 0, 0, 200))
            };
            var tight = InkExtent(factory, 1.0, "ff", style, null, split);
            var reference = InkExtent(factory, 1.0, "ff  ", style, null);
            Console.WriteLine($"[{backend}] split ff right tight={tight.Right} reference={reference.Right}");
            Assert.AreEqual(reference.Right, tight.Right,
                $"{backend}: the colour split clipped the last segment at the run box.");
        });
    }

    private static void RunOnBackend(string backend, Action<IGraphicsFactory> body)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows backends only.");
            return;
        }

        switch (backend)
        {
            case "Direct2D":
            {
                using var factory = new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory();
                body(factory);
                break;
            }
            case "Gdi":
            {
                using var factory = new Aprillz.MewUI.Rendering.Gdi.GdiGraphicsFactory();
                body(factory);
                break;
            }
            default:
            {
                using var factory = new MewVGWin32GraphicsFactory();
                using var backgroundScope = factory.AcquireBackgroundRenderScope();
                body(factory);
                break;
            }
        }
    }

    private readonly record struct InkResult(int Right, int Bottom, double RunWidthPx, double LineHeight);

    /// <summary>Draws one line at the origin and returns its ink extents in device pixels from the origin.</summary>
    private static InkResult InkExtent(
        IGraphicsFactory factory,
        double scale,
        string text,
        TextRunStyle style,
        double? lineHeight,
        TextPaintSpan[]? paints = null)
    {
        const int widthPx = 400;
        const int heightPx = 160;
        uint dpi = (uint)Math.Round(96 * scale);
        var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = dpi,
            DefaultStyle = style,
            Paragraph = new TextParagraphStyle { MaxWidth = 1000, LineHeight = lineHeight },
            Transient = true
        });

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(widthPx, heightPx, scale));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            var options = new TextDrawOptions(Color.Black, paints ?? Array.Empty<TextPaintSpan>(), Transient: true);
            context.Text.Draw(layout, new Point(ORIGIN, ORIGIN), in options);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        int rows = pixels.Length / stride;
        int columns = stride / 4;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int offset = y * stride + x * 4;
                if (pixels[offset] < 250 || pixels[offset + 1] < 250 || pixels[offset + 2] < 250)
                {
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        int originPx = (int)Math.Round(ORIGIN * scale);
        return new InkResult(
            right + 1 - originPx,
            bottom + 1 - originPx,
            layout.MeasuredSize.Width * scale,
            layout.Lines[0].Bounds.Height);
    }
}
