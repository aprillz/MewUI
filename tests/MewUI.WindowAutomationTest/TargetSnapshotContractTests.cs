using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// What every backend does when a frame reads its own target partway through, as a backdrop does: the
/// copy holds the pixels drawn so far unchanged, the drawing queued before it is not drawn again, and the
/// clip in effect still clips after it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TargetSnapshotContractTests
{
    private const int SIZE = 64;
    private const int TOLERANCE = 1;
    private static readonly Rect _copied = new(8, 8, 40, 32);

    [TestMethod]
    public Task ACopyDrawnBackOverItsErasedArea_LeavesAnAlphaTargetAsItWas() => CopyDrawnBack(hasAlpha: true);

    [TestMethod]
    public Task ACopyDrawnBackOverItsErasedArea_LeavesAnOpaqueTargetAsItWas() => CopyDrawnBack(hasAlpha: false);

    [TestMethod]
    public Task DrawingQueuedBeforeACopy_IsDrawnOnce() => CaptureScene.RunAsync(async scene =>
    {
        var window = await scene.ShowAsync(new Border { Background = Color.White });
        var factory = window.GraphicsFactory;
        using var renderScope = factory.AcquireBackgroundRenderScope();
        using var destination = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SIZE, SIZE, 1.0, hasAlpha: true));

        var reference = Draw(factory, hasAlpha: true, DrawTranslucentScene);
        var actual = Draw(factory, hasAlpha: true, context =>
        {
            DrawTranslucentScene(context);
            CopyTarget(context, factory, destination);
        });

        AssertSamePixels(reference, actual, $"{factory.Backend}: the frame after a copy");
    });

    [TestMethod]
    public Task ARoundedClip_StillClipsAfterACopy() => CaptureScene.RunAsync(async scene =>
    {
        var window = await scene.ShowAsync(new Border { Background = Color.White });
        var factory = window.GraphicsFactory;
        using var renderScope = factory.AcquireBackgroundRenderScope();
        using var destination = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SIZE, SIZE, 1.0, hasAlpha: true));

        bool offered = false;
        var actual = Draw(factory, hasAlpha: true, context =>
        {
            context.Save();
            context.SetClipRoundedRect(new Rect(4, 4, 56, 56), 20, 20);
            context.FillRectangle(new Rect(0, 0, SIZE, SIZE / 2), Color.FromRgb(30, 120, 200));
            offered = ((ITargetSnapshotContext)context).TryGetSnapshotTarget(out _, out _, out _);
            if (offered)
            {
                Assert.IsTrue(((ITargetSnapshotContext)context).TryCopyTargetPixels(0, 0, SIZE, SIZE, destination, 0, 0),
                    $"{factory.Backend}: a target offered under a clip was not copied");
            }

            context.FillRectangle(new Rect(0, SIZE / 2, SIZE, SIZE / 2), Color.FromRgb(220, 30, 30));
            context.Restore();
        });

        // Only a backend that holds clipped drawing back from its target until the clip closes cannot offer it.
        if (factory.Backend != "Direct2D")
        {
            Assert.IsTrue(offered, $"{factory.Backend}: the target was not offered under a rounded clip");
        }

        // The corners lie outside the rounded clip, above and below the copy alike.
        Assert.AreEqual(0, actual[3], $"{factory.Backend}: top-left corner (offered: {offered})");
        Assert.AreEqual(0, actual[((SIZE - 1) * SIZE) * 4 + 3], $"{factory.Backend}: bottom-left corner (offered: {offered})");
        Assert.AreEqual(255, actual[((SIZE * 3 / 4) * SIZE + SIZE / 2) * 4 + 3], $"{factory.Backend}: inside the clip after the copy");
    });

    private static Task CopyDrawnBack(bool hasAlpha) => CaptureScene.RunAsync(async scene =>
    {
        var window = await scene.ShowAsync(new Border { Background = Color.White });
        var factory = window.GraphicsFactory;
        using var renderScope = factory.AcquireBackgroundRenderScope();
        using var destination = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SIZE, SIZE, 1.0, hasAlpha));
        using var copy = factory.CreateImageView(destination);

        var reference = Draw(factory, hasAlpha, DrawTranslucentScene);
        var actual = Draw(factory, hasAlpha, context =>
        {
            DrawTranslucentScene(context);
            CopyTarget(context, factory, destination);
            Erase(context, hasAlpha);
            context.DrawImage(copy, _copied, _copied);
        });

        AssertSamePixels(reference, actual, $"{factory.Backend}, {(hasAlpha ? "alpha" : "opaque")} target: the copy drawn back");
    });

    /// <summary>Overlapping opaque and translucent fills, the translucent one partly over nothing.</summary>
    private static void DrawTranslucentScene(IGraphicsContext context)
    {
        context.FillRectangle(new Rect(4, 4, 30, 30), Color.FromRgb(30, 120, 200));
        context.FillRectangle(new Rect(20, 16, 36, 24), Color.FromArgb(128, 220, 30, 30));
        context.FillEllipse(new Rect(10, 28, 20, 20), Color.FromArgb(77, 20, 160, 60));
    }

    private static void CopyTarget(IGraphicsContext context, IGraphicsFactory factory, IRenderSurface destination)
    {
        var snapshot = (ITargetSnapshotContext)context;
        Assert.IsTrue(snapshot.TryGetSnapshotTarget(out int width, out int height, out _), $"{factory.Backend}: the target is not offered");
        Assert.AreEqual(SIZE, width, $"{factory.Backend}: target width");
        Assert.AreEqual(SIZE, height, $"{factory.Backend}: target height");
        Assert.IsTrue(
            snapshot.TryCopyTargetPixels((int)_copied.X, (int)_copied.Y, (int)_copied.Width, (int)_copied.Height,
                destination, (int)_copied.X, (int)_copied.Y),
            $"{factory.Backend}: the target was not copied");
    }

    private static void Erase(IGraphicsContext context, bool hasAlpha)
    {
        if (hasAlpha)
        {
            ((ITransparentDirtyRectContext)context).ClearRectangleToTransparent(_copied);
        }
        else
        {
            ((IOpaqueDirtyRectContext)context).ClearRectangle(_copied, Color.White);
        }
    }

    private static byte[] Draw(IGraphicsFactory factory, bool hasAlpha, Action<IGraphicsContext> draw)
    {
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SIZE, SIZE, 1.0, hasAlpha));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            try
            {
                context.Clear(hasAlpha ? Color.Transparent : Color.White);
                draw(context);
            }
            finally
            {
                context.EndFrame();
            }
        }

        var pixels = new byte[SIZE * SIZE * 4];
        if (!((IRenderDevice)factory).TryReadPixels(surface, pixels, SIZE * 4))
        {
            Assert.Inconclusive($"{factory.Backend} cannot read an offscreen surface back.");
        }

        return pixels;
    }

    private static void AssertSamePixels(byte[] expected, byte[] actual, string label)
    {
        for (int index = 0; index < expected.Length; index++)
        {
            if (Math.Abs(expected[index] - actual[index]) > TOLERANCE)
            {
                int pixel = index / 4;
                Assert.Fail($"{label}: pixel ({pixel % SIZE}, {pixel / SIZE}) channel {index % 4} " +
                    $"expected {expected[index]}, found {actual[index]}");
            }
        }
    }
}
