using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Layout;

/// <summary>A layout request raised while a sibling is being arranged is carried out in the same window layout.</summary>
[TestClass]
[DoNotParallelize]
public sealed class LayoutInvalidationOrderTests
{
    [TestMethod]
    public void InvalidateArrange_OfAnArrangedSiblingDuringArrange_ArrangesItAgain()
    {
        var (window, first, second) = CreateSiblings();
        int arranges = first.Arranges;

        second.OnArrange = () => first.InvalidateArrange();
        second.InvalidateArrange();
        window.PerformLayout();

        Assert.AreEqual(arranges + 1, first.Arranges, "the sibling was not arranged again");
        Assert.IsFalse(first.IsArrangeDirty, "the sibling was left arrange dirty");
        Assert.IsTrue(window.IsUpdatePassSettled, "the window did not settle");
    }

    [TestMethod]
    public void Measure_OfAnArrangedSiblingDuringArrange_ArrangesItAgain()
    {
        var (window, first, second) = CreateSiblings();
        int arranges = first.Arranges;

        second.OnArrange = () => first.Measure(new Size(55, 55));
        second.InvalidateArrange();
        window.PerformLayout();

        Assert.AreEqual(arranges + 1, first.Arranges, "the sibling was not arranged after it was measured");
        Assert.IsFalse(first.IsArrangeDirty, "the sibling was left arrange dirty");
        Assert.IsTrue(window.IsUpdatePassSettled, "the window did not settle");
    }

    [TestMethod]
    public void InvalidateMeasure_OfAnArrangedSiblingDuringArrange_MeasuresAndArrangesItAgain()
    {
        var (window, first, second) = CreateSiblings();
        int measures = first.Measures;
        int arranges = first.Arranges;

        second.OnArrange = () => first.InvalidateMeasure();
        second.InvalidateArrange();
        window.PerformLayout();

        Assert.AreEqual(measures + 1, first.Measures, "the sibling was not measured again");
        Assert.AreEqual(arranges + 1, first.Arranges, "the sibling was not arranged again");
        Assert.IsFalse(first.IsMeasureDirty || first.IsArrangeDirty, "the sibling was left dirty");
    }

    [TestMethod]
    public void InvalidateArrange_OfItselfDuringItsArrange_DoesNotArrangeItAgain()
    {
        var (window, _, second) = CreateSiblings();
        int arranges = second.Arranges;

        second.OnArrange = () => second.InvalidateArrange();
        second.InvalidateArrange();
        window.PerformLayout();

        Assert.AreEqual(arranges + 1, second.Arranges, "an element asking to be arranged while it arranges was arranged again");
        Assert.IsFalse(second.IsArrangeDirty, "the element was left arrange dirty");
        Assert.IsTrue(window.IsUpdatePassSettled, "the window did not settle");
    }

    private static (Window Window, CountingElement First, CountingElement Second) CreateSiblings()
    {
        var first = new CountingElement();
        var second = new CountingElement();
        var window = HeadlessWindow.Create(400, 300);
        window.Content = new StackPanel().Vertical().Children(first, second);
        window.PerformLayout();
        return (window, first, second);
    }

    /// <summary>Counts its measures and arranges, and runs a callback once from its next arrange.</summary>
    internal sealed class CountingElement : FrameworkElement
    {
        public int Measures { get; private set; }

        public int Arranges { get; private set; }

        public Action? OnArrange { get; set; }

        protected override Size MeasureContent(Size availableSize)
        {
            Measures++;
            return new Size(40, 20);
        }

        protected override void ArrangeContent(Rect bounds)
        {
            Arranges++;
            var callback = OnArrange;
            OnArrange = null;
            callback?.Invoke();
        }
    }
}
