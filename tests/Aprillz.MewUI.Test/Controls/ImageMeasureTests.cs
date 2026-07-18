using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ImageMeasureTests
{
    [TestMethod]
    public void Measure_Uniform_UsesConstrainedWidth()
    {
        var image = CreateImage(100, 50);

        image.Measure(new Size(200, double.PositiveInfinity));

        Assert.AreEqual(new Size(200, 100), image.DesiredSize);
    }

    [TestMethod]
    public void Measure_Uniform_UsesConstrainedHeight()
    {
        var image = CreateImage(100, 50);

        image.Measure(new Size(double.PositiveInfinity, 25));

        Assert.AreEqual(new Size(50, 25), image.DesiredSize);
    }

    [TestMethod]
    public void Measure_Fill_StretchesConstrainedAxesOnly()
    {
        var image = CreateImage(100, 50);
        image.StretchMode = Stretch.Fill;

        image.Measure(new Size(200, double.PositiveInfinity));

        Assert.AreEqual(new Size(200, 50), image.DesiredSize);
    }

    [TestMethod]
    public void Measure_None_UsesIntrinsicSize()
    {
        var image = CreateImage(100, 50);
        image.StretchMode = Stretch.None;

        image.Measure(new Size(200, 200));

        Assert.AreEqual(new Size(100, 50), image.DesiredSize);
    }

    private static Image CreateImage(double width, double height) =>
        new()
        {
            Source = new TestVectorImageSource(new Size(width, height))
        };

    private sealed class TestVectorImageSource(Size intrinsicSize) : IVectorImageSource
    {
        public Size IntrinsicSize { get; } = intrinsicSize;

        public IImage CreateImage(IGraphicsFactory factory) =>
            throw new NotSupportedException("Vector image measure should not rasterize.");

        public void Render(IGraphicsContext context, Rect destRect)
        {
        }
    }
}
