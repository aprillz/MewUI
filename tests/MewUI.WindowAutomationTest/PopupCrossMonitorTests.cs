using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

[TestClass]
public sealed class PopupCrossMonitorTests
{
    [TestMethod]
    public async Task PopupKeepsOwnerScaleOnEveryMonitor()
    {
        Assert.IsTrue(OperatingSystem.IsWindows() && RealAppSession.IsAvailable);
        var monitors = MonitorMatrix.Monitors;
        Assert.IsTrue(monitors.Select(monitor => monitor.Dpi).Distinct().Count() > 1);
        foreach (var monitor in monitors)
            Console.WriteLine($"DISPLAY {monitor.Label} bounds={monitor.PixelBounds}");

        var observations = new List<string>();
        foreach (var ownerMonitor in monitors)
        foreach (var popupMonitor in monitors)
        {
            await RealAppSession.RunAsync(async () =>
            {
                var trigger = new Button { Content = new TextBlock { Text = "Placement owner" } };
                var window = new Window
                {
                    Title = "Issue 247 popup scale",
                    StartupLocation = WindowStartupLocation.Manual,
                    WindowSize = WindowSize.Resizable(400, 240),
                    Content = trigger,
                };
                int clicks = 0;
                var content = new Button { Width = 200, Height = 96, Content = new TextBlock { Text = "Popup hit test" } };
                content.Click += () => clicks++;
                var popup = new Popup { Content = content, StaysOpen = true };
                string label = $"{ownerMonitor.ScalePercent}% owner / {popupMonitor.ScalePercent}% surface";
                try
                {
                    window.Show();
                    MonitorProbe.SetWindowPos(window.Handle, 0, ownerMonitor.PixelBounds.CenterX - 250,
                        ownerMonitor.PixelBounds.CenterY - 150, 0, 0, MonitorProbe.MOVE_ONLY);
                    await Task.Delay(300);
                    Assert.AreEqual(ownerMonitor.Dpi, window.Dpi, label);
                    var anchorPosition = window.ScreenToClient(new Point(popupMonitor.PixelBounds.CenterX - 180,
                        popupMonitor.PixelBounds.CenterY - 100));
                    var anchor = new Rect(anchorPosition.X, anchorPosition.Y, 160, 24);
                    popup.ShowAt(trigger, anchor);
                    await Task.Delay(300);
                    Assert.IsTrue(popup.IsOpen, label);
                    var surface = content.ResolveInputHostWindow();
                    Assert.IsNotNull(surface, label);
                    Assert.AreNotSame(window, surface, label);
                    Assert.AreEqual(popupMonitor.Dpi, surface.Dpi, label);
                    Assert.AreEqual(ownerMonitor.Dpi / (double)popupMonitor.Dpi, surface.HostedPortalScale, 0.001, label);
                    var screenOrigin = window.ClientToScreen(surface.HostedPortalOrigin);
                    var actualOrigin = new NativePoint();
                    Assert.IsTrue(ClientToScreen(surface.Handle, ref actualOrigin));
                    Near(screenOrigin.X, actualOrigin.X, label + " origin X");
                    Near(screenOrigin.Y, actualOrigin.Y, label + " origin Y");
                    Assert.IsTrue(GetClientRect(surface.Handle, out var client));
                    Near((200 + PopupChrome.ShadowPadding.Left + PopupChrome.ShadowPadding.Right) * window.DpiScale,
                        client.Right, label + " width");
                    Near((96 + PopupChrome.ShadowPadding.Top + PopupChrome.ShadowPadding.Bottom) * window.DpiScale,
                        client.Bottom, label + " height");
                    var local = new Point(100, 48);
                    var expectedScreen = window.ClientToScreen(content.TranslatePoint(local, window));
                    var actualScreen = content.PointToScreen(local);
                    Near(expectedScreen.X, actualScreen.X, label + " element X");
                    Near(expectedScreen.Y, actualScreen.Y, label + " element Y");
                    var roundTrip = content.PointFromScreen(expectedScreen);
                    Near(local.X, roundTrip.X, label + " inverse X");
                    Near(local.Y, roundTrip.Y, label + " inverse Y");
                    int clientX = (int)Math.Round(expectedScreen.X - actualOrigin.X);
                    int clientY = (int)Math.Round(expectedScreen.Y - actualOrigin.Y);
                    nint packed = (nint)((clientY << 16) | (clientX & 0xffff));
                    SendMessage(surface.Handle, 0x0200, 0, packed);
                    SendMessage(surface.Handle, 0x0201, 1, packed);
                    SendMessage(surface.Handle, 0x0202, 0, packed);
                    await Task.Delay(80);
                    Assert.AreEqual(1, clicks, label + " native mouse input");
                    for (int iteration = 0; iteration < 5; iteration++)
                        popup.MoveTo(anchor);
                    await Task.Delay(80);
                    var settled = content.PointToScreen(local);
                    Near(actualScreen.X, settled.X, label + " stable X");
                    Near(actualScreen.Y, settled.Y, label + " stable Y");
                    observations.Add($"PASS {label} client={client.Right}x{client.Bottom} ratio={surface.HostedPortalScale:F3} clicks={clicks}");
                }
                finally
                {
                    popup.Close();
                    window.Close();
                }
            });
        }
        foreach (string observation in observations) Console.WriteLine(observation);
    }

