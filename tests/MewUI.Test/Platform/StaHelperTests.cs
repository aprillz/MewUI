using Aprillz.MewUI;
using Aprillz.MewUI.Platform.Win32;

namespace MewUI.Test.Platform;

[TestClass]
public sealed class StaHelperTests
{
    [TestMethod]
    public void Run_WithoutRunningApplication_ExecutesOnStaThread()
    {
        Assert.IsFalse(Application.IsRunning);

        var apartment = StaHelper.Run(static () => Thread.CurrentThread.GetApartmentState());

        Assert.AreEqual(ApartmentState.STA, apartment);
    }

    [TestMethod]
    public void Run_ReturnsWorkerResult()
    {
        int result = StaHelper.Run(static () => 42);

        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public void Run_WorkerException_IsWrappedWithOriginalAsInner()
    {
        var wrapped = Assert.ThrowsExactly<InvalidOperationException>(
            static () => StaHelper.Run<object?>(static () => throw new FormatException("worker failure")));

        Assert.IsInstanceOfType<FormatException>(wrapped.InnerException);
    }
}
