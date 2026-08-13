using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using System.Runtime.InteropServices;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A user drag differs from a programmatic move: the OS modal move loop owns the window rectangle
/// while WM_DPICHANGED arrives, so a fit resize submitted from inside that handler can be stamped
/// straight back to the OS-suggested rectangle. This reproduces the override and expects the window
/// to re-fit once the move loop exits.
/// </summary>
[TestClass]
public sealed class DragCrossingTests
{
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out WinRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int left, top, right, bottom; }

    private const string WARNING_DETAIL =
        "System.InvalidOperationException: The operation failed.\n"
        + "   at App.Module.Process() in Module.cs:line 42\n"
        + "   at App.Main() in Program.cs:line 10\n"
        + "\n"
        + "This is a multiline detail text that can be scrolled if the content is too long.";

    [TestMethod]
    [DynamicData(nameof(MonitorMatrix.Transitions), typeof(MonitorMatrix),
        DynamicDataDisplayName = nameof(MonitorMatrix.TransitionName),
        DynamicDataDisplayNameDeclaringType = typeof(MonitorMatrix))]
    public Task DraggedPrompt_RefitsAfterTheMoveLoopOverridesItsResize(MonitorProbe from, MonitorProbe to)
        => RunDragScenario(from, to, detail: null, expandDetail: false);

    /// <summary>
    /// The detail pane sizes to its own text, so expanding it makes the prompt far wider than the
    /// message alone and gives the crossing a second thing to re-fit.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(MonitorMatrix.Transitions), typeof(MonitorMatrix),
        DynamicDataDisplayName = nameof(MonitorMatrix.TransitionName),
        DynamicDataDisplayNameDeclaringType = typeof(MonitorMatrix))]
    public Task DraggedPromptWithDetailExpanded_RefitsAcrossScales(MonitorProbe from, MonitorProbe to)
        => RunDragScenario(from, to, WARNING_DETAIL, expandDetail: true);

    private async Task RunDragScenario(MonitorProbe from, MonitorProbe to, string? detail, bool expandDetail)
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
                Title = "DragCrossing owner",
                StartupLocation = WindowStartupLocation.Manual,
            };
            var box = new MessageBoxWindow(
                "This is a Warning message box sample.",
                PromptIconKind.Warning,
                MessageBoxWindow.ButtonsOkCancel,
                detail: detail);

            try
            {
                owner.Show();
                MonitorProbe.SetWindowPos(owner.Handle, 0,
                    from.PixelBounds.CenterX - 200, from.PixelBounds.CenterY - 300, 0, 0, MonitorProbe.MOVE_ONLY);

                box.SetMaxHeightFromOwner(owner);
                _ = box.ShowDialogAsync(owner);
                await Task.Delay(250);
                Assert.AreEqual(from.Dpi, box.Dpi, $"the prompt must open on {from.Label}");

                if (expandDetail)
                {
                    ExpandDetail(box);
                    await Task.Delay(300);
                }

                GetWindowRect(box.Handle, out var beforeRect);
                int scaledWidth = (int)Math.Round((beforeRect.right - beforeRect.left) * (double)to.Dpi / from.Dpi);
                int scaledHeight = (int)Math.Round((beforeRect.bottom - beforeRect.top) * (double)to.Dpi / from.Dpi);

                // The drag: enter the move loop, cross monitors, then stamp the OS-suggested
                // rectangle over whatever the DPI handler resized to, as the loop's tracked
                // rectangle does, and only then leave the loop.
                SendMessage(box.Handle, WM_ENTERSIZEMOVE, 0, 0);
                MonitorProbe.SetWindowPos(box.Handle, 0,
                    to.PixelBounds.CenterX - 150, to.PixelBounds.CenterY - 100, 0, 0, MonitorProbe.MOVE_ONLY);
                MonitorProbe.SetWindowPos(box.Handle, 0, 0, 0, scaledWidth, scaledHeight, MonitorProbe.RESIZE_ONLY);
                SendMessage(box.Handle, WM_EXITSIZEMOVE, 0, 0);
                await Task.Delay(400);

                Assert.AreEqual(to.Dpi, box.Dpi, $"the drag must have landed on {to.Label}");
                string stage = $"after dragging {from.ScalePercent}% -> {to.ScalePercent}%"
                    + (expandDetail ? " with the detail expanded" : string.Empty);
                PromptAssertions.AssertTextFitsItsBounds(box, stage);
            }
            finally
            {
                box.Close();
                owner.Close();
            }
        });
    }

    private static void ExpandDetail(Window box)
    {
        var toggle = FindDetailToggle((Element)box.Content!);
        Assert.IsNotNull(toggle, "the prompt was built without a detail toggle");
        toggle.IsChecked = true;
    }

    private static CheckBox? FindDetailToggle(Element element)
    {
        if (element is CheckBox checkBox)
        {
            return checkBox;
        }

        if (element is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (FindDetailToggle(child) is CheckBox found)
                {
                    return found;
                }
            }
        }
        else if (element is ContentControl content && content.Content is Element inner)
        {
            return FindDetailToggle(inner);
        }
        else if (element is Border border && border.Child is Element borderChild)
        {
            return FindDetailToggle(borderChild);
        }

        return null;
    }
}
