using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A transform decorator lays out its child untransformed (Render) or by the transformed bounding box (Layout),
/// hit tests through the inverse transform, and records the transform as a scope so the child stays its own node.
/// Not parallelizable: the retained tests swap the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TransformDecoratorTests
{
    private const double TOLERANCE = 1e-3;
    private const int SURFACE_SIZE = 200;

    [TestMethod]
    public void RenderMode_MeasuresAndArrangesTheChildUntransformed()
    {
        var child = new Border { Width = 40, Height = 20 };
        var decorator = new TransformDecorator { Child = child, Transform = Matrix3x2.CreateRotation(MathF.PI / 2) };

        decorator.Measure(Size.Infinity);
        decorator.Arrange(new Rect(0, 0, 40, 20));

        Assert.AreEqual(new Size(40, 20), decorator.DesiredSize);
        Assert.AreEqual(new Rect(0, 0, 40, 20), child.Bounds);
    }

    [TestMethod]
    public void LayoutMode_QuarterTurn_DesiresTheSwappedSizeAndCentersTheChild()
    {
        var child = new Border { Width = 40, Height = 20 };
        var decorator = new TransformDecorator
        {
            Child = child,
            Mode = TransformMode.Layout,
            Transform = Matrix3x2.CreateRotation(MathF.PI / 2),
        };

        decorator.Measure(Size.Infinity);
        decorator.Arrange(new Rect(0, 0, 20, 40));

        Assert.AreEqual(20, decorator.DesiredSize.Width, TOLERANCE);
        Assert.AreEqual(40, decorator.DesiredSize.Height, TOLERANCE);
        Assert.AreEqual(new Rect(-10, 10, 40, 20), child.Bounds);
    }

    [TestMethod]
    public void LayoutMode_IgnoresTranslation()
    {
        var child = new Border { Width = 40, Height = 20 };
        var decorator = new TransformDecorator
        {
            Child = child,
            Mode = TransformMode.Layout,
            Transform = Matrix3x2.CreateScale(2) * Matrix3x2.CreateTranslation(100, 100),
        };

        decorator.Measure(Size.Infinity);
        decorator.Arrange(new Rect(0, 0, 80, 40));

        Assert.AreEqual(new Size(80, 40), decorator.DesiredSize);
        var origin = child.TransformToAncestor(decorator).Transform(new Point(0, 0));
        Assert.AreEqual(0, origin.X, TOLERANCE, "scaled about the center, the child's corner lands on the decorator's");
        Assert.AreEqual(0, origin.Y, TOLERANCE);
    }

    [TestMethod]
    [DataRow(90.0, 100.0, 50.0, 50.0, 100.0)]
    [DataRow(0.0, 100.0, 50.0, 100.0, 50.0)]
    public void LayoutMode_MeasuresTheChildAtTheLargestSizeThatFitsWhenTransformed(
        double degrees, double availableWidth, double availableHeight, double expectedChildWidth, double expectedChildHeight)
    {
        var child = new FillingElement();
        var decorator = new TransformDecorator
        {
            Child = child,
            Mode = TransformMode.Layout,
            Transform = Matrix3x2.CreateRotation((float)(degrees * Math.PI / 180)),
        };

        decorator.Measure(new Size(availableWidth, availableHeight));

        Assert.AreEqual(expectedChildWidth, child.LastAvailable.Width, TOLERANCE);
        Assert.AreEqual(expectedChildHeight, child.LastAvailable.Height, TOLERANCE);
        Assert.AreEqual(availableWidth, decorator.DesiredSize.Width, TOLERANCE);
        Assert.AreEqual(availableHeight, decorator.DesiredSize.Height, TOLERANCE);
    }

    [TestMethod]
    public void LayoutMode_ScaleOnOneAxis_SplitsTheConstraint()
    {
        var child = new FillingElement();
        var decorator = new TransformDecorator
        {
            Child = child,
            Mode = TransformMode.Layout,
            Transform = Matrix3x2.CreateScale(2, 1),
        };

        decorator.Measure(new Size(100, 50));

        Assert.AreEqual(new Size(50, 50), child.LastAvailable);
        Assert.AreEqual(new Size(100, 50), decorator.DesiredSize);
    }

    [TestMethod]
    public void HitTest_Rotation_FindsTheChildWhereItIsDrawn()
    {
        var strip = new Border { Width = 40, Height = 10, VerticalAlignment = VerticalAlignment.Top };
        var decorator = Arranged(new TransformDecorator { Child = strip, Transform = Matrix3x2.CreateRotation(MathF.PI / 2) }, 40, 40);

        Assert.AreSame(strip, decorator.HitTest(new Point(35, 20)), "the top strip turns onto the right edge");
        Assert.AreSame(decorator, decorator.HitTest(new Point(20, 5)), "where the strip was laid out nothing is drawn now");
    }

    [TestMethod]
    public void HitTest_Scale_FindsTheChildWhereItIsDrawn()
    {
        var child = new Border();
        var decorator = Arranged(new TransformDecorator
        {
            Child = child,
            Transform = Matrix3x2.CreateScale(0.5f),
            TransformOrigin = new Point(0, 0),
        }, 40, 40);

        Assert.AreSame(child, decorator.HitTest(new Point(10, 10)));
        Assert.AreSame(decorator, decorator.HitTest(new Point(30, 30)));
    }

    [TestMethod]
    public void HitTest_ScaleUp_FindsTheChildWhereItIsDrawnOutsideTheDecorator()
    {
        var child = new Border();
        var decorator = Arranged(new TransformDecorator
        {
            Child = child,
            Transform = Matrix3x2.CreateScale(2f),
            TransformOrigin = new Point(0, 0),
        }, 40, 40);

        Assert.AreSame(child, decorator.HitTest(new Point(60, 60)), "the child is drawn up to 80 and stays clickable there");
        Assert.IsNull(decorator.HitTest(new Point(100, 100)), "past what the child draws and outside the decorator");
    }

    [TestMethod]
    public void HitTest_Flip_FindsTheChildWhereItIsDrawn()
    {
        var leftHalf = new Border { Width = 20, HorizontalAlignment = HorizontalAlignment.Left };
        var decorator = Arranged(new TransformDecorator { Child = leftHalf, Transform = Matrix3x2.CreateScale(-1, 1) }, 40, 40);

        Assert.AreSame(leftHalf, decorator.HitTest(new Point(30, 10)));
        Assert.AreSame(decorator, decorator.HitTest(new Point(10, 10)));
    }

    [TestMethod]
    public void HitTest_ZeroScale_HitsOnlyTheDecorator()
    {
        var child = new Border();
        var decorator = Arranged(new TransformDecorator { Child = child, Transform = Matrix3x2.CreateScale(0) }, 40, 40);

        Assert.AreSame(decorator, decorator.HitTest(new Point(20, 20)));
    }

    [TestMethod]
    public void Composition_RecordsTheTransformAsAScopeAroundTheChild()
    {
        var child = new Border();
        var decorator = Arranged(new TransformDecorator { Child = child, Transform = Matrix3x2.CreateRotation(0.5f) }, 40, 40);
        var expected = Matrix3x2.CreateTranslation(-20, -20) * Matrix3x2.CreateRotation(0.5f) * Matrix3x2.CreateTranslation(20, 20);

        var plan = Plan(decorator);

        Assert.HasCount(3, plan.Entries);
        Assert.AreEqual(CompositionEntryKind.Content, plan.Entries[0].Kind);
        Assert.AreEqual(CompositionEntryKind.Transform, plan.Entries[1].Kind);
        Assert.AreEqual(1, plan.Entries[1].ScopeLength);
        Assert.AreEqual(expected, plan.Entries[1].Matrix);
        Assert.IsTrue(plan.Entries[1].DisablesTextPixelSnap, "a rotation puts glyphs between pixels");
        Assert.AreSame(child, plan.Entries[2].Child);
    }

    [TestMethod]
    public void Composition_WholeNumberScaleKeepsTextSnapping()
    {
        var decorator = Arranged(new TransformDecorator { Child = new Border(), Transform = Matrix3x2.CreateScale(2) }, 40, 40);

        Assert.IsFalse(Plan(decorator).Entries[1].DisablesTextPixelSnap);
    }

    [TestMethod]
    public void Composition_IdentityHasNoScope()
    {
        var decorator = Arranged(new TransformDecorator { Child = new Border() }, 40, 40);

        var plan = Plan(decorator);

        Assert.HasCount(2, plan.Entries);
        Assert.AreEqual(CompositionEntryKind.Child, plan.Entries[1].Kind);
    }

    [TestMethod]
    public void RetainedScene_MatchesTheImmediateRender()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        using var factory = new GdiGraphicsFactory();
        using var factoryScope = new DefaultGraphicsFactoryScope(factory);
        var (root, _) = BuildScene();

        byte[] immediate = RenderSurface(factory, context => root.Render(context));
        using var scene = new RenderScene();
        byte[] captured = RenderSurface(factory, context => new SceneCapture().Capture(scene, root, context));
        byte[] replayed = RenderSurface(factory, context => FrameRenderer.Replay(scene, context));

        CollectionAssert.AreEqual(immediate, captured, "capture pass");
        CollectionAssert.AreEqual(immediate, replayed, "replay pass");
    }

    [TestMethod]
    public void RetainedScene_ChangedChild_RecordsOnlyTheChild()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        using var factory = new GdiGraphicsFactory();
        using var factoryScope = new DefaultGraphicsFactoryScope(factory);
        var (root, leaf) = BuildScene();

        var capture = new SceneCapture();
        using var scene = new RenderScene();
        RenderSurface(factory, context => capture.Capture(scene, root, context));
        Assert.AreEqual(0, scene.Statistics.RejectedSlotCount);
        Assert.IsFalse(scene.FindNode(root)!.Plan.IsUnsupported, "the decorator must declare its composition");
        int firstPassRecords = scene.Statistics.ContentRecordCount;
        scene.Statistics.Reset();

        var dirty = new HashSet<UIElement>(ReferenceEqualityComparer.Instance) { leaf };
        RenderSurface(factory, context => capture.Capture(scene, root, context, dirty));

        Assert.AreEqual(1, scene.Statistics.ContentRecordCount, "only the leaf's own slot is recorded again");
        Assert.AreEqual(firstPassRecords - 1, scene.Statistics.ContentReplayCount);
    }

    private static (TransformDecorator Root, PaintedElement Leaf) BuildScene()
    {
        var leaf = new PaintedElement();
        var root = new TransformDecorator
        {
            Child = leaf,
            Transform = Matrix3x2.CreateRotation(0.4f) * Matrix3x2.CreateScale(1.5f),
        };
        root.Measure(new Size(SURFACE_SIZE, SURFACE_SIZE));
        root.Arrange(new Rect(0, 0, SURFACE_SIZE, SURFACE_SIZE));
        return (root, leaf);
    }

    private static T Arranged<T>(T element, double width, double height) where T : UIElement
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        return element;
    }

    private static CompositionPlan Plan(UIElement element)
    {
        var builder = new CompositionPlanBuilder();
        element.WriteComposition(builder);
        return builder.Build();
    }

    private static byte[] RenderSurface(GdiGraphicsFactory factory, Action<IGraphicsContext> draw)
    {
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_SIZE, SURFACE_SIZE, 1.0, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            draw(context);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var copy = new byte[SURFACE_SIZE * SURFACE_SIZE * 4];
        for (int row = 0; row < SURFACE_SIZE; row++)
        {
            pixels.Slice(row * cpu.StrideBytes, SURFACE_SIZE * 4).CopyTo(copy.AsSpan(row * SURFACE_SIZE * 4));
        }

        return copy;
    }

    /// <summary>A leaf with one content slot that draws inside its bounds.</summary>
    private sealed class PaintedElement : FrameworkElement
    {
        protected override Size MeasureContent(Size availableSize) => new(80, 40);

        protected override void OnRender(IGraphicsContext context)
        {
            context.FillRectangle(Bounds, Color.FromArgb(255, 200, 60, 60));
            context.FillEllipse(Bounds.Deflate(new Thickness(6)), Color.FromArgb(255, 20, 20, 90));
        }
    }

    /// <summary>Desires whatever it is offered, so the size the decorator offers is observable.</summary>
    private sealed class FillingElement : FrameworkElement
    {
        public Size LastAvailable { get; private set; }

        protected override Size MeasureContent(Size availableSize)
        {
            LastAvailable = availableSize;
            return availableSize;
        }
    }
}
