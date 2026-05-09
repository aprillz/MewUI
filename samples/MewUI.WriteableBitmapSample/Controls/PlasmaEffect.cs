using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.WriteableBitmapSample.Controls;

/// <summary>
/// A plasma effect control demonstrating real-time animation with WriteableBitmap.
/// Uses direct pixel manipulation for high-performance rendering.
/// </summary>
public class PlasmaEffect : FrameworkElement
{
    private WriteableBitmap? _bitmap;
    private IImage? _image;

    private double _time;
    private DispatcherTimer? _timer;

    // Pre-computed sine lookup table for performance
    private static readonly double[] SinTable;
    private static readonly int TableSize = 256;

    static PlasmaEffect()
    {
        SinTable = new double[TableSize];
        for (int i = 0; i < TableSize; i++)
        {
            SinTable[i] = Math.Sin(i * Math.PI * 2 / TableSize);
        }
    }

    public double Speed
    {
        get;
        set => field = Math.Max(0.1, Math.Min(5.0, value));
    } = 1.0;

    public bool IsAnimating
    {
        get;
        set
        {
            if (field == value) return;
            field = value;

            if (field)
            {
                StartAnimation();
            }
            else
            {
                StopAnimation();
            }
        }
    }

    public void Start() => IsAnimating = true;
    public void Stop() => IsAnimating = false;
    public void Toggle() => IsAnimating = !IsAnimating;

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        bool recreated = EnsureBitmap();
        if (recreated)
        {
            // Render initial frame for the new buffer.
            RenderPlasma(_time);
        }

        if (_image != null)
        {
            context.DrawImage(_image, bounds);
        }
    }

    private bool EnsureBitmap()
    {
        var bounds = Bounds;
        double scale = GetDpi() / 96.0;
        int pw = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        int ph = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

        if (_bitmap != null && _bitmap.PixelWidth == pw && _bitmap.PixelHeight == ph)
        {
            return false;
        }

        _image?.Dispose();
        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(pw, ph);
        _image = GetGraphicsFactory().CreateImageFromPixelSource(_bitmap);
        return true;
    }

    private void RenderPlasma(double time)
    {
        if (_bitmap == null) return;

        using var ctx = _bitmap.LockForWrite();

        int w = ctx.Width;
        int h = ctx.Height;
        var pixels = ctx.PixelsUInt32;

        double scaleX = 8.0 / w;
        double scaleY = 8.0 / h;

        for (int y = 0; y < h; y++)
        {
            double dy = y * scaleY;
            int rowOffset = y * w;

            for (int x = 0; x < w; x++)
            {
                double dx = x * scaleX;

                // Plasma function - combination of sine waves
                double v1 = FastSin(dx + time);
                double v2 = FastSin((dy + time) * 0.5);
                double v3 = FastSin((dx + dy + time) * 0.5);
                double dist = Math.Sqrt((dx - 4) * (dx - 4) + (dy - 4) * (dy - 4));
                double v4 = FastSin(dist + time);

                double v = (v1 + v2 + v3 + v4) / 4.0;

                // Map to color
                byte r = (byte)((FastSin(v * Math.PI + time) + 1) * 127);
                byte g = (byte)((FastSin(v * Math.PI + time + 2.094) + 1) * 127);
                byte b = (byte)((FastSin(v * Math.PI + time + 4.189) + 1) * 127);

                // Pack as BGRA
                pixels[rowOffset + x] = (uint)(b | (g << 8) | (r << 16) | (255 << 24));
            }
        }
    }

    private static double FastSin(double x)
    {
        // Normalize to 0-TableSize range
        double normalized = (x % (Math.PI * 2)) / (Math.PI * 2);
        if (normalized < 0) normalized += 1.0;

        int index = (int)(normalized * TableSize) & (TableSize - 1);
        return SinTable[index];
    }

    private void StartAnimation()
    {
        if (_timer != null) return;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16)); // ~60 FPS
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void StopAnimation()
    {
        if (_timer == null) return;

        _timer.Stop();
        _timer.Dispose();
        _timer = null;
    }

    private void OnTimerTick()
    {
        _time += 0.05 * Speed;
        RenderPlasma(_time);
        InvalidateVisual();
    }

    protected override void OnDispose()
    {
        StopAnimation();
        _image?.Dispose();
        _image = null;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDispose();
    }
}

public static class PlasmaEffectExtensions
{
    public static PlasmaEffect Speed(this PlasmaEffect plasma, double speed)
    {
        plasma.Speed = speed;
        return plasma;
    }

    public static PlasmaEffect Animating(this PlasmaEffect plasma, bool isAnimating = true)
    {
        plasma.IsAnimating = isAnimating;
        return plasma;
    }
}
