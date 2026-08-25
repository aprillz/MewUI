using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class CalendarTextEngineTests
{
    [TestMethod]
    public void Calendar_Render_UsesSharedContentLayoutsAndPaintOnlySelectionChanges()
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
            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(320, 280, 1));
            var calendar = new Calendar
            {
                DisplayDate = new DateTime(2026, 8, 1),
                SelectedDate = new DateTime(2026, 8, 10)
            };
            using var window = HeadlessWindow.Create(320, 280);
            window.Content = calendar;
            window.PerformLayout();

            window.RenderFrameToSurface(surface);
            int firstRenderCount = factory.TextEngine.ManagedCache.Count;

            Assert.IsGreaterThanOrEqualTo(31, firstRenderCount,
                "Calendar day cells did not populate the shared TextEngine content cache.");

            calendar.SelectedDate = new DateTime(2026, 8, 20);
            window.RenderFrameToSurface(surface);

            Assert.AreEqual(firstRenderCount, factory.TextEngine.ManagedCache.Count,
                "Changing selection paint must not create new Calendar geometry layouts.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }
}
