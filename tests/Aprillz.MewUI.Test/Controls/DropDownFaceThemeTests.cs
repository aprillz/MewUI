using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A drop-down face fills through a Background transition, so what the eye sees is the animated
/// interval, not the settled value. Stepping that interval is the only way to catch a frame painted
/// from the theme the tree started in rather than the one it runs in.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DropDownFaceThemeTests
{
    [TestMethod]
    public void FaceHoversInTheThemeTheApplicationSwitchedTo()
    {
        TestPlatformHosts.EnsureRegistered();

        var button = new SplitButton
        {
            Content = new TextBlock { Text = "Save" },
            DropDownMenu = new Menu().Item(new Command("test.item", "Item")),
            Width = 200,
            Height = 32,
        };
        var window = new Window { Content = button };
        window.AttachBackend(new HeadlessWindowBackend());
        window.SetClientSizeDip(400, 300);

        Color lightHover = default;
        Color darkHover = default;
        Color settled = default;
        var samples = new List<Color>();

        TestPlatformHosts.Queue.Enqueue(new ThemeSwitchHost(onRun: (app, mainWindow) =>
        {
            // Lay out under the startup theme first, then switch: that is what a dark-mode start looks
            // like from the tree's point of view.
            mainWindow.PerformLayout();
            lightHover = app.Theme.Palette.ButtonHoverBackground;

            app.SetTheme(ThemeVariant.Dark);
            mainWindow.PerformLayout();
            darkHover = app.Theme.Palette.ButtonHoverBackground;

            var face = (Button)VisualTree.Find(button, e => e is Button b && b.Content is ContentPresenter)!;
            mainWindow.SetIsActive(true);
            mainWindow.SendMouseMove(face.CenterOf());
            mainWindow.UpdateVisualStates();

            // No ForceStyleSnap here: snapping is exactly what hides the animated frames.
            long start = Stopwatch.GetTimestamp();
            AnimationManager.Instance.UpdateAt(start);
            for (int step = 1; step <= 12; step++)
            {
                long at = start + (long)(Stopwatch.Frequency * 0.02 * step);
                AnimationManager.Instance.UpdateAt(at);
                samples.Add(face.Background);
            }

            settled = face.Background;
        }));

        Application.Run(window);

        Assert.AreNotEqual(lightHover, darkHover, "the two themes must differ for this probe to mean anything");
        Assert.AreEqual(darkHover, settled, "the face did not settle on the running theme's hover fill");

        // Only the alpha may ramp. Color.Transparent is white at zero alpha, so an idle fill left at it
        // makes every frame a blend towards white, which over dark chrome reads as a bright flash on the
        // way in and again on the way out.
        var offenders = samples
            .Where(sample => sample.A > 0 && !SharesHue(sample, darkHover))
            .ToList();

        Assert.IsTrue(offenders.Count == 0,
            $"frames drifted off the hover hue (dark hover={darkHover}): "
            + string.Join(", ", offenders.Select(static color => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}")));
    }

    private static bool SharesHue(Color sample, Color target)
        => Math.Abs(sample.R - target.R) <= 1
        && Math.Abs(sample.G - target.G) <= 1
        && Math.Abs(sample.B - target.B) <= 1;

    private sealed class ThemeSwitchHost(Action<Application, Window>? onRun = null) : IPlatformHost
    {
        public IMessageBoxService MessageBox => null!;
        public IFileDialogService FileDialog => null!;
        public IClipboardService Clipboard => null!;
        public string DefaultFontFamily => "Arial";
        public IReadOnlyList<string> DefaultFontFallbacks => [];
        public IWindowBackend CreateWindowBackend(Window window) => throw new NotSupportedException();
        public IDispatcher CreateDispatcher(nint windowHandle) => throw new NotSupportedException();
        public uint GetSystemDpi() => 96;
        public ThemeVariant GetSystemThemeVariant() => ThemeVariant.Light;
        public uint GetDpiForWindow(nint windowHandle) => 96;
        public bool EnablePerMonitorDpiAwareness() => false;
        public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;
        public void Run(Application app, Window? mainWindow) => onRun?.Invoke(app, mainWindow!);
        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() { }
    }
}
