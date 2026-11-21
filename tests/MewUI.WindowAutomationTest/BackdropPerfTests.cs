using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Measurement, not a test: what a frame costs with a backdrop in it, on the GPU of the machine the suite runs
/// on, against the same scene without one. Frames are drawn into an offscreen surface of the window's factory
/// as fast as they go, and the wait for the GPU to finish them is part of the time. Each case appends one line
/// to backdrop-perf.txt next to the suite. Run in Release; the case data are the panel size and blur radius.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class BackdropPerfTests
{
    private const int WIDTH = 1280;
    private const int HEIGHT = 800;
    private const int WARM_UP = 20;
    private const int FRAMES = 120;
    private const int ROUNDS = 5;

    [TestMethod]
    [Timeout(600_000)]
    [DataRow(320.0, 200.0, 12.0)]
    [DataRow(640.0, 400.0, 12.0)]
    [DataRow(1200.0, 720.0, 12.0)]
    [DataRow(640.0, 400.0, 4.0)]
    [DataRow(640.0, 400.0, 32.0)]
    public async Task Measure(double panelWidth, double panelHeight, double radius)
    {
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real application loop.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            var rigs = new List<Rig>();
            try
            {
                foreach (bool backdrop in new[] { false, true })
                {
                    var scene = Scene.Build(backdrop ? radius : 0, panelWidth, panelHeight);
                    scene.Window.Show();
                    scene.Window.MoveTo(40, 40);
                    await Task.Delay(500);
                    rigs.Add(new Rig(scene));
                }

                // The two take turns, so that whatever else the machine does lands on both alike.
                for (int round = 0; round < ROUNDS; round++)
                {
                    foreach (var rig in rigs)
                    {
                        rig.MeasureRound();
                    }
                }

                var plain = rigs[0];
                var withBackdrop = rigs[1];
                Report($"[backdrop] {plain.Backend} scale={plain.Scale} panel={panelWidth}x{panelHeight} radius={radius}: "
                    + $"far change {Rig.Summary(plain.Far)} / {Rig.Summary(withBackdrop.Far)}; "
                    + $"caret under the panel {Rig.Summary(plain.Caret)} / {Rig.Summary(withBackdrop.Caret)}; "
                    + $"scroll {Rig.Summary(plain.Scroll)} / {Rig.Summary(withBackdrop.Scroll)} (without / with the backdrop)");
            }
            finally
            {
                foreach (var rig in rigs)
                {
                    rig.Dispose();
                }

                await Task.Delay(300);
            }
        });
    }

    private static void Report(string line)
        => File.AppendAllLines(Path.Combine(AppContext.BaseDirectory, "backdrop-perf.txt"), [line]);

    private sealed class Block : Control
    {
        internal Color Fill { get; set; }

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Fill);
    }

    /// <summary>Stripes, lines of text and a caret, with one panel over the middle that has a backdrop when the radius is above zero.</summary>
    private sealed class Scene
    {
        internal required Window Window { get; init; }
        internal required Canvas Content { get; init; }
        internal required Block Caret { get; init; }
        internal required Block Far { get; init; }

        internal static Scene Build(double radius, double panelWidth, double panelHeight)
        {
            var content = new Canvas();
            for (int index = 0; index < 40; index++)
            {
                content.Children(Place(new Block { Fill = Color.FromArgb(255, (byte)(index * 6), (byte)(255 - (index * 5)), 120) }, index * 32, 0, 32, HEIGHT + 4));
            }

            for (int line = 0; line < 24; line++)
            {
                var text = new TextBlock
                {
                    Text = $"Line {line + 1}: the quick brown fox jumps over the lazy dog 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz",
                    FontSize = 16,
                    Foreground = line % 2 == 0 ? Color.White : Color.FromRgb(20, 20, 20),
                };
                Canvas.SetLeft(text, 12);
                Canvas.SetTop(text, 8 + (line * 33));
                content.Children(text);
            }

            var caret = Place(new Block { Fill = Color.Black }, 640, 390, 2, 20);
            // Outside every panel size used here.
            var far = Place(new Block { Fill = Color.Black }, 4, 4, 20, 20);

            var panelContent = new StackPanel().Vertical().Spacing(8).Children(
                new TextBlock { Text = "Panel", FontSize = 18, Foreground = Color.White },
                new Button { Content = new TextBlock { Text = "Button" }, HorizontalAlignment = HorizontalAlignment.Left });
            panelContent.Margin = new Thickness(16);
            var panel = Place(new Border
            {
                Background = Color.FromArgb(56, 255, 255, 255),
                BorderBrush = Color.FromArgb(170, 255, 255, 255),
                BorderThickness = 1,
                Child = panelContent,
            }, Math.Round((WIDTH - panelWidth) / 2), Math.Round((HEIGHT - panelHeight) / 2), panelWidth, panelHeight);
            if (radius > 0)
            {
                panel.BackdropFilter = new BlurFilter(radius);
            }

            var root = new Canvas();
            Place(content, 0, 0, WIDTH, HEIGHT + 4);
            root.Children(content, caret, far, panel);

            var window = new Window
            {
                Title = "BackdropPerf",
                StartupLocation = WindowStartupLocation.Manual,
                Padding = new Thickness(0),
                Borderless = true,
                WindowSize = WindowSize.Fixed(WIDTH, HEIGHT),
                Content = root,
            };

            return new Scene { Window = window, Content = content, Caret = caret, Far = far };
        }

        private static T Place<T>(T element, double left, double top, double width, double height) where T : FrameworkElement
        {
            element.Width = width;
            element.Height = height;
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            return element;
        }
    }

    /// <summary>One window of the comparison with the offscreen surface its frames go to.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly Scene _scene;
        private readonly IGraphicsFactory _factory;
        private readonly IDisposable _renderScope;
        private readonly IRenderSurface _surface;
        private readonly byte[] _sink;
        private readonly int _width;
        private int _tick;

        internal Rig(Scene scene)
        {
            _scene = scene;
            var window = scene.Window;
            _factory = window.GraphicsFactory;
            Scale = window.GetDpi() / 96.0;
            _width = (int)Math.Round(window.ClientSize.Width * Scale);
            int height = (int)Math.Round(window.ClientSize.Height * Scale);
            _renderScope = _factory.AcquireBackgroundRenderScope();
            _surface = _factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(_width, height, Scale, hasAlpha: false));
            _sink = new byte[_width * height * 4];
        }

        internal string Backend => _factory.Backend;
        internal double Scale { get; }
        internal List<double> Far { get; } = [];
        internal List<double> Caret { get; } = [];
        internal List<double> Scroll { get; } = [];

        /// <summary>The median of the rounds, in milliseconds a frame.</summary>
        internal static string Summary(List<double> rounds)
        {
            var sorted = rounds.OrderBy(value => value).ToList();
            return $"{sorted[sorted.Count / 2]:F3}";
        }

        internal void MeasureRound()
        {
            Far.Add(Time(() =>
            {
                _scene.Far.Fill = _tick++ % 2 == 0 ? Color.White : Color.Black;
                _scene.Far.InvalidateVisual();
            }));
            Caret.Add(Time(() =>
            {
                _scene.Caret.Fill = _tick++ % 2 == 0 ? Color.White : Color.Black;
                _scene.Caret.InvalidateVisual();
            }));
            // Everything under the panel moves by a unit and back, the way scrolled content does.
            Scroll.Add(Time(() => Canvas.SetTop(_scene.Content, _tick++ % 2 == 0 ? -1 : 0)));
        }

        private double Time(Action change)
        {
            var window = _scene.Window;
            for (int index = 0; index < WARM_UP; index++)
            {
                change();
                window.PerformLayout();
                window.RenderFrameToSurface(_surface);
            }

            ((IRenderDevice)_factory).TryReadPixels(_surface, _sink, _width * 4);
            var watch = Stopwatch.StartNew();
            for (int index = 0; index < FRAMES; index++)
            {
                change();
                window.PerformLayout();
                window.RenderFrameToSurface(_surface);
            }

            // The GPU has finished every frame only once something reads the surface back.
            ((IRenderDevice)_factory).TryReadPixels(_surface, _sink, _width * 4);
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds / FRAMES;
        }

        public void Dispose()
        {
            _surface.Dispose();
            _renderScope.Dispose();
            _scene.Window.Close();
        }
    }
}
