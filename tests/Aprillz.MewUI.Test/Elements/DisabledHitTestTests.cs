using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Elements;

/// <summary>
/// A disabled element must absorb the pointer (be its own hit result) instead of letting the hit fall
/// through to whatever sits behind it. Controls gate their own input on IsEffectivelyEnabled.
/// </summary>
[TestClass]
public sealed class DisabledHitTestTests
{
    [TestMethod]
    public void DisabledElement_IsStillHitTested()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Headless window uses the Windows-only GDI factory.");
            return;
        }

        var target = new Border { Width = 100, Height = 100, IsEnabled = false };
        var window = HeadlessWindow.Create(200, 200);
        window.Content = target;
        window.PerformLayout();

        var hit = window.HitTest(new Point(10, 10));

        Assert.IsNotNull(hit);
        Assert.IsTrue(
            ReferenceEquals(hit, target) || VisualTree.IsInSubtreeOf(hit, target),
            "a disabled element should absorb the hit rather than pass it through");
    }
}
