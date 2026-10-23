using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;
using Aprillz.MewUI.Text;

namespace MewUI.Headless.Test;

/// <summary>
/// Gate 6: a TextBlock laid out in measure-only mode takes the size the graphics backend's own text
/// engine measures for the same text, which is what a window on the real platform lays out with.
/// </summary>
[TestClass]
public sealed class Gate6LayoutMatchTests
{
    private const string TEXT = "Headless layout matches the backend measurement";
    private const double FONT_SIZE = 14;

    [TestMethod]
    public void TextBlock_SizeEqualsBackendTextMeasurement()
    {
        string family = OperatingSystem.IsWindows() ? "Arial" : OperatingSystem.IsMacOS() ? "Helvetica" : "sans-serif";
        var textBlock = new TextBlock
        {
            Text = TEXT,
            FontFamily = family,
            FontSize = FONT_SIZE,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = HeadlessTestHost.ShowWindow(textBlock);
        try
        {
            var bounds = HeadlessTestHost.Shared.Run(() => textBlock.Bounds);
            var measured = HeadlessTestHost.Shared.Run(() => Application.DefaultGraphicsFactory.TextEngine.CreateLayout(new TextLayoutRequest
            {
                Text = TEXT.AsMemory(),
                Dpi = 96,
                DefaultStyle = new TextRunStyle(family, FONT_SIZE),
                Paragraph = new TextParagraphStyle { Culture = System.Globalization.CultureInfo.CurrentUICulture },
            }).MeasuredSize);

            Assert.IsGreaterThan(0, measured.Width, "the backend measured nothing");
            Assert.AreEqual(Math.Ceiling(measured.Width), bounds.Width, 1e-9, "width");
            Assert.AreEqual(measured.Height, bounds.Height, 1e-9, "height");
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }

    [TestMethod]
    public void Session_UsesTheRegisteredBackendForMeasurement()
    {
        string expected = Application.DefaultGraphicsFactory.Backend;

        Assert.AreEqual(expected, HeadlessTestHost.Shared.Run(() => Application.Current.GraphicsFactory.Backend));
    }
}