    [TestMethod]
    public async Task DatePickerInSpanningWindowKeepsOwnerScale()
    {
        var observations = new List<string>();
        foreach (var ownerMonitor in MonitorMatrix.Monitors)
        foreach (var targetMonitor in MonitorMatrix.Monitors)
        {
            var ownerRect = ownerMonitor.PixelBounds;
            var targetRect = targetMonitor.PixelBounds;
            int overlapTop = Math.Max(ownerRect.Top, targetRect.Top);
            int overlapBottom = Math.Min(ownerRect.Bottom, targetRect.Bottom);
            bool targetRight = ownerRect.Right == targetRect.Left;
            bool targetLeft = ownerRect.Left == targetRect.Right;
            if ((!targetRight && !targetLeft) || overlapBottom - overlapTop < 300)
                continue;
            await RealAppSession.RunAsync(async () =>
            {
                var picker = new DatePicker
                {
                    Width = 160, Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                var window = new Window
                {
                    Title = "Issue 247 spanning DatePicker",
                    StartupLocation = WindowStartupLocation.Manual,
                    WindowSize = WindowSize.Resizable(400, 240),
                    Content = picker,
                };
                try
                {
                    window.Show();
                    MonitorProbe.SetWindowPos(window.Handle, 0, ownerRect.CenterX - 200,
                        ownerRect.CenterY - 150, 0, 0, MonitorProbe.MOVE_ONLY);
                    await Task.Delay(250);
                    int boundary = targetRight ? ownerRect.Right : ownerRect.Left;
                    int outerLeft = targetRight ? boundary - 1000 : boundary - 550;
                    int outerTop = overlapTop + 40;
                    MonitorProbe.SetWindowPos(window.Handle, 0, outerLeft, outerTop, 1550, 280, 0x0004);
                    await Task.Delay(300);
                    Assert.AreEqual(ownerMonitor.Dpi, window.Dpi, "spanning owner DPI");
                    int anchorX = targetRight ? boundary + 100 : boundary - 500;
                    var desired = window.ScreenToClient(new Point(anchorX, outerTop + 80));
                    picker.Margin = new Thickness(desired.X, desired.Y, 0, 0);
                    await Task.Delay(150);
                    var actualTarget = picker.PointToScreen(new Point(0, 0));
                    Near(anchorX + window.Padding.Left * window.DpiScale, actualTarget.X, "actual target X");
                    Assert.IsTrue(actualTarget.X >= targetRect.Left && actualTarget.X < targetRect.Right);
                    picker.IsDropDownOpen = true;
                    await Task.Delay(300);
                    var calendar = (UIElement?)typeof(DatePicker).GetField("_calendar",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(picker);
                    Assert.IsNotNull(calendar);
                    var surface = calendar.ResolveInputHostWindow();
                    Assert.IsNotNull(surface);
                    Assert.AreNotSame(window, surface);
                    Assert.AreEqual(targetMonitor.Dpi, surface.Dpi, "spanning popup DPI");
                    Assert.AreEqual(window.Dpi / (double)surface.Dpi, surface.HostedPortalScale, 0.001);
                    var expected = window.ClientToScreen(calendar.TranslatePoint(new Point(30, 30), window));
                    var actual = calendar.PointToScreen(new Point(30, 30));
                    Near(expected.X, actual.X, "spanning calendar X");
                    Near(expected.Y, actual.Y, "spanning calendar Y");
                    GetClientRect(surface.Handle, out var client);
                    Near((calendar.Bounds.Width + PopupChrome.ShadowPadding.Left + PopupChrome.ShadowPadding.Right) * window.DpiScale,
                        client.Right, "spanning calendar width");
                    observations.Add($"PASS spanning DatePicker {ownerMonitor.ScalePercent}% owner / {targetMonitor.ScalePercent}% surface target=({actualTarget.X},{actualTarget.Y}) client={client.Right}x{client.Bottom}");
                }
                finally { picker.IsDropDownOpen = false; window.Close(); }
            });
        }
        foreach (string observation in observations) Console.WriteLine(observation);
        Assert.IsTrue(observations.Count > 0, "No adjacent horizontal displays to exercise");
    }

    private static void Near(double expected, double actual, string label)
        => Assert.AreEqual(expected, actual, 1.1, label);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nuint parameter, nint position);
}
