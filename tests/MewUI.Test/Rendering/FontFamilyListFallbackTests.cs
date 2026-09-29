extern alias direct2d;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using MewUI.Test.Infrastructure;

using DWriteRendering = direct2d::Aprillz.MewUI.Rendering.DirectWrite;

namespace MewUI.Test.Rendering;

/// <summary>
/// A comma-separated font family draws with its first installed family, and the families after it
/// supply the characters that one lacks, before the fallback chain and the system are asked.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory and the fallback chain.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FontFamilyListFallbackTests
{
    private const int WIDTH = 120;
    private const int HEIGHT = 60;
    private const double SIZE = 32;

    // A Han character Consolas lacks, drawn by a serif CJK family no system fallback picks for it.
    private const string HAN = "行";
    private const string LATIN_FAMILY = "Consolas";
    private const string LISTED_FAMILY = "Batang";

    [TestMethod]
    public void Direct2D_ListedFamilySuppliesMissingCharacters()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        var previousFactory = Application.DefaultGraphicsFactory;
        Application.DefaultGraphicsFactory = factory;
        try
        {
            AssertListedFamilyDraws(family => Ink(factory, family));
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    // The GDI and MewVG backends draw text through this rasterizer when the app selects DirectWrite text.
    [TestMethod]
    public void DirectWriteRasterizer_ListedFamilySuppliesMissingCharacters()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        using var fonts = new DWriteRendering.DirectWriteFontFactory();
        AssertListedFamilyDraws(family =>
        {
            var font = fonts.CreateFont(family, SIZE, FontWeight.Normal, false, false, false, 96);
            var bitmap = DWriteRendering.DirectWriteTextRasterizer.Rasterize(
                fonts.Factory, font, HAN, WIDTH, HEIGHT, Color.FromArgb(255, 0, 0, 0),
                TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap, TextTrimming.None, pixelsPerDip: 1f);
            return Shape(bitmap.Data, WIDTH * 4, bitmap.WidthPx, bitmap.HeightPx, (data, offset) => data[offset + 3] >= 128);
        });
    }

    private static void AssertListedFamilyDraws(Func<string, string> ink)
    {
        var previousChain = FontFallback.FallbackChain.ToArray();
        FontFallback.ClearFallbacks();
        try
        {
            var listed = ink($"{LATIN_FAMILY}, {LISTED_FAMILY}");
            var alone = ink(LISTED_FAMILY);
            var latinOnly = ink(LATIN_FAMILY);

            Assert.AreNotEqual(alone, latinOnly, "the system's fallback draws the character as the listed family would, so this probe cannot tell them apart");
            Assert.AreEqual(alone, listed, $"the character was not drawn from {LISTED_FAMILY}");
        }
        finally
        {
            FontFallback.SetFallbacks(previousChain);
        }
    }

    private static string Ink(IGraphicsFactory factory, string family)
    {
        var block = new TextBlock { Text = HAN, FontFamily = family, FontSize = SIZE, Foreground = Color.Black };
        using var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Background = Color.White;
        window.Content = block;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        var cpu = (ICpuPixelSurface)surface;
        return Shape(cpu.GetReadOnlyPixelSpan().ToArray(), cpu.StrideBytes, WIDTH, HEIGHT, (data, offset) => data[offset] < 128);
    }

    /// <summary>The drawn glyph, cropped to its ink so the primary family's line metrics do not move it.</summary>
    private static string Shape(byte[] pixels, int stride, int width, int height, Func<byte[], int, bool> inked)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (inked(pixels, y * stride + x * 4))
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        if (right < 0)
        {
            return "nothing drawn";
        }

        var shape = new System.Text.StringBuilder($"{right - left + 1}x{bottom - top + 1}:");
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                shape.Append(inked(pixels, y * stride + x * 4) ? '#' : '.');
            }
        }

        return shape.ToString();
    }
}
