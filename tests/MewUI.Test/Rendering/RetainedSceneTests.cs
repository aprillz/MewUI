using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates the retained scene model against the immediate render path: a captured scene must
/// paint the same pixels as a direct render and as a pure replay, and a capture told which visuals
/// changed must re-record only those visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedSceneTests
{
    private const int SURFACE_WIDTH = 320;
    private const int SURFACE_HEIGHT = 300;

    /// <summary>Leaf visual that is not a declared-composition type, so it is recorded as a compatibility subtree.</summary>
    private sealed class FillBox : Control
    {
        internal Color Fill { get; set; } = Color.FromArgb(255, 200, 60, 60);

        protected override Size MeasureContent(Size availableSize) => new(40, 40);

        protected override void OnRender(IGraphicsContext context)
        {
            // Deliberately overflows Bounds so an enclosing clip changes the result.
            var overflow = new Rect(Bounds.X - 12, Bounds.Y - 12, Bounds.Width + 24, Bounds.Height + 24);
            context.FillRectangle(overflow, Fill);
            context.FillEllipse(Bounds, Color.FromArgb(255, 20, 20, 90));
        }
    }

    /// <summary>Leaf visual that issues every recordable overload, so a replay has to restore each of them.</summary>
    private sealed class OverloadBox : Control
    {
        internal IImage? Image { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(220, 200);

        protected override void OnRender(IGraphicsContext context)
        {
            double left = Bounds.X;
            double top = Bounds.Y;
            var pen = new Pen(Color.FromArgb(255, 10, 40, 90), 2);
            var brush = new SolidColorBrush(Color.FromArgb(255, 90, 40, 10));

            var mutablePath = new PathGeometry();
            mutablePath.MoveTo(left + 4, top + 4);
            mutablePath.LineTo(left + 44, top + 12);
            mutablePath.BezierTo(left + 52, top + 30, left + 22, top + 46, left + 6, top + 32);
            mutablePath.Close();

            var frozenPath = new PathGeometry { FillRule = FillRule.EvenOdd };
            frozenPath.MoveTo(left + 60, top + 4);
            frozenPath.LineTo(left + 104, top + 4);
            frozenPath.LineTo(left + 82, top + 44);
            frozenPath.Close();
            frozenPath.Freeze();

            context.GlobalAlpha = 0.9f;
            context.TextPixelSnap = true;
            context.EnableAlphaTextHint = true;
            context.ImageScaleQuality = ImageScaleQuality.HighQuality;

            context.DrawBoxShadow(new Rect(left + 8, top + 56, 60, 24), 4, 6, Color.FromArgb(120, 0, 0, 0), 2, 3);
            context.DrawLine(new Point(left, top + 52), new Point(left + 56, top + 52), Color.FromArgb(255, 200, 0, 0), 3);
            context.DrawLine(new Point(left, top + 55), new Point(left + 56, top + 55), Color.FromArgb(255, 0, 160, 0), 3, true);
            context.DrawLine(new Point(left, top + 58), new Point(left + 56, top + 58), pen);

            context.DrawRectangle(new Rect(left + 4, top + 62, 40, 20), Color.FromArgb(255, 0, 0, 200), 2);
            context.DrawRectangle(new Rect(left + 48, top + 62, 40, 20), Color.FromArgb(255, 0, 120, 200), 2, true);
            context.DrawRectangle(new Rect(left + 92, top + 62, 40, 20), pen);
            context.FillRectangle(new Rect(left + 136, top + 62, 40, 20), Color.FromArgb(255, 220, 220, 60));
            context.FillRectangle(new Rect(left + 180, top + 62, 30, 20), brush);

            context.DrawRoundedRectangle(new Rect(left + 4, top + 86, 40, 20), 6, 5, Color.FromArgb(255, 200, 0, 120), 2);
            context.DrawRoundedRectangle(new Rect(left + 48, top + 86, 40, 20), 6, 5, Color.FromArgb(255, 120, 0, 200), 2, true);
            context.DrawRoundedRectangle(new Rect(left + 92, top + 86, 40, 20), 6, 5, pen);
            context.FillRoundedRectangle(new Rect(left + 136, top + 86, 40, 20), 6, 5, Color.FromArgb(255, 60, 220, 220));
            context.FillRoundedRectangle(new Rect(left + 180, top + 86, 30, 20), 6, 5, brush);

            context.DrawEllipse(new Rect(left + 4, top + 110, 40, 20), Color.FromArgb(255, 200, 100, 0), 2);
            context.DrawEllipse(new Rect(left + 48, top + 110, 40, 20), Color.FromArgb(255, 0, 100, 200), 2, true);
            context.DrawEllipse(new Rect(left + 92, top + 110, 40, 20), pen);
            context.FillEllipse(new Rect(left + 136, top + 110, 40, 20), Color.FromArgb(255, 120, 220, 120));
            context.FillEllipse(new Rect(left + 180, top + 110, 30, 20), brush);

            context.Save();
            context.Translate(left + 6, top + 128);
            context.Scale(0.5, 0.5);
            context.Rotate(0.2);
            context.DrawPath(mutablePath, Color.FromArgb(255, 30, 30, 30), 2);
            context.DrawPath(frozenPath, pen);
            context.FillPath(mutablePath, Color.FromArgb(255, 200, 180, 60));
            context.FillPath(frozenPath, Color.FromArgb(255, 60, 180, 200), FillRule.EvenOdd);
            context.FillPath(mutablePath, brush);
            context.FillPath(frozenPath, brush, FillRule.NonZero);
            context.Restore();

            context.Save();
            context.SetTransform(new Matrix3x2(1, 0, 0, 1, (float)left + 120, (float)top + 130));
            context.SetClipRoundedRect(new Rect(0, 0, 60, 40), 8, 8);
            context.FillRectangle(new Rect(0, 0, 60, 40), Color.FromArgb(255, 240, 120, 200));
            context.ResetClip();
            context.SetClipRoundedRect(new Rect(0, 42, 60, 24), 6, 6, 2);
            context.FillRectangle(new Rect(0, 42, 60, 24), Color.FromArgb(255, 120, 240, 200));
            context.ResetClip();
            context.ResetTransform();
            context.Restore();

            context.Save();
            context.SetClipPath(frozenPath);
            context.FillRectangle(Bounds, Color.FromArgb(255, 250, 200, 250));
            context.ResetClip();
            context.Restore();

            context.Save();
            context.SetClip(new Rect(left + 140, top + 132, 70, 60));
            context.IntersectClip(new Rect(left + 150, top + 136, 60, 50));
            context.BeginOpaqueBackdrop();
            context.BeginOpacity(0.6);
            if (Image != null)
            {
                context.DrawImage(Image, new Point(left + 150, top + 136));
                context.DrawImage(Image, new Rect(left + 150, top + 158, 30, 20));
                context.DrawImage(Image, new Rect(left + 182, top + 158, 26, 20), new Rect(2, 2, 20, 16));
            }

            context.EndOpacity();
            context.EndOpaqueBackdrop();
            context.ResetClip();
            context.Restore();

            context.ImageScaleQuality = ImageScaleQuality.Default;
            context.EnableAlphaTextHint = false;
            context.TextPixelSnap = false;
            context.GlobalAlpha = 1.0f;
        }
    }

    [TestMethod]
    public void RetainedScene_EveryRecordedOverload_ReplaysAsItWasCalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        using var imageSource = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(24, 18, 1.0, hasAlpha: false));
        using (var imageContext = factory.CreateContext(imageSource))
        {
            imageContext.BeginFrame(imageSource);
            imageContext.Clear(Color.FromArgb(255, 20, 140, 220));
            imageContext.EndFrame();
        }

        using var image = factory.CreateImageView(imageSource);
        var painter = new OverloadBox { Image = image };
        var root = new Border
        {
            Padding = new Thickness(8),
            Background = Color.FromArgb(255, 250, 250, 250),
            Child = new StackPanel { Orientation = Orientation.Vertical }
                .Children(painter, new TextBlock { Text = "Replay parity", FontSize = 14 }),
        };
        Layout(root);

        byte[] immediate = RenderSurface(factory, context => root.Render(context));

        using var scene = new RenderScene();
        byte[] captured = RenderSurface(factory, context => new SceneCapture().Capture(scene, root, context));
        byte[] replayed = RenderSurface(factory, context => FrameRenderer.Replay(scene, context));

        Assert.AreEqual(0, scene.Statistics.RejectedSlotCount, scene.FindNode(painter)?.NonRecordableReason);
        Assert.AreEqual(0, scene.Statistics.LiveFallbackCount, "the replay fell back to the live element");
        AssertPixelsEqual(immediate, captured, "capture pass");
        AssertPixelsEqual(immediate, replayed, "replay pass");
    }

    [TestMethod]
    public void RetainedScene_CaptureAndReplay_MatchImmediateRenderPixelForPixel()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildTree();
        Layout(root);

        byte[] immediate = RenderSurface(factory, context => root.Render(context));

        using var scene = new RenderScene();
        byte[] captured = RenderSurface(factory, context => new SceneCapture().Capture(scene, root, context));
        byte[] replayed = RenderSurface(factory, context => FrameRenderer.Replay(scene, context));

        Console.WriteLine(
            $"nodes={scene.NodeCount} recorded={scene.Statistics.ContentRecordCount} " +
            $"replayed={scene.Statistics.ContentReplayCount} rejected={scene.Statistics.RejectedSlotCount} " +
            $"liveFallback={scene.Statistics.LiveFallbackCount}");

        AssertPixelsEqual(immediate, captured, "capture pass");
        AssertPixelsEqual(immediate, replayed, "replay pass");
    }

    [TestMethod]
    public void RetainedScene_DirtyParent_DoesNotReRecordChildren()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildTree();
        Layout(root);

        var capture = new SceneCapture();
        using var scene = new RenderScene();
        RenderSurface(factory, context => capture.Capture(scene, root, context));

        Assert.AreEqual(0, scene.Statistics.RejectedSlotCount,
            "A rejected slot is re-recorded on every pass, which would mask the dirty-set behaviour.");
        int firstPassRecords = scene.Statistics.ContentRecordCount;
        Assert.IsGreaterThan(0, firstPassRecords);

        scene.Statistics.Reset();

        var dirty = new HashSet<UIElement>(ReferenceEqualityComparer.Instance) { root };
        RenderSurface(factory, context => capture.Capture(scene, root, context, dirty));

        // The root Border owns two content slots: the background and the border stroke.
        Assert.AreEqual(2, scene.Statistics.ContentRecordCount,
            $"Only the dirty root's own content slots may be recorded; recorded {scene.Statistics.ContentRecordCount}.");
        Assert.AreEqual(firstPassRecords - 2, scene.Statistics.ContentReplayCount,
            "Every slot that was not re-recorded must be accounted for as a replay.");
        Assert.AreEqual(0, scene.Statistics.RejectedSlotCount);
    }

    [TestMethod]
    public void RetainedScene_UnchangedFrame_RecordsNothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildTree();
        Layout(root);

        var capture = new SceneCapture();
        using var scene = new RenderScene();
        RenderSurface(factory, context => capture.Capture(scene, root, context));

        int firstPassRecords = scene.Statistics.ContentRecordCount;
        scene.Statistics.Reset();

        var dirty = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        RenderSurface(factory, context => capture.Capture(scene, root, context, dirty));

        Assert.AreEqual(0, scene.Statistics.ContentRecordCount,
            $"An unchanged frame must record nothing; recorded {scene.Statistics.ContentRecordCount}.");
        Assert.AreEqual(firstPassRecords, scene.Statistics.ContentReplayCount,
            "An unchanged frame must replay every slot the first pass recorded.");
    }

    /// <summary>
    /// Builds a tree of declared-composition types (Border, StackPanel, Grid, ScrollViewer,
    /// RotationDecorator) covering clipped and unclipped borders, square and rounded corners, a
    /// scrolled viewport and a sub-unit opacity, with compatibility-subtree leaves.
    /// </summary>
    private static Border BuildTree()
    {
        var clipped = new Border
        {
            Width = 120,
            Height = 60,
            ClipToBounds = true,
            CornerRadius = 12,
            BorderThickness = 3,
            BorderBrush = Color.FromArgb(255, 10, 90, 10),
            Background = Color.FromArgb(255, 180, 220, 180),
            Child = new FillBox { Fill = Color.FromArgb(255, 220, 90, 40) },
        };

        var unclipped = new Border
        {
            Width = 120,
            Height = 60,
            ClipToBounds = false,
            CornerRadius = 0,
            BorderThickness = 2,
            BorderBrush = Color.FromArgb(255, 90, 10, 10),
            Background = Color.FromArgb(255, 220, 200, 160),
            Child = new FillBox { Fill = Color.FromArgb(255, 40, 120, 200) },
        };

        var grid = new Grid { Height = 70 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        var gridLeft = new Border
        {
            CornerRadius = 6,
            Background = Color.FromArgb(255, 150, 150, 240),
            Child = new FillBox { Fill = Color.FromArgb(255, 60, 60, 160) },
        };
        var gridRight = new Border
        {
            ClipToBounds = true,
            Background = Color.FromArgb(255, 240, 150, 150),
            Opacity = 0.45,
            Child = new FillBox { Fill = Color.FromArgb(255, 160, 60, 60) },
        };
        Grid.SetColumn(gridLeft, 0);
        Grid.SetColumn(gridRight, 1);
        grid.Children(gridLeft, gridRight);

        var scrolledContent = new StackPanel { Orientation = Orientation.Vertical };
        var scrolledChildren = new Element[5];
        for (int index = 0; index < scrolledChildren.Length; index++)
        {
            scrolledChildren[index] = new Border
            {
                Height = 40,
                CornerRadius = index % 2 == 0 ? 8 : 0,
                Background = Color.FromArgb(255, (byte)(60 + index * 30), 200, 120),
                Child = new FillBox { Fill = Color.FromArgb(255, 30, 30, (byte)(80 + index * 25)) },
            };
        }

        scrolledContent.Children(scrolledChildren);

        var scrollViewer = new ScrollViewer
        {
            Width = 180,
            Height = 90,
            Background = Color.FromArgb(255, 245, 245, 245),
            Content = scrolledContent,
        };

        var rotated = new RotationDecorator
        {
            Rotation = Rotation.Clockwise90,
            Child = new Border
            {
                Width = 90,
                Height = 40,
                CornerRadius = 10,
                BorderThickness = 2,
                BorderBrush = Color.FromArgb(255, 0, 0, 0),
                Background = Color.FromArgb(255, 250, 230, 120),
                Child = new FillBox { Fill = Color.FromArgb(255, 120, 200, 250) },
            },
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            ClipToBounds = true,
            Opacity = 0.8,
        };
        stack.Children(clipped, unclipped, grid, scrollViewer, rotated);

        return new Border
        {
            BorderThickness = 4,
            BorderBrush = Color.FromArgb(255, 30, 30, 30),
            Background = Color.FromArgb(255, 250, 250, 250),
            Padding = new Thickness(6),
            Child = stack,
        };
    }

    private static void Layout(UIElement root)
    {
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));
    }

    private static byte[] RenderSurface(GdiGraphicsFactory factory, Action<IGraphicsContext> draw)
    {
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            draw(context);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        var copy = new byte[SURFACE_WIDTH * SURFACE_HEIGHT * 4];
        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            pixels.Slice(row * stride, SURFACE_WIDTH * 4).CopyTo(copy.AsSpan(row * SURFACE_WIDTH * 4));
        }

        return copy;
    }

    private static void AssertPixelsEqual(byte[] expected, byte[] actual, string label)
    {
        int differing = 0;
        int firstX = -1;
        int firstY = -1;
        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            for (int column = 0; column < SURFACE_WIDTH; column++)
            {
                int offset = (row * SURFACE_WIDTH + column) * 4;
                if (expected[offset] == actual[offset] &&
                    expected[offset + 1] == actual[offset + 1] &&
                    expected[offset + 2] == actual[offset + 2] &&
                    expected[offset + 3] == actual[offset + 3])
                {
                    continue;
                }

                differing++;
                if (firstX < 0)
                {
                    firstX = column;
                    firstY = row;
                }
            }
        }

        if (differing == 0)
        {
            return;
        }

        int firstOffset = (firstY * SURFACE_WIDTH + firstX) * 4;
        Assert.Fail(
            $"{label} differs from the immediate render at {differing} pixels. " +
            $"First at ({firstX},{firstY}): expected BGRA=" +
            $"({expected[firstOffset]},{expected[firstOffset + 1]},{expected[firstOffset + 2]},{expected[firstOffset + 3]}) " +
            $"actual BGRA=" +
            $"({actual[firstOffset]},{actual[firstOffset + 1]},{actual[firstOffset + 2]},{actual[firstOffset + 3]}).");
    }
}
