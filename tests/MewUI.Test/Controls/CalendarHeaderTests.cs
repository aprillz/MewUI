using System.Globalization;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

using Calendar = Aprillz.MewUI.Controls.Calendar;

namespace MewUI.Test.Controls;

/// <summary>
/// The header shows the displayed month at the width its text needs in the first frame after the month changes, not
/// at the width of the month shown before.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CalendarHeaderTests
{
    [TestMethod]
    public void ALongerMonthNameIsLaidOutInTheFrameThatShowsIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Headless window uses the Windows-only GDI factory.");
            return;
        }

        var previousCulture = CultureInfo.CurrentCulture;
        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(320, 280, 1));
            var calendar = new Calendar { DisplayDate = new DateTime(2025, 5, 1) };
            using var window = HeadlessWindow.Create(320, 280);
            window.Content = calendar;
            var header = Header(calendar);

            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            Assert.AreEqual("2025 May", header.Text);

            calendar.DisplayDate = new DateTime(2025, 9, 1);
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            double shownWidth = header.Bounds.Width;

            window.PerformLayout();
            Assert.AreEqual("2025 September", header.Text);
            Assert.AreEqual(header.Bounds.Width, shownWidth, "the new month was drawn at the width of the month before");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void AHeaderWiderThanTheDayGridKeepsItsWholeText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Headless window uses the Windows-only GDI factory.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            // A larger font stands in for a platform font wider than the one the grid was sized against.
            var calendar = new Calendar { FontSize = 24, HorizontalAlignment = HorizontalAlignment.Left };
            using var window = HeadlessWindow.Create(600, 400);
            window.Content = calendar;
            window.PerformLayout();

            var header = Header(calendar);
            var natural = new TextBlock { Text = header.Text, FontSize = header.FontSize, FontWeight = header.FontWeight, FontFamily = header.FontFamily };
            natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Assert.IsGreaterThanOrEqualTo(natural.DesiredSize.Width, header.Bounds.Width, "the header text was laid out narrower than it is");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private static TextBlock Header(Calendar calendar)
    {
        TextBlock? header = null;
        ((IVisualTreeHost)calendar).VisitChildren(child =>
        {
            if (child is Button { Content: TextBlock label })
            {
                header = label;
            }

            return true;
        });

        Assert.IsNotNull(header, "the calendar has no header");
        return header;
    }
}
