using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Moving a fit-sized prompt between monitors with different scales must re-fit it: text metrics
/// change with the DPI, and a window still sized for the old metrics renders its text wrapped
/// against layout, drawing over its siblings and detaching hit-testing from what is visible.
/// One case is generated per scale transition the machine can actually produce.
/// </summary>
[TestClass]
public sealed class DpiCrossingTests
{
    [TestMethod]
    public void TheMachineOffersAScaleTransition()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only.");
            return;
        }

        Assert.IsTrue(
            MonitorMatrix.HasMixedScales,
            $"no mixed-scale transition to exercise on this machine: {MonitorMatrix.Describe()}");
    }

    [TestMethod]
    [DynamicData(nameof(MonitorMatrix.Transitions), typeof(MonitorMatrix),
        DynamicDataDisplayName = nameof(MonitorMatrix.TransitionName),
        DynamicDataDisplayNameDeclaringType = typeof(MonitorMatrix))]
    public async Task PromptKeepsTextUnwrapped_AfterCrossingScales(MonitorProbe from, MonitorProbe to)
    {
        if (!OperatingSystem.IsWindows() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real Win32 application loop.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            var owner = new Window
            {
                Title = "DpiCrossing owner",
                StartupLocation = WindowStartupLocation.Manual,
            };
            var box = new MessageBoxWindow(
                "This is a Warning message box sample.",
                PromptIconKind.Warning);

            try
            {
                owner.Show();
                MonitorProbe.SetWindowPos(owner.Handle, 0,
                    from.PixelBounds.CenterX - 200, from.PixelBounds.CenterY - 300, 0, 0, MonitorProbe.MOVE_ONLY);

                box.SetMaxHeightFromOwner(owner);
                _ = box.ShowDialogAsync(owner);
                await Task.Delay(250);

                Assert.AreEqual(from.Dpi, box.Dpi,
                    $"the prompt must open on {from.Label} for this transition to mean anything");
                PromptAssertions.AssertTextFitsItsBounds(box, $"on {from.Label}");

                MonitorProbe.SetWindowPos(box.Handle, 0,
                    to.PixelBounds.CenterX - 150, to.PixelBounds.CenterY - 100, 0, 0, MonitorProbe.MOVE_ONLY);
                await Task.Delay(400);

                Assert.AreEqual(to.Dpi, box.Dpi, $"the move must have landed on {to.Label}");
                PromptAssertions.AssertTextFitsItsBounds(box, $"after {from.ScalePercent}% -> {to.ScalePercent}%");
            }
            finally
            {
                box.Close();
                owner.Close();
            }
        });
    }
}
