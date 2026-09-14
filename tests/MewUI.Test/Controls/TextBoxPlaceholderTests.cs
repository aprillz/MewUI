using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class TextBoxPlaceholderTests
{
    private const double WIDTH = 200;
    private const double HEIGHT = 40;

    [TestMethod]
    public void ChangingPlaceholder_RendersTheNewText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var changed = new TextBox { Width = WIDTH, Placeholder = "WWWWWWWW" };
        using var changedWindow = HeadlessWindow.Create(WIDTH, HEIGHT);
        changedWindow.Content = changed;
        byte[] initial = Render(changedWindow);

        changed.Placeholder = "ii";
        byte[] updated = Render(changedWindow);

        var fresh = new TextBox { Width = WIDTH, Placeholder = "ii" };
        using var freshWindow = HeadlessWindow.Create(WIDTH, HEIGHT);
        freshWindow.Content = fresh;
        byte[] expected = Render(freshWindow);

        CollectionAssert.AreNotEqual(initial, updated, "The placeholder change did not alter the rendered pixels.");
        CollectionAssert.AreEqual(expected, updated, "The updated placeholder rendered differently from a TextBox created with it.");
    }

    private static byte[] Render(Window window)
    {
        var factory = Application.DefaultGraphicsFactory;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage((int)WIDTH, (int)HEIGHT, 1));
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
    }
}
