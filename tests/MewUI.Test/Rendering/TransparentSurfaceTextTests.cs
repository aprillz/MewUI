using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Text of a control that paints its background on a part of itself only, drawn on a surface that is
/// transparent there: black text must come out as black with coverage in the alpha channel, on the
/// retained frame and on a frame drawn straight from the visuals alike. Subpixel antialiasing has
/// nothing opaque to blend against on such pixels and leaves colour in them.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TransparentSurfaceTextTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 120;

    // Premultiplied black is zero in every colour channel; rounding leaves at most this much.
    private const int COLOUR_TOLERANCE = 2;

    private static readonly Color _black = Color.FromArgb(255, 0, 0, 0);

    public TestContext TestContext { get; set; } = null!;

    private static TextBlock Label(string text) => new() { Text = text, Foreground = _black, FontSize = 14 };

    [TestMethod]
    public void CheckBoxLabel_OnATransparentSurface_IsBlack()
    {
        var label = Label("CheckBox label");
        Check(new CheckBox { Content = label }, label);
    }

    [TestMethod]
    public void RadioButtonLabel_OnATransparentSurface_IsBlack()
    {
        var label = Label("RadioButton label");
        Check(new RadioButton { Content = label }, label);
    }

    [TestMethod]
    public void GroupBoxHeader_OnATransparentSurface_IsBlack()
    {
        var header = Label("GroupBox header");
        Check(new GroupBox { Header = header, Content = new Border { Height = 30 } }, header);
    }

    [TestMethod]
    public void ToggleSwitchLabel_OnATransparentSurface_IsBlack()
    {
        var label = Label("ToggleSwitch label");
        Check(new ToggleSwitch { Content = label }, label);
    }

    private void Check(FrameworkElement control, TextBlock label)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        using var factoryScope = new DefaultGraphicsFactoryScope(factory);

        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        control.Margin = new Thickness(10);
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.AllowsTransparency = true;
        window.Background = Color.Transparent;
        window.Padding = new Thickness(0);
        window.Content = control;
        window.PerformLayout();

        using var retained = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: true));
        window.RenderFrameToSurface(retained);
        using var direct = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: true));
        window.RenderReferenceFrameToSurface(direct);

        var box = label.Bounds;
        AssertBlackText(retained, box, $"{control.GetType().Name}, a retained frame");
        AssertBlackText(direct, box, $"{control.GetType().Name}, a frame drawn straight from the visuals");
    }

    private void AssertBlackText(IRenderSurface surface, Rect box, string label)
    {
        ReadOnlySpan<byte> pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int left = Math.Max(0, (int)Math.Floor(box.X));
        int top = Math.Max(0, (int)Math.Floor(box.Y));
        int right = Math.Min(WIDTH, (int)Math.Ceiling(box.Right));
        int bottom = Math.Min(HEIGHT, (int)Math.Ceiling(box.Bottom));

        int covered = 0;
        int coloured = 0;
        int strongestColour = 0;
        int strongestAlpha = 0;
        for (int row = top; row < bottom; row++)
        {
            for (int column = left; column < right; column++)
            {
                int offset = ((row * WIDTH) + column) * 4;
                int colour = Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2]));
                int alpha = pixels[offset + 3];
                if (alpha > 0 || colour > 0)
                {
                    covered++;
                }

                if (colour > COLOUR_TOLERANCE)
                {
                    coloured++;
                    strongestColour = Math.Max(strongestColour, colour);
                }

                strongestAlpha = Math.Max(strongestAlpha, alpha);
            }
        }

        TestContext.WriteLine($"{label}: text box {box}, {covered} pixels drawn, strongest alpha {strongestAlpha}, {coloured} pixels with colour up to {strongestColour}");
        Assert.IsGreaterThan(20, covered, $"{label}: the text left nothing on the surface in its box {box}");
        Assert.IsGreaterThan(150, strongestAlpha, $"{label}: the text has no solid pixel");
        Assert.AreEqual(0, coloured, $"{label}: {coloured} pixels of black text carry colour, up to {strongestColour}");
    }
}
