using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ProgressBarTemplateTests
{
    private const double TRACK_WIDTH = 200;

    private static DelegateControlTemplate<ProgressBar> TrackAndIndicatorTemplate(Border indicator)
        => new((owner, ctx) =>
        {
            var track = new Border { Width = TRACK_WIDTH, Height = 8, Child = indicator };
            ctx.Register(ProgressBar.PART_TRACK, track);
            ctx.Register(ProgressBar.PART_INDICATOR, indicator);
            ctx.Bind(indicator, Control.BackgroundProperty, TextElement.ForegroundProperty);
            return track;
        });

    [TestMethod]
    public void IndicatorPart_WidthFollowsValueAndTrackLength()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var indicator = new Border();
        var window = HeadlessWindow.Create();
        var bar = new ProgressBar { Value = 25, Template = TrackAndIndicatorTemplate(indicator) };
        window.Content = bar;
        window.PerformLayout();

        Assert.AreEqual(TRACK_WIDTH * 0.25, indicator.Bounds.Width, 0.01, "a quarter of the range fills a quarter of the track");
        Assert.AreEqual(bar.Foreground, indicator.Background, "the indicator color comes from Foreground through the template binding");

        bar.Value = 50;
        window.PerformLayout();

        Assert.AreEqual(TRACK_WIDTH * 0.5, indicator.Bounds.Width, 0.01, "a value change re-arranges the indicator");

        bar.Maximum = 200;
        window.PerformLayout();

        Assert.AreEqual(TRACK_WIDTH * 0.25, indicator.Bounds.Width, 0.01, "a range change re-sizes the indicator");
    }

    [TestMethod]
    public void MissingParts_LeaveTheTemplateAsIs()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var root = new Border { Width = 120, Height = 8 };
        var window = HeadlessWindow.Create();
        var bar = new ProgressBar { Value = 25, Template = new DelegateControlTemplate<ProgressBar>((_, _) => root) };
        window.Content = bar;
        window.PerformLayout();

        Assert.AreEqual(120, root.Bounds.Width, 0.01, "a template without parts still owns the visuals");
    }

    [TestMethod]
    public void ClearTemplate_RestoresTheDrawnBarInForeground()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var indicator = new Border();
        var window = HeadlessWindow.Create();
        var bar = new ProgressBar { Value = 50, Template = TrackAndIndicatorTemplate(indicator) };
        window.Content = bar;
        window.PerformLayout();

        bar.Template = null;
        bar.Foreground = Color.FromRgb(200, 10, 10);
        window.PerformLayout();

        var recorder = new FillRecordingContext();
        bar.Render(recorder);

        CollectionAssert.Contains(recorder.FillColors, bar.Foreground, "the drawn bar fills with Foreground");
    }

    [TestMethod]
    public void DefaultStyle_GivesForegroundTheAccentAndDisabledAccent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var bar = new ProgressBar { Value = 50 };
        var ring = new ProgressRing { Value = 50 };
        window.Content = new StackPanel().Children(bar, ring);
        window.PerformLayout();

        Assert.AreEqual(bar.ThemeInternal.Palette.Accent, bar.Foreground, "the bar fills with the accent by default");
        Assert.AreEqual(ring.ThemeInternal.Palette.Accent, ring.Foreground, "the ring fills with the accent by default");

        bar.IsEnabled = false;
        ring.IsEnabled = false;
        window.PerformLayout();

        Assert.AreEqual(bar.ThemeInternal.Palette.DisabledAccent, bar.Foreground, "a disabled bar fills with the disabled accent");
        Assert.AreEqual(ring.ThemeInternal.Palette.DisabledAccent, ring.Foreground, "a disabled ring fills with the disabled accent");
    }

    private sealed class FillRecordingContext : NoOpGraphicsContext
    {
        public List<Color> FillColors { get; } = [];

        public override void FillRectangle(Rect rect, Color color) => FillColors.Add(color);

        public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color) => FillColors.Add(color);
    }
}
