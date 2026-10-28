using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Elements;

/// <summary>
/// Coordinate conversion follows the transforms decorators apply to their children: points, mouse positions, input
/// handled by controls and popup anchors all land where the element is drawn. Trees without a transform convert
/// exactly as before.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TransformedCoordinateTests
{
    private const double TOLERANCE = 1e-3;

    [TestMethod]
    public void TranslatePoint_Rotation_MapsToWhereThePointIsDrawn()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, decorator, child) = MakeRotated();

        // A quarter turn about the center of the 80x40 box carries its top-left corner, 40 left of and 20 above
        // the center, to 20 right of and 40 above it.
        var corner = RotatedCorner(decorator);
        AssertPoint(corner, child.TranslatePoint(new Point(0, 0), window));
        AssertPoint(new Point(0, 0), window.TranslatePoint(corner, child));
    }

    [TestMethod]
    public void TransformToVisual_RoundTripsThroughTheInverse()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, decorator, child) = MakeRotated();
        var outside = new Border { Width = 10, Height = 10 };
        ((Canvas)window.Content!).Add(outside);
        Canvas.SetLeft(outside, 300);
        Canvas.SetTop(outside, 200);
        window.PerformLayout();

        var point = new Point(13.5, 7.25);
        var there = child.TransformToVisual(outside).Transform(point);
        AssertPoint(point, outside.TransformToVisual(child).Transform(there));
        AssertPoint(point, child.TransformToVisual(outside).Inverse.Transform(there));
        AssertPoint(point, decorator.TransformToDescendant(child).Transform(child.TransformToAncestor(decorator).Transform(point)));
    }

    [TestMethod]
    public void TranslatePoint_NestedScaleAndRotation_Compose()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var child = new Border { Width = 40, Height = 20 };
        var inner = new TransformDecorator { Child = child, Transform = Matrix3x2.CreateRotation(MathF.PI / 2), TransformOrigin = new Point(0, 0) };
        var outer = new TransformDecorator { Child = inner, Transform = Matrix3x2.CreateScale(2), TransformOrigin = new Point(0, 0) };
        var window = HeadlessWindow.Create();
        var canvas = new Canvas();
        canvas.Add(outer);
        Canvas.SetLeft(outer, 100);
        Canvas.SetTop(outer, 100);
        window.Content = canvas;
        window.PerformLayout();

        // Rotated about the top-left corner, local (10,0) lands 10 below it; scaled 2x about the same corner, 20 below.
        AssertPoint(new Point(outer.Bounds.X, outer.Bounds.Y + 20), child.TranslatePoint(new Point(10, 0), window));
    }

    [TestMethod]
    public void TranslateRect_ReturnsTheBoundingBoxWhereTheRectIsDrawn()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, decorator, child) = MakeRotated();
        var center = decorator.Bounds.Center;

        var drawn = child.TranslateRect(new Rect(0, 0, 80, 40), window);

        Assert.AreEqual(center.X - 20, drawn.X, TOLERANCE);
        Assert.AreEqual(center.Y - 40, drawn.Y, TOLERANCE);
        Assert.AreEqual(40, drawn.Width, TOLERANCE);
        Assert.AreEqual(80, drawn.Height, TOLERANCE);
        var inRoot = child.GetBoundsInRoot();
        Assert.AreEqual(drawn.X, inRoot.X, TOLERANCE);
        Assert.AreEqual(drawn.Y, inRoot.Y, TOLERANCE);
        Assert.AreEqual(drawn.Width, inRoot.Width, TOLERANCE);
        Assert.AreEqual(drawn.Height, inRoot.Height, TOLERANCE);
    }

    [TestMethod]
    public void ZeroScale_TransformIntoTheChildMapsNoPoint()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, decorator, child) = MakeRotated();
        decorator.Transform = Matrix3x2.CreateScale(0);
        window.PerformLayout();
        var center = decorator.Bounds.Center;

        Assert.IsFalse(window.TransformToVisual(child).TryTransform(center, out _));
        Assert.ThrowsExactly<InvalidOperationException>(() => window.TranslatePoint(center, child));
    }

    [TestMethod]
    public void GetPosition_ReturnsThePositionInTheTransformedElement()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (_, decorator, child) = MakeRotated();
        var corner = RotatedCorner(decorator);
        var args = new MouseEventArgs(corner, corner);

        AssertPoint(new Point(0, 0), args.GetPosition(child));
    }

    [TestMethod]
    public void Button_InsideAScaledViewbox_ClicksWhereItIsDrawn()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var button = new Button { Content = new Border(), Width = 60, Height = 20 };
        int clicks = 0;
        button.Click += () => clicks++;
        var viewbox = new Viewbox { Child = button, Width = 240, Height = 80 };
        var window = HeadlessWindow.Create();
        var canvas = new Canvas();
        canvas.Add(viewbox);
        window.Content = canvas;
        window.PerformLayout();

        // Scaled up, the button fills the viewbox's height; (150,50) lies outside its unscaled layout box.
        window.SendClick(new Point(150, 50));

        Assert.AreEqual(1, clicks);
    }

    [TestMethod]
    public void Slider_TurnedUpright_TakesTheValueAlongTheDrawnTrack()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var slider = new Slider { Width = 200, Height = 24, Minimum = 0, Maximum = 100 };
        var decorator = new TransformDecorator
        {
            Child = slider,
            Mode = TransformMode.Layout,
            Transform = Matrix3x2.CreateRotation(MathF.PI / 2),
        };
        var window = HeadlessWindow.Create();
        var canvas = new Canvas();
        canvas.Add(decorator);
        Canvas.SetLeft(decorator, 100);
        Canvas.SetTop(decorator, 50);
        window.Content = canvas;
        window.PerformLayout();
        var center = decorator.Bounds.Center;

        // A quarter turn clockwise puts the slider's start at the top and its end at the bottom.
        window.SendClick(new Point(center.X, center.Y + 95));
        double nearEnd = slider.Value;
        window.SendClick(new Point(center.X, center.Y - 95));
        double nearStart = slider.Value;

        Assert.IsGreaterThan(85, nearEnd);
        Assert.IsLessThan(15, nearStart);
    }

    [TestMethod]
    public void ScrollBar_TurnedUpright_PagesAlongTheDrawnTrack()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var scrollBar = new ScrollBar { Orientation = Orientation.Horizontal, Width = 200, Height = 16, Minimum = 0, Maximum = 100, ViewportSize = 10, LargeChange = 10, Value = 50 };
        var decorator = new TransformDecorator
        {
            Child = scrollBar,
            Mode = TransformMode.Layout,
            Transform = Matrix3x2.CreateRotation(MathF.PI / 2),
        };
        var window = HeadlessWindow.Create();
        var canvas = new Canvas();
        canvas.Add(decorator);
        Canvas.SetLeft(decorator, 100);
        Canvas.SetTop(decorator, 50);
        window.Content = canvas;
        window.PerformLayout();
        var center = decorator.Bounds.Center;

        // A quarter turn clockwise puts the track's start at the top; a press past the thumb pages toward it.
        window.SendClick(new Point(center.X, center.Y + 95));
        double afterEndPress = scrollBar.Value;
        window.SendClick(new Point(center.X, center.Y - 95));
        double afterStartPress = scrollBar.Value;

        Assert.AreEqual(60, afterEndPress, TOLERANCE);
        Assert.AreEqual(50, afterStartPress, TOLERANCE);
    }

    [TestMethod]
    public void ContextMenu_BelowAScaledTarget_OpensUnderWhereTheTargetIsDrawn()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var target = new Border { Width = 120, Height = 32 };
        var decorator = new TransformDecorator { Child = target, Transform = Matrix3x2.CreateScale(2), TransformOrigin = new Point(0, 0) };
        var window = HeadlessWindow.Create();
        var canvas = new Canvas();
        canvas.Add(decorator);
        Canvas.SetLeft(decorator, 60);
        Canvas.SetTop(decorator, 40);
        window.Content = canvas;
        window.PerformLayout();

        var menu = new ContextMenu().Item("Alpha").Item("Beta");
        menu.Placement = MenuPlacement.Below;
        menu.Show(target);
        window.PerformLayout();

        Assert.AreEqual(decorator.Bounds.X, menu.Bounds.X, 0.5);
        Assert.AreEqual(decorator.Bounds.Y + 64, menu.Bounds.Y, 0.5, "the target is drawn 64 tall");
    }

    [TestMethod]
    public void UntransformedTree_ConvertsExactlyByBoundsDifference()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var first = new Border { Width = 33.3, Height = 10 };
        var second = new Border { Width = 10, Height = 10 };
        var identity = new TransformDecorator { Child = second };
        var window = HeadlessWindow.Create();
        var canvas = new Canvas();
        canvas.Add(first);
        canvas.Add(identity);
        Canvas.SetLeft(first, 17.7);
        Canvas.SetTop(first, 3.1);
        Canvas.SetLeft(identity, 101.9);
        Canvas.SetTop(identity, 57.3);
        window.Content = canvas;
        window.PerformLayout();

        var point = new Point(1.25, 2.5);
        var expected = new Point(
            point.X + (first.Bounds.X - second.Bounds.X),
            point.Y + (first.Bounds.Y - second.Bounds.Y));
        Assert.AreEqual(expected, first.TranslatePoint(point, second));
        Assert.AreEqual(new Point(point.X + first.Bounds.X, point.Y + first.Bounds.Y), first.TranslatePoint(point, window));
        Assert.AreEqual(second.Bounds, second.GetBoundsInRoot());
        Assert.AreEqual(new Point(5, 6), second.TransformFromRoot(new Point(5, 6)));
        var args = new MouseEventArgs(new Point(120, 70), new Point(120, 70));
        Assert.AreEqual(new Point(120 - second.Bounds.X, 70 - second.Bounds.Y), args.GetPosition(second));
    }

    /// <summary>Where the child's top-left corner is drawn once turned a quarter clockwise about the decorator's center.</summary>
    private static Point RotatedCorner(TransformDecorator decorator)
        => new(decorator.Bounds.Center.X + 20, decorator.Bounds.Center.Y - 40);

    /// <summary>An 80x40 child turned a quarter clockwise about the center of its decorator, placed at (100,50) in the content.</summary>
    private static (Window Window, TransformDecorator Decorator, Border Child) MakeRotated()
    {
        var child = new Border { Width = 80, Height = 40 };
        var decorator = new TransformDecorator { Child = child, Transform = Matrix3x2.CreateRotation(MathF.PI / 2) };
        var window = HeadlessWindow.Create();
        var canvas = new Canvas();
        canvas.Add(decorator);
        Canvas.SetLeft(decorator, 100);
        Canvas.SetTop(decorator, 50);
        window.Content = canvas;
        window.PerformLayout();
        return (window, decorator, child);
    }

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.AreEqual(expected.X, actual.X, TOLERANCE, $"X of {actual}");
        Assert.AreEqual(expected.Y, actual.Y, TOLERANCE, $"Y of {actual}");
    }
}
