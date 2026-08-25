using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// <see cref="UIElement.Opacity"/> is a render-time scope, not a layout or input concern: it wraps the
/// element's own drawing and its subtree, skips the drawing entirely at 0, and leaves measurement and
/// hit testing alone.
/// </summary>
[TestClass]
public sealed class OpacityTests
{
    // Re-listing the interface lets the explicit implementations below re-map its slots for this type.
    private sealed class RecordingContext : NoOpGraphicsContext, IGraphicsContext
    {
        private int _depth;

        public int BeginCount { get; private set; }
        public int EndCount { get; private set; }
        public double LastOpacity { get; private set; } = double.NaN;
        public int FillCount { get; private set; }
        public int FillsInsideScope { get; private set; }

        public override double DpiScale => 1;

        // Explicit implementations: the base class members are not virtual, and rendering dispatches
        // through the interface.
        void IGraphicsContext.BeginOpacity(double opacity)
        {
            BeginCount++;
            LastOpacity = opacity;
            _depth++;
        }

        void IGraphicsContext.EndOpacity()
        {
            EndCount++;
            _depth--;
        }

        void IGraphicsContext.FillRectangle(Rect rect, Color color)
        {
            FillCount++;
            if (_depth > 0)
            {
                FillsInsideScope++;
            }
        }
    }

    private sealed class FillingControl : Control
    {
        protected override void OnRender(IGraphicsContext context)
            => context.FillRectangle(Bounds, Color.FromRgb(1, 2, 3));
    }

    private static (FillingControl element, RecordingContext context) Laid(double opacity)
    {
        var element = new FillingControl { Opacity = opacity };
        element.Measure(new Size(100, 100));
        element.Arrange(new Rect(0, 0, 100, 100));
        return (element, new RecordingContext());
    }

    [TestMethod]
    public void DefaultOpacity_IsOpaque()
    {
        Assert.AreEqual(1.0, new Border().Opacity);
    }

    [TestMethod]
    public void FullOpacity_OpensNoScope()
    {
        var (element, context) = Laid(1.0);

        element.Render(context);

        Assert.AreEqual(0, context.BeginCount);
        Assert.AreEqual(1, context.FillCount);
    }

    [TestMethod]
    public void PartialOpacity_WrapsTheDrawing()
    {
        var (element, context) = Laid(0.5);

        element.Render(context);

        Assert.AreEqual(1, context.BeginCount);
        Assert.AreEqual(1, context.EndCount);
        Assert.AreEqual(0.5, context.LastOpacity);
        Assert.AreEqual(1, context.FillsInsideScope, "the element's own drawing belongs inside the scope");
    }

    [TestMethod]
    public void ZeroOpacity_SkipsTheDrawingEntirely()
    {
        var (element, context) = Laid(0);

        element.Render(context);

        Assert.AreEqual(0, context.FillCount);
        Assert.AreEqual(0, context.BeginCount, "nothing is drawn, so no scope is needed either");
    }

    [TestMethod]
    public void Opacity_DoesNotAffectLayout()
    {
        var opaque = new FillingControl { Width = 40, Height = 20 };
        var faded = new FillingControl { Width = 40, Height = 20, Opacity = 0 };

        opaque.Measure(new Size(100, 100));
        faded.Measure(new Size(100, 100));

        Assert.AreEqual(opaque.DesiredSize, faded.DesiredSize);
    }

    [TestMethod]
    public void TransparentElement_StillTakesThePointer()
    {
        var element = new FillingControl { Opacity = 0 };
        element.Measure(new Size(100, 100));
        element.Arrange(new Rect(0, 0, 100, 100));

        Assert.AreSame(element, element.HitTest(new Point(50, 50)));
    }

    [TestMethod]
    public void Binding_DrivesOpacityFromEffectiveEnabled()
    {
        var icon = new Border();
        icon.Bind(UIElement.OpacityProperty, icon, UIElement.IsEffectivelyEnabledProperty,
            (bool enabled) => enabled ? 1.0 : 0.5);
        var root = new StackPanel();
        root.Add(icon);

        Assert.AreEqual(1.0, icon.Opacity);

        root.IsEnabled = false;
        Assert.AreEqual(0.5, icon.Opacity);

        root.IsEnabled = true;
        Assert.AreEqual(1.0, icon.Opacity);
    }
}
