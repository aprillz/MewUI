using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// The viewport cull judges an element where it is drawn: a child laid out off the viewport but rotated onto it
/// by its parent is drawn, and one that is off the viewport either way is not.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TransformedCullTests
{
    private const double VIEWPORT = 200;

    [TestMethod]
    public void ChildRotatedOntoTheViewport_IsDrawn()
    {
        // The decorator stands at the left edge; its child is laid out unrotated, so its left end falls below x = 0.
        var (host, icon) = RotatedStrip(top: 50);
        Assert.IsLessThan(0, icon.Bounds.Right, "the icon's layout box has to lie off the viewport for this test to mean anything");

        Render(host);

        Assert.AreEqual(1, icon.RenderCount, "rotated, the icon lies inside the viewport");
    }

    [TestMethod]
    public void ChildOffTheViewportEvenWhenRotated_IsCulled()
    {
        // Low enough that the rotated icon lands below the viewport too.
        var (host, icon) = RotatedStrip(top: 170);

        Render(host);

        Assert.AreEqual(0, icon.RenderCount);
    }

    [TestMethod]
    public void UntransformedChildOffTheViewport_IsCulled()
    {
        var icon = new CountingElement(16, 16);
        var host = new Canvas();
        host.Add(icon);
        Canvas.SetLeft(icon, -40);
        Canvas.SetTop(icon, 50);
        host.Measure(new Size(VIEWPORT, VIEWPORT));
        host.Arrange(new Rect(0, 0, VIEWPORT, VIEWPORT));

        Render(host);

        Assert.AreEqual(0, icon.RenderCount);
    }

    /// <summary>A left-edge strip: a counter-clockwise <see cref="RotationDecorator"/> holding [icon, filler] laid out horizontally.</summary>
    private static (Canvas Host, CountingElement Icon) RotatedStrip(double top)
    {
        var icon = new CountingElement(16, 16);
        var row = new StackPanel().Horizontal().Children(icon, new CountingElement(84, 16));
        var decorator = new RotationDecorator { Rotation = Rotation.CounterClockwise90, Child = row };
        var host = new Canvas();
        host.Add(decorator);
        Canvas.SetLeft(decorator, 0);
        Canvas.SetTop(decorator, top);
        host.Measure(new Size(VIEWPORT, VIEWPORT));
        host.Arrange(new Rect(0, 0, VIEWPORT, VIEWPORT));
        return (host, icon);
    }

    private static void Render(UIElement host)
    {
        var previous = UIElement.RenderCullViewport;
        UIElement.RenderCullViewport = new Rect(0, 0, VIEWPORT, VIEWPORT);
        try
        {
            host.Render(new TransformContext());
        }
        finally
        {
            UIElement.RenderCullViewport = previous;
        }
    }

    /// <summary>A drawing sink that keeps the transform, each call pre-multiplying it like the backends do.</summary>
    private sealed class TransformContext : NoOpGraphicsContext
    {
        private readonly Stack<Matrix3x2> _saved = new();
        private Matrix3x2 _transform = Matrix3x2.Identity;

        public override void Save() => _saved.Push(_transform);

        public override void Restore() => _transform = _saved.Count > 0 ? _saved.Pop() : Matrix3x2.Identity;

        public override void Translate(double dx, double dy) => _transform = Matrix3x2.CreateTranslation((float)dx, (float)dy) * _transform;

        public override void Rotate(double angleRadians) => _transform = Matrix3x2.CreateRotation((float)angleRadians) * _transform;

        public override void Scale(double sx, double sy) => _transform = Matrix3x2.CreateScale((float)sx, (float)sy) * _transform;

        public override void SetTransform(Matrix3x2 matrix) => _transform = matrix;

        public override Matrix3x2 GetTransform() => _transform;

        public override void ResetTransform() => _transform = Matrix3x2.Identity;
    }

    private sealed class CountingElement(double width, double height) : FrameworkElement
    {
        public int RenderCount { get; private set; }

        protected override Size MeasureContent(Size availableSize) => new(width, height);

        protected override void OnRender(IGraphicsContext context) => RenderCount++;
    }
}
