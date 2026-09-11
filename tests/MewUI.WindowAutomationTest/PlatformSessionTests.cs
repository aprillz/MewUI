using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// The application loop comes up on whichever platform the suite runs on, and a window shown on it becomes a
/// real native window that loads.
/// </summary>
[TestClass]
public sealed class PlatformSessionTests
{
    [TestMethod]
    public async Task Window_ShowsAsALoadedNativeWindow()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            bool loaded = false;
            var window = new Window
            {
                Title = "PlatformSession",
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(240, 160),
                Content = new TextBlock { Text = "session" },
            };
            window.Loaded += () => loaded = true;

            try
            {
                window.Show();
                window.MoveTo(80, 80);
                await Task.Delay(400);

                Assert.AreNotEqual((nint)0, window.Handle, "the shown window has no native handle");
                Assert.IsTrue(loaded, "the shown window never loaded");
            }
            finally
            {
                window.Close();
            }
        });
    }
}
