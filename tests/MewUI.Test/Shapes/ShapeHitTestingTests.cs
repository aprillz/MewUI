using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Shapes;

[TestClass]
public sealed class ShapeHitTestingTests
{
    [TestMethod]
    public void RegisteredProviders_UseLatestResultAndRestoreAfterDispose()
    {
        PathShape shape = CreateShape();
        var firstProvider = new TestProvider(shape, ShapeHitTestResult.Hit);
        var secondProvider = new TestProvider(shape, ShapeHitTestResult.Miss);

        using IDisposable firstRegistration = ShapeHitTesting.Register(firstProvider);
        Assert.AreSame(shape, shape.HitTest(new Point(90, 90)));

        using (ShapeHitTesting.Register(secondProvider))
        {
            Assert.IsNull(shape.HitTest(new Point(90, 90)));
            Assert.AreEqual(1, secondProvider.CallCount);
        }

        Assert.AreSame(shape, shape.HitTest(new Point(90, 90)));
        Assert.AreEqual(2, firstProvider.CallCount);
    }

    [TestMethod]
    public void RegisteredProviders_RunOnlyAfterTheExistingBoundsCheck()
    {
        PathShape shape = CreateShape();
        var provider = new TestProvider(shape, ShapeHitTestResult.Miss);

        using IDisposable registration = ShapeHitTesting.Register(provider);

        Assert.IsNull(shape.HitTest(new Point(110, 50)));
        Assert.AreEqual(0, provider.CallCount);

        Assert.IsNull(shape.HitTest(new Point(50, 50)));
        Assert.AreEqual(1, provider.CallCount);
        Assert.IsNotNull(provider.LastRenderedGeometry);
    }

    private static PathShape CreateShape()
    {
        var shape = new PathShape
        {
            Data = PathGeometry.FromRect(0, 0, 100, 100),
        };
        shape.Arrange(new Rect(0, 0, 100, 100));
        return shape;
    }

    private sealed class TestProvider : IShapeHitTestProvider
    {
        private readonly Shape _shape;
        private readonly ShapeHitTestResult _result;

        public TestProvider(Shape shape, ShapeHitTestResult result)
        {
            _shape = shape;
            _result = result;
        }

        public int CallCount { get; private set; }

        public PathGeometry? LastRenderedGeometry { get; private set; }

        public ShapeHitTestResult HitTest(Shape shape, PathGeometry? renderedGeometry, Point point)
        {
            if (!ReferenceEquals(shape, _shape))
            {
                return ShapeHitTestResult.Unhandled;
            }

            CallCount++;
            LastRenderedGeometry = renderedGeometry;
            return _result;
        }
    }
}
