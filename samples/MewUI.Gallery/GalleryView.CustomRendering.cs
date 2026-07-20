using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement CustomRenderingPage() =>
        CardGrid(
            Card("Offscreen", new SampleOffscreenControl { Height = 240, Width = 280 }),
            // Result bitmap changes after every worker-thread render, so a stale BitmapCache
            // blit would hide the update until something else invalidates the border.
            Card("Async Rendering", AsyncConfettiContent()).Cached(false)
        );

    private static readonly int[] ConfettiCountOptions = [500, 5_000, 50_000, 100_000];

    private static FrameworkElement AsyncConfettiContent()
    {
        var canvas = new AsyncConfettiCanvas { Height = 200, Width = 280 };
        var ring = new ProgressRing { Width = 64, Height = 64 }
            .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
            .HorizontalAlignment(HorizontalAlignment.Center)
            .VerticalAlignment(VerticalAlignment.Center)
            .IsVisible(false);
        var countComboBox = new ComboBox()
            .Width(100)
            .Items(ConfettiCountOptions.Select(c => c.ToString("N0")).ToArray())
            .SelectedIndex(2);
        var button = new Button().Content("_Start")
            .OnClick(() => canvas.StartRender(ConfettiCountOptions[Math.Max(0, countComboBox.SelectedIndex)]));

        canvas.BusyChanged += busy =>
        {
            ring.IsActive = busy;
            ring.IsVisible = busy;
            button.IsEnabled = !busy;
            countComboBox.IsEnabled = !busy;
        };

        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new DockPanel()
                    .Spacing(8)
                    .Children(button, countComboBox),
                new Grid().Children(canvas, ring));
    }
}

/// <summary>Renders a random burst of confetti paths off the UI thread on each
/// <see cref="StartRender"/>, using <see cref="IGraphicsFactory.AcquireBackgroundRenderScope"/>
/// (the same worker-render pattern as MewUI.Svg.Sample's SvgView).</summary>
public sealed class AsyncConfettiCanvas : Control
{
    private static readonly Color[] ConfettiColors =
    [
        new Color(244, 67, 54),
        new Color(255, 193, 7),
        new Color(76, 175, 80),
        new Color(33, 150, 243),
        new Color(156, 39, 176),
        new Color(255, 152, 0),
    ];

    private IRenderSurface? _surface;
    private IImage? _view;
    private bool _busy;

    public event Action<bool>? BusyChanged;

    public void StartRender(int count)
    {
        if (_busy || count <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _busy = true;
        BusyChanged?.Invoke(true);

        double dpiScale = GetDpi() / 96.0;
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpiScale));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpiScale));
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

        _ = RenderAsync(pixelWidth, pixelHeight, dpiScale, bounds, count);
    }

    private async Task RenderAsync(int pixelWidth, int pixelHeight, double dpiScale, Rect bounds, int count)
    {
        IRenderSurface? newSurface = null;
        IImage? newImage = null;
        try
        {
            await Task.Run(() =>
            {
                var factory = GetGraphicsFactory();

                // Worker-thread setup mirroring SvgView.RebuildAsync: GL backends activate a
                // share-listed worker context; D2D (MULTI_THREADED factory) and GDI (per-HDC)
                // return a no-op scope.
                using var workerScope = factory.AcquireBackgroundRenderScope();

                var surface = factory.CreateSurface(
                    RenderSurfaceDescriptor.Offscreen(pixelWidth, pixelHeight, dpiScale));
                using (var context = factory.CreateContext(surface))
                {
                    context.BeginFrame(surface);
                    try
                    {
                        var rng = new Random();
                        for (int i = 0; i < count; i++)
                        {
                            DrawConfettiPiece(context, bounds, rng);
                        }
                    }
                    finally
                    {
                        context.EndFrame();
                    }
                }

                newImage = factory.CreateImageView(surface);
                newSurface = surface;
            });
        }
        catch
        {
            newImage?.Dispose();
            newSurface?.Dispose();
            newImage = null;
            newSurface = null;
        }

        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher is not null && !dispatcher.IsOnUIThread)
        {
            dispatcher.BeginInvoke(() => Commit(newSurface, newImage));
        }
        else
        {
            Commit(newSurface, newImage);
        }
    }

    private void Commit(IRenderSurface? surface, IImage? image)
    {
        _view?.Dispose();
        _surface?.Dispose();
        _surface = surface;
        _view = image;
        _busy = false;
        BusyChanged?.Invoke(false);
        InvalidateVisual();
    }

    private void DrawConfettiPiece(IGraphicsContext context, Rect bounds, Random rng)
    {
        double x = bounds.X + rng.NextDouble() * bounds.Width;
        double y = bounds.Y + rng.NextDouble() * bounds.Height;
        double width = 3 + rng.NextDouble() * 5;
        double height = 2 + rng.NextDouble() * 3;
        var color = ConfettiColors[rng.Next(ConfettiColors.Length)].Lerp(Theme.Palette.WindowBackground, 0.25);

        context.Save();
        context.Translate(x, y);
        context.Rotate(rng.NextDouble() * Math.PI * 2);
        context.FillPath(PathGeometry.FromRect(-width / 2, -height / 2, width, height), color);
        context.Restore();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        if (_view is not null)
        {
            context.DrawImage(_view, Bounds);
        }
    }

    protected override void OnDispose()
    {
        _view?.Dispose();
        _surface?.Dispose();
        base.OnDispose();
    }
}

public class SampleOffscreenControl : OffscreenControl
{
    private bool _testCase;

    protected override void RenderOffscreen(IGraphicsContext context, Rect bounds)
    {
        Point p0, p1, p2, p3;

        if (_testCase)
        {
            (p0, p1, p2, p3) = (bounds.TopLeft, bounds.BottomRight, bounds.TopRight, bounds.BottomLeft);
        }
        else
        {
            (p0, p1, p2, p3) = (bounds.TopRight, bounds.BottomLeft, bounds.TopLeft, bounds.BottomRight);
        }

        context.DrawLine(p0, p1, Color.Green, 1);
        context.DrawLine(p2, p3, Color.Blue, 3);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        _testCase = !_testCase;
        InvalidateOffscreen();
    }
}
