using Aprillz.MewUI;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class DWriteGlyphRunExtractorTests
{
    [TestMethod]
    public void Capture_CopiesGlyphAndClusterDataFromTextLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        const string text = "office 한글 😀";
        using var factory = new Direct2DGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(400, 80, 1));
        using var context = factory.CreateContext(surface);
        using var font = factory.CreateFont("Segoe UI", 16, 96);

        context.BeginFrame(surface);
        try
        {
            using var run = ((ITextBackendRenderContext)context).CreateRun(text, font, 400, 80);

            Assert.IsNotNull(run);
            Assert.AreNotEqual(0, run.NativeHandle);

            var runs = DWriteGlyphRunExtractor.Capture(run.NativeHandle);

            Assert.IsNotEmpty(runs);
            Assert.AreEqual(text.Length, runs.Sum(run => checked((int)run.TextLength)));
            Assert.IsGreaterThanOrEqualTo(2, runs.Select(run => run.FaceIndex).Distinct().Count(),
                "The mixed Latin/Hangul/emoji sample should preserve fallback face boundaries.");
            foreach (var capturedRun in runs)
            {
                Assert.IsGreaterThan(0, capturedRun.GlyphIndices.Length);
                Assert.HasCount(capturedRun.GlyphIndices.Length, capturedRun.Advances);
                Assert.HasCount(capturedRun.GlyphIndices.Length, capturedRun.Offsets);
                Assert.HasCount(checked((int)capturedRun.TextLength), capturedRun.ClusterMap);
                Assert.IsGreaterThan(0, capturedRun.Advances.Sum());
            }
        }
        finally
        {
            context.EndFrame();
        }
    }
}
