using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class ScratchSurfaceSizeTests
{
    [DataRow(1, 16)]
    [DataRow(16, 16)]
    [DataRow(17, 32)]
    [DataRow(256, 256)]
    [DataRow(257, 288)]
    [DataRow(480, 480)]
    [DataRow(567, 576)]
    [DataRow(1025, 1088)]
    [DataRow(1654, 1664)]
    [DataRow(4097, 4224)]
    [TestMethod]
    public void Approximate_UsesTightReusableSizeClasses(int requested, int expected)
    {
        Assert.AreEqual(expected, ScratchSurfaceSize.Approximate(requested));
    }

    [TestMethod]
    public void Approximate_NeverReturnsLessThanRequested()
    {
        for (int requested = 1; requested <= 8192; requested++)
        {
            Assert.IsGreaterThanOrEqualTo(requested, ScratchSurfaceSize.Approximate(requested));
        }
    }
}
