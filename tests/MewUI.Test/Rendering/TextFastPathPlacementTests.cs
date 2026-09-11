extern alias MewVGWin32;

using System;
using System.Collections.Generic;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A single-style line draws through the fast path until a paint span sends it through the run path. Both
/// have to put the glyphs on the line's baseline, including the half-leading a taller line box adds, or text
/// jumps when a selection or colour span appears and sits off the caret the layout reports.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextFastPathPlacementTests
{
    private const double FONT_SIZE = 20;
    private const double ORIGIN = 12;

    private static IEnumerable<object[]> Backends()
    {
        yield return ["Direct2D"];
        yield return ["Gdi"];
        yield return ["MewVGWin32"];
    }

    [TestMethod]
    [DynamicData(nameof(Backends), DynamicDataSourceType.Method)]
    public void FastPath_DrawsOnTheSameRowsAsTheRunPath(string backend)
    {
        RunOnBackend(backend, factory =>
        {
            var mismatches = new List<string>();
            foreach (double scale in new[] { 1.0, 1.25, 1.5 })
            {
                foreach (string family in new[] { "Segoe UI", "Malgun Gothic" })
                {
                    foreach (double? lineHeight in new double?[] { null, 40 })
                    {
                        var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest
                        {
                            Text = "Heading g".AsMemory(),
                            Dpi = (uint)Math.Round(96 * scale),
                            DefaultStyle = new TextRunStyle(family, FONT_SIZE),
                            Paragraph = new TextParagraphStyle { MaxWidth = 1000, LineHeight = lineHeight },
                            Transient = true
                        });
                        Assert.IsTrue(((ManagedTextLayout)layout).IsFastPath, "fixture is not a fast-path layout");

                        var fast = InkRows(factory, scale, layout, []);
                        var run = InkRows(factory, scale, layout, [new TextPaintSpan(new TextRange(0, 1), Color.Black)]);
                        Console.WriteLine(
                            $"[{backend} {family} x{scale} lineHeight={lineHeight?.ToString() ?? "natural"}] " +
                            $"fast={fast.Top}..{fast.Bottom} run={run.Top}..{run.Bottom} baseline={layout.Lines[0].Baseline:F2}");
                        if (fast != run)
                        {
                            mismatches.Add($"{family} x{scale} lineHeight={lineHeight?.ToString() ?? "natural"}: fast {fast.Top}..{fast.Bottom}, run {run.Top}..{run.Bottom}");
                        }
                    }
                }
            }

            Assert.IsEmpty(mismatches, $"{backend}: {string.Join("; ", mismatches)}");
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

    private static (int Top, int Bottom) InkRows(IGraphicsFactory factory, double scale, ITextLayout layout, TextPaintSpan[] paints)
    {
        const int widthPx = 320;
        const int heightPx = 120;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(widthPx, heightPx, scale));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            var options = new TextDrawOptions(Color.Black, paints, Transient: true);
            context.Text.Draw(layout, new Point(ORIGIN, ORIGIN), in options);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        int rows = pixels.Length / stride;
        int columns = stride / 4;
        int top = -1;
        int bottom = -1;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int offset = y * stride + x * 4;
                if (pixels[offset] < 250 || pixels[offset + 1] < 250 || pixels[offset + 2] < 250)
                {
                    if (top < 0)
                    {
                        top = y;
                    }
                    bottom = y;
                    break;
                }
            }
        }

        return (top, bottom);
    }
}
