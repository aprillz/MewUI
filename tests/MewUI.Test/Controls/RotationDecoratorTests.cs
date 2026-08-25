using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class RotationDecoratorTests
{
    [TestMethod]
    public void Measure_SwapsChildDimensions()
    {
        var child = new Border { Width = 40, Height = 20 };
        var decorator = new RotationDecorator { Child = child };

        decorator.Measure(Size.Infinity);

        Assert.AreEqual(new Size(20, 40), decorator.DesiredSize);
    }

    [TestMethod]
    public void Child_ReplacementUpdatesParents()
    {
        var first = new Border();
        var second = new Border();
        var decorator = new RotationDecorator { Child = first };

        decorator.Child = second;

        Assert.IsNull(first.Parent);
        Assert.AreSame(decorator, second.Parent);
    }

    [TestMethod]
    [DataRow(Rotation.Clockwise90)]
    [DataRow(Rotation.CounterClockwise90)]
    public void HitTest_MapsPointIntoRotatedChild(Rotation rotation)
    {
        var child = new Border { Width = 40, Height = 20 };
        var decorator = new RotationDecorator
        {
            Child = child,
            Rotation = rotation,
        };

        decorator.Measure(Size.Infinity);
        decorator.Arrange(new Rect(0, 0, 20, 40));

        var hit = decorator.HitTest(new Point(10, 2));

        Assert.AreSame(child, hit);
    }
}
