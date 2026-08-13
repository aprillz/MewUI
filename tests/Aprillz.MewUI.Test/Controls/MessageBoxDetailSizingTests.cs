using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// The prompt's detail pane sizes to its document: expanding it grows the dialog to the laid-out
/// text (clamped by the window's fit maximum) with no scroll bar over fully visible content, and
/// the expanded size holds still across layout passes instead of re-wrapping every frame.
/// </summary>
[TestClass]
public sealed class MessageBoxDetailSizingTests
{
    private const string DETAIL_TEXT =
        "System.InvalidOperationException: The operation failed.\n"
        + "   at App.Module.Process() in Module.cs:line 42\n"
        + "   at App.Main() in Program.cs:line 10\n"
        + "\n"
        + "This is a multiline detail text that can be scrolled if the content is too long.";

    [TestMethod]
    public void ExpandingDetail_GrowsTheDialog_WithoutScrollBars()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (box, detail, toggle) = CreateExpandedPrompt();
        var collapsedSize = LayOut(box);

        toggle.IsChecked = true;
        var expandedSize = LayOut(box);

        Assert.IsGreaterThan(collapsedSize.Height, expandedSize.Height, "the dialog grows for the detail text");
        Assert.IsFalse(detail.IsVerticalScrollBarVisible, "fully visible detail must not scroll");
        Assert.IsFalse(detail.IsHorizontalScrollBarVisible);
        Assert.IsGreaterThan(40, detail.Bounds.Height, "the detail pane holds the laid-out text, not a token strip");
    }

    [TestMethod]
    public void ExpandedDetail_HoldsItsSizeAcrossPasses()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (box, detail, toggle) = CreateExpandedPrompt();
        LayOut(box);
        toggle.IsChecked = true;

        var first = LayOut(box);
        var firstDetail = detail.Bounds;

        for (int pass = 0; pass < 4; pass++)
        {
            var next = LayOut(box);
            Assert.AreEqual(first, next, $"client size moved on pass {pass}");
            Assert.AreEqual(firstDetail, detail.Bounds, $"detail bounds moved on pass {pass}");
            Assert.IsFalse(detail.IsVerticalScrollBarVisible, $"scroll bar flickered in on pass {pass}");
        }
    }

    private static (MessageBoxWindow box, MultiLineTextBox detail, CheckBox toggle) CreateExpandedPrompt()
    {
        var box = new MessageBoxWindow(
            "This is a Warning message box sample.",
            PromptIconKind.Warning,
            MessageBoxWindow.ButtonsOkCancel,
            detail: DETAIL_TEXT);

        var backend = new ApplyingWindowBackend();
        box.AttachBackend(backend);
        backend.Window = box;
        box.SetClientSizeDip(100, 100);
        box.SetMaxHeightFromOwner(null);

        MultiLineTextBox? detail = null;
        CheckBox? toggle = null;
        Collect((Element)box.Content!, ref detail, ref toggle);
        Assert.IsNotNull(detail);
        Assert.IsNotNull(toggle);
        return (box, detail, toggle);
    }

    private static Size LayOut(Window box)
    {
        for (int pass = 0; pass < 6; pass++)
        {
            box.PerformLayout();
            if (box.IsUpdatePassSettled)
            {
                break;
            }
        }

        return box.ClientSize;
    }

    private static void Collect(Element element, ref MultiLineTextBox? detail, ref CheckBox? toggle)
    {
        if (element is MultiLineTextBox multiLine)
        {
            detail = multiLine;
        }
        else if (element is CheckBox checkBox)
        {
            toggle ??= checkBox;
        }

        if (element is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                Collect(child, ref detail, ref toggle);
            }
        }
        else if (element is ContentControl content && content.Content is Element inner)
        {
            Collect(inner, ref detail, ref toggle);
        }
        else if (element is Border border && border.Child is Element borderChild)
        {
            Collect(borderChild, ref detail, ref toggle);
        }
    }
}
