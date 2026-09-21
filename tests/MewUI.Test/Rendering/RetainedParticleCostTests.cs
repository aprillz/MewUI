extern alias MewVGWin32;

using System.Diagnostics;
using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// Measures a visual that draws many small shapes which all move every frame, the way a particle
/// overlay does: the scene-driven frame against a frame drawn straight from the visuals, and the first
/// frames against the last ones to show a cost that grows while the work stays the same.
/// It reports numbers and asserts none. Runs only when MEWUI_PARTICLE_COST is 1.
/// Not parallelizable: timing, and the process-wide graphics factory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedParticleCostTests
{
    private const int WIDTH = 1000;
    private const int HEIGHT = 700;
    private const int FRAMES = 300;

    [TestMethod]
    [DataRow("Gdi", 300, false)]
    [DataRow("Gdi", 1500, false)]
    [DataRow("MewVG", 300, false)]
    [DataRow("MewVG", 1500, false)]
    [DataRow("MewVG", 1500, true)]
    [DataRow("Gdi", 1500, true)]
    public void MeasureMovingParticles(string backend, int particleCount, bool onTheOverlayLayer)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("MEWUI_PARTICLE_COST") != "1")
        {
            Assert.Inconclusive("Set MEWUI_PARTICLE_COST=1 on Windows to run the particle cost measurement.");
            return;
        }

        IGraphicsFactory factory = backend == "Gdi" ? new GdiGraphicsFactory() : new MewVGWin32GraphicsFactory();
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;
        using var renderScope = factory is MewVGWin32GraphicsFactory ? factory.AcquireBackgroundRenderScope() : null;

        var particles = new Particles(particleCount);
        var counter = new TextBlock { Text = "frame 0" };
        var page = new StackPanel { Orientation = Orientation.Vertical };
        page.Children(counter);
        for (int index = 0; index < 12; index++)
        {
            page.Children(new Button { Content = new TextBlock { Text = "Button " + index }, Margin = new Thickness(4) });
        }

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        if (onTheOverlayLayer)
        {
            window.Content = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = page };
            window.OverlayLayer.Add(particles);
        }
        else
        {
            window.Content = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = particles };
        }

        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        for (int warm = 0; warm < 20; warm++)
        {
            particles.Step();
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            window.RenderReferenceFrameToSurface(reference);
        }

        var retained = new double[FRAMES];
        long retainedBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < FRAMES; frame++)
        {
            particles.Step();
            counter.Text = "frame " + frame;
            window.PerformLayout();
            long start = Stopwatch.GetTimestamp();
            window.RenderFrameToSurface(surface);
            retained[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        retainedBytes = GC.GetAllocatedBytesForCurrentThread() - retainedBytes;

        var direct = new double[FRAMES];
        long directBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < FRAMES; frame++)
        {
            particles.Step();
            counter.Text = "again " + frame;
            window.PerformLayout();
            long start = Stopwatch.GetTimestamp();
            window.RenderReferenceFrameToSurface(reference);
            direct[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        directBytes = GC.GetAllocatedBytesForCurrentThread() - directBytes;

        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""

            === moving particles ({backend}, {particleCount} shapes, overlay layer {onTheOverlayLayer}, {FRAMES} frames) ===
            scene-driven : first 50 {Median(retained, 0, 50):0.00} ms, last 50 {Median(retained, FRAMES - 50, 50):0.00} ms, {retainedBytes / FRAMES / 1024.0:0.0} KB per frame, last damage {window.LastRetainedDamage?.ToString() ?? "whole"} ({window.LastWholeFrameReason})
            straight     : first 50 {Median(direct, 0, 50):0.00} ms, last 50 {Median(direct, FRAMES - 50, 50):0.00} ms, {directBytes / FRAMES / 1024.0:0.0} KB per frame
            """));
    }

    private static double Median(double[] samples, int start, int count)
    {
        var window = samples.AsSpan(start, count).ToArray();
        Array.Sort(window);
        return window[window.Length / 2];
    }

    private sealed class Particles : FrameworkElement
    {
        private readonly PathGeometry _path = new();
        private readonly double[] _x;
        private readonly double[] _y;
        private readonly Random _random = new(7);

        public Particles(int count)
        {
            _x = new double[count];
            _y = new double[count];
            for (int index = 0; index < count; index++)
            {
                _x[index] = _random.NextDouble() * WIDTH;
                _y[index] = _random.NextDouble() * HEIGHT;
            }

            IsHitTestVisible = false;
        }

        public void Step()
        {
            for (int index = 0; index < _x.Length; index++)
            {
                _y[index] += 2.5;
                if (_y[index] > HEIGHT)
                {
                    _y[index] = 0;
                }
            }

            InvalidateVisual();
        }

        protected override void OnRender(IGraphicsContext context)
        {
            for (int index = 0; index < _x.Length; index++)
            {
                double left = _x[index];
                double top = _y[index];
                if ((index & 3) == 0)
                {
                    context.FillEllipse(new Rect(left, top, 4, 4), Color.FromArgb(255, 84, 175, 255));
                }
                else
                {
                    _path.Clear();
                    _path.MoveTo(left, top);
                    _path.LineTo(left + 6, top + 1);
                    _path.LineTo(left + 5, top + 4);
                    _path.LineTo(left - 1, top + 3);
                    _path.Close();
                    context.FillPath(_path, Color.FromArgb(255, 255, 107, 107));
                }
            }
        }
    }
}
