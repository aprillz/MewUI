using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewCharts.Views;

public abstract partial class ChartViewBase
{
    /// <summary>
    /// Writes the chart's current frame as a PNG image of its laid-out size, at the window DPI, to <paramref name="stream"/>.
    /// </summary>
    /// <remarks>Call it after the chart is loaded and laid out; an unloaded or zero-sized chart throws <see cref="InvalidOperationException"/>.</remarks>
    public void SaveImage(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (!_loaded || width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The chart must be loaded and laid out before an image can be built from it.");
        }

        var dpiScale = FindVisualRoot() is Window window ? window.DpiScale : 1.0;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * dpiScale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * dpiScale));
        var factory = GetGraphicsFactory();

        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.CpuPixels(pixelWidth, pixelHeight, dpiScale, premultiplied: false, debugName: "MewChartsImage"));

        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            try
            {
                var localBounds = new Rect(0, 0, width, height);
                var background = Background.A > 0
                    ? Background
                    : MewPaint.ToMewColor(CoreChart.GetTheme().VirtualBackroundColor);
                context.FillRectangle(localBounds, background);

                MewChartsText.FontFamily = FontFamily;
                MewChartsText.FontScale = FontSize > 0 ? FontSize / 16.0 : 1;

                var drawingContext = new MewDrawingContext(CoreCanvas, context, background, factory)
                {
                    DrawArea = localBounds,
                };
                CoreCanvas.DrawFrame(drawingContext);
            }
            finally
            {
                context.EndFrame();
            }
        }

        using (var readback = factory.RequestReadback(surface))
        {
            readback.Wait();
        }

        var stride = pixelWidth * 4;
        var pixels = new byte[stride * pixelHeight];
        if (!factory.TryReadPixels(surface, pixels, stride))
        {
            throw new NotSupportedException("The graphics backend cannot read back the chart image.");
        }

        var premultiplied = surface.Format is RenderPixelFormat.Bgra8888Premultiplied or RenderPixelFormat.Rgba8888Premultiplied;
        PngWriter.Write(stream, pixels, pixelWidth, pixelHeight, stride, premultiplied);
    }

    /// <summary>Writes the chart's current frame as a PNG file at <paramref name="path"/>, replacing an existing file.</summary>
    /// <remarks>See <see cref="SaveImage(Stream)"/>.</remarks>
    public void SaveImage(string path)
    {
        using var stream = File.Create(path);
        SaveImage(stream);
    }
}
