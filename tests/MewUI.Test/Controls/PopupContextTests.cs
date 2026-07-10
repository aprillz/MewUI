using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Headless end-to-end coverage for popup context resolution: the popup pipeline runs with a
/// real (fake-handled) window, real layout, and real GDI text measurement.
/// </summary>
[TestClass]
public sealed class PopupContextTests
{
    // Popup content hosted at the window layer must resolve StyleName and inherited values
    // through its owner, not through the window it is visually parented to.
    [TestMethod]
    public void ShowPopup_ContentResolvesThroughOwnerContext()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var accent = Color.FromRgb(120, 30, 30);
        var sheet = new StyleSheet();
        sheet.Define("accent", () => new Style(typeof(Border))
        {
            Setters = [Setter.Create(Control.BackgroundProperty, accent)],
        });

        var window = HeadlessWindow.Create();
        var owner = new Border { StyleSheet = sheet, FontSize = 24 };
        window.Content = owner;
        window.PerformLayout();

        var popupText = new TextBlock { Text = "hello" };
        var popupRoot = new Border { StyleName = "accent", Child = popupText };
        window.ShowPopup(owner, popupRoot, new Rect(10, 10, 200, 50));

        Assert.AreEqual(accent, popupRoot.Background, "StyleName resolved via owner's StyleSheet");
        Assert.AreEqual(24, popupText.FontSize, "inherited font size diverted through the owner");
        Assert.AreSame(window, popupRoot.FindVisualRoot(), "popup stays visually rooted in the window");

        window.ClosePopup(popupRoot);
    }

    // Real text measurement must use the owner's font: the same content measures larger
    // when opened from an owner with a larger inherited font size.
    [TestMethod]
    public void ShowPopup_TextMeasuresWithOwnerFont()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var stack = new StackPanel();
        var smallOwner = new Border { FontSize = 10 };
        var largeOwner = new Border { FontSize = 30 };
        stack.Add(smallOwner);
        stack.Add(largeOwner);
        window.Content = stack;
        window.PerformLayout();

        Size MeasurePopupFor(Border popupOwner)
        {
            var text = new TextBlock { Text = "measure me" };
            window.ShowPopup(popupOwner, text, new Rect(0, 0, 400, 100));
            text.Measure(new Size(400, 100));
            var size = text.DesiredSize;
            window.ClosePopup(text);
            return size;
        }

        var small = MeasurePopupFor(smallOwner);
        var large = MeasurePopupFor(largeOwner);

        Assert.IsTrue(large.Height > small.Height,
            $"expected taller text under the 30pt owner (small={small}, large={large})");
        Assert.IsTrue(large.Width > small.Width,
            $"expected wider text under the 30pt owner (small={small}, large={large})");
    }

    // Closing a popup must clear the owner diversion so a reused popup element does not
    // keep resolving through a previous owner.
    [TestMethod]
    public void ClosePopup_ClearsOwnerContext()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var stack = new StackPanel();
        var firstOwner = new Border { FontSize = 30 };
        var secondOwner = new Border { FontSize = 11 };
        stack.Add(firstOwner);
        stack.Add(secondOwner);
        window.Content = stack;
        window.PerformLayout();

        var popup = new Border();
        window.ShowPopup(firstOwner, popup, new Rect(0, 0, 100, 40));
        Assert.AreEqual(30, popup.FontSize);
        window.ClosePopup(popup);

        window.ShowPopup(secondOwner, popup, new Rect(0, 0, 100, 40));
        Assert.AreEqual(11, popup.FontSize, "reopened popup follows the new owner");
        window.ClosePopup(popup);
    }
}
