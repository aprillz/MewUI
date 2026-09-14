extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class TextBoxPlaceholderTests
{
    private const int WIDTH = 200;
    private const int HEIGHT = 40;

    [TestMethod]
    public void Gdi_ChangingPlaceholder_RendersTheNewText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertPlaceholderChangeRenders(factory);
    }

    [TestMethod]
    public void Direct2D_ChangingPlaceholder_RendersTheNewText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        AssertPlaceholderChangeRenders(factory);
    }

    [TestMethod]
    public void MewVGWin32_ChangingPlaceholder_RendersTheNewText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("MewVG Win32 is Windows-only.");
            return;
        }

        using var factory = new MewVGWin32GraphicsFactory();
        AssertPlaceholderChangeRenders(factory);
    }

    [TestMethod]
    public void ChangingPlaceholder_RemeasuresAnEmptyTextBox()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = new TextBox { Placeholder = "i", HorizontalAlignment = HorizontalAlignment.Left };
        using var window = HeadlessWindow.Create(400, HEIGHT);
        window.Content = textBox;
        window.PerformLayout();
        double narrow = textBox.DesiredSize.Width;

        textBox.Placeholder = "WWWWWWWWWWWW";
        window.PerformLayout();

        Assert.IsGreaterThan(narrow, textBox.DesiredSize.Width,
            "An empty TextBox kept the width measured for its previous placeholder.");
    }

    private static void AssertPlaceholderChangeRenders(IGraphicsFactory factory)
    {
        var previousFactory = Application.DefaultGraphicsFactory;
        Application.DefaultGraphicsFactory = factory;
        try
        {
            using var scope = factory.AcquireBackgroundRenderScope();
            AssertPlaceholderChangeRenders(factory, static () => new TextBox());
            AssertPlaceholderChangeRenders(factory, static () => new MultiLineTextBox());
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private static void AssertPlaceholderChangeRenders(IGraphicsFactory factory, Func<TextBase> create)
    {
        var changed = create();
        changed.Width = WIDTH;
        changed.Height = HEIGHT;
        changed.Placeholder = "WWWWWWWW";
        using var changedWindow = HeadlessWindow.Create(WIDTH, HEIGHT);
        changedWindow.Content = changed;
        byte[] initial = Render(factory, changedWindow);

        changed.Placeholder = "ii";
        byte[] updated = Render(factory, changedWindow);

        var fresh = create();
        fresh.Width = WIDTH;
        fresh.Height = HEIGHT;
        fresh.Placeholder = "ii";
        using var freshWindow = HeadlessWindow.Create(WIDTH, HEIGHT);
        freshWindow.Content = fresh;
        byte[] expected = Render(factory, freshWindow);

        string subject = $"{factory.Backend} {changed.GetType().Name}";
        CollectionAssert.AreNotEqual(initial, updated, $"{subject}: the placeholder change did not alter the rendered pixels.");
        CollectionAssert.AreEqual(expected, updated, $"{subject}: the updated placeholder rendered differently from a control created with it.");
    }

    private static byte[] Render(IGraphicsFactory factory, Window window)
    {
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
    }
}
