using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

internal static class PromptAssertions
{
    /// <summary>
    /// Every text block's arranged box must hold the text it measured. Arranged smaller than
    /// desired is exactly the state where render re-wraps against layout: the extra line spills
    /// over whatever sits below, and hit-testing keeps using the boxes layout handed out.
    /// </summary>
    public static void AssertTextFitsItsBounds(Window window, string stage)
    {
        var blocks = new List<TextBlock>();
        Collect((Element)window.Content!, blocks);
        Assert.AreNotEqual(0, blocks.Count, "the prompt should contain text");

        foreach (var block in blocks)
        {
            Assert.IsGreaterThanOrEqualTo(
                block.DesiredSize.Width - 0.51,
                block.Bounds.Width,
                $"{stage}: '{block.Text}' is arranged narrower than it measured");
            Assert.IsGreaterThanOrEqualTo(
                block.DesiredSize.Height - 0.51,
                block.Bounds.Height,
                $"{stage}: '{block.Text}' is arranged shorter than it measured");

            // The one that matches what is on screen: render lays the text out again against the
            // arranged bounds, and a taller result is the extra line painting over what sits below.
            var render = block.GetRenderLayoutMetrics();
            Assert.IsLessThanOrEqualTo(
                block.Bounds.Height + 0.51,
                render.ContentHeight,
                $"{stage}: '{block.Text}' renders {render.LineCount} line(s) at {render.ContentHeight:F2} in a box "
                + $"{block.Bounds.Height:F2} tall (arranged {block.Bounds.Width:F2} wide, measured {block.DesiredSize.Width:F2})");
        }
    }

    public static void Collect(Element element, List<TextBlock> into)
    {
        if (element is TextBlock textBlock)
        {
            into.Add(textBlock);
        }

        if (element is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                Collect(child, into);
            }
        }
        else if (element is ContentControl content && content.Content is Element inner)
        {
            Collect(inner, into);
        }
        else if (element is Border border && border.Child is Element borderChild)
        {
            Collect(borderChild, into);
        }
    }
}
