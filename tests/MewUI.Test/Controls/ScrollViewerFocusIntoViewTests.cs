using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Focus scroll must ignore a target that has no arranged box. An unarranged element reports
/// Bounds of (0,0,0,0), which reads as the tree origin and drags every ancestor viewer to the top.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ScrollViewerFocusIntoViewTests
{
    [TestMethod]
    public void UnarrangedTargetLeavesTheOffsetAlone()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var content = new StackPanel().Vertical();
        for (int index = 0; index < 40; index++)
        {
            content.Add(new TextBox { Text = $"Row {index}", Height = 24 });
        }

        var viewer = new ScrollViewer().VerticalScroll(ScrollMode.Auto).Content(content);
        using var window = HeadlessWindow.Create(320, 200);
        window.Content = viewer;
        window.PerformLayout();

        // Scroll away from the origin first, otherwise a viewport dragged to the top looks unchanged.
        ((IFocusIntoViewHost)viewer).OnDescendantFocused((TextBox)content.Children[30]);
        window.PerformLayout();

        double before = viewer.VerticalOffset;
        Assert.IsGreaterThan(0, before, "The viewer never scrolled, so the guard would not be exercised.");

        // A control can hand focus to an inner editor that the current pass has not arranged yet.
        var unarranged = new TextBox { Text = "editor" };
        content.Add(unarranged);

        ((IFocusIntoViewHost)viewer).OnDescendantFocused(unarranged);

        Assert.AreEqual(before, viewer.VerticalOffset, 0.01,
            $"An unarranged target moved the viewport from {before} to {viewer.VerticalOffset}.");
    }
}
