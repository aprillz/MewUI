using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// The damage overlay reports what a frame did, so it must not change what a frame does: the same
/// changes have to record, damage and count the same with it on as with it off, and showing it must
/// not make frames of its own.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedDamageOverlayTests
{
    private const int WIDTH = 320;
    private const int HEIGHT = 240;

    [TestMethod]
    public void Overlay_ChangesNeitherWhatIsRecordedNorWhatIsDamaged()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        string withoutOverlay = Run(factory, overlay: false);
        string withOverlay = Run(factory, overlay: true);

        Assert.AreEqual(withoutOverlay, withOverlay);
    }

    private static string Run(GdiGraphicsFactory factory, bool overlay)
    {
        var first = new Button { Content = new TextBlock { Text = "First" } };
        var second = new Button { Content = new TextBlock { Text = "Second" } };
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Margin = new Thickness(12) };
        stack.Children(first, second);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = stack;
        window.PerformLayout();
        if (overlay)
        {
            window.ToggleDamageOverlay();
        }

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        var log = new System.Text.StringBuilder();
        Frame(window, surface, log);
        Frame(window, surface, log);

        second.Background = Color.FromArgb(255, 200, 40, 40);
        Frame(window, surface, log);

        // Frames with nothing to do: an overlay that kept itself alive would show up here.
        Frame(window, surface, log);
        Frame(window, surface, log);

        first.Width = 140;
        Frame(window, surface, log);
        Frame(window, surface, log);

        log.Append(window.RetainedFrames);
        return log.ToString();
    }

    private static void Frame(Window window, IRenderSurface surface, System.Text.StringBuilder log)
    {
        window.PerformLayout();
        window.RetainedStatistics?.Reset();
        window.RenderFrameToSurface(surface);
        var statistics = window.RetainedStatistics!;
        log.Append(window.LastRetainedDamage?.ToString() ?? "whole")
            .Append(" records=").Append(statistics.ContentRecordCount)
            .Append(" replays=").Append(statistics.ContentReplayCount)
            .Append(" areas=").Append(window.LastRetainedDamageAreas.Count)
            .AppendLine();
    }
}
