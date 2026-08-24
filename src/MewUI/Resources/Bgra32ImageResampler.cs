namespace Aprillz.MewUI.Resources;

internal static class Bgra32ImageResampler
{
    public static Bgra32PixelBuffer FitWithin(Bgra32PixelBuffer source, int maxWidth, int maxHeight)
    {
        maxWidth = Math.Max(1, maxWidth);
        maxHeight = Math.Max(1, maxHeight);
        if (source.WidthPx <= maxWidth && source.HeightPx <= maxHeight)
        {
            return source;
        }

        double scale = Math.Min((double)maxWidth / source.WidthPx, (double)maxHeight / source.HeightPx);
        int width = Math.Max(1, (int)Math.Round(source.WidthPx * scale));
        int height = Math.Max(1, (int)Math.Round(source.HeightPx * scale));
        return Resize(source, width, height);
    }

    public static Bgra32PixelBuffer Resize(Bgra32PixelBuffer source, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (source.WidthPx == width && source.HeightPx == height)
        {
            return source;
        }

        byte[] destination = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        double scaleX = (double)source.WidthPx / width;
        double scaleY = (double)source.HeightPx / height;
        for (int y = 0; y < height; y++)
        {
            double sourceY = (y + 0.5) * scaleY - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, source.HeightPx - 1);
            int y1 = Math.Min(y0 + 1, source.HeightPx - 1);
            double fy = Math.Clamp(sourceY - y0, 0, 1);
            for (int x = 0; x < width; x++)
            {
                double sourceX = (x + 0.5) * scaleX - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, source.WidthPx - 1);
                int x1 = Math.Min(x0 + 1, source.WidthPx - 1);
                double fx = Math.Clamp(sourceX - x0, 0, 1);
                int p00 = (y0 * source.WidthPx + x0) * 4;
                int p10 = (y0 * source.WidthPx + x1) * 4;
                int p01 = (y1 * source.WidthPx + x0) * 4;
                int p11 = (y1 * source.WidthPx + x1) * 4;
                int target = (y * width + x) * 4;
                double alphaTop = source.Data[p00 + 3] * (1 - fx) + source.Data[p10 + 3] * fx;
                double alphaBottom = source.Data[p01 + 3] * (1 - fx) + source.Data[p11 + 3] * fx;
                double alpha = alphaTop * (1 - fy) + alphaBottom * fy;
                destination[target + 3] = (byte)Math.Clamp((int)Math.Round(alpha), 0, 255);

                for (int channel = 0; channel < 3; channel++)
                {
                    double p00Premultiplied = source.Data[p00 + channel] * source.Data[p00 + 3] / 255.0;
                    double p10Premultiplied = source.Data[p10 + channel] * source.Data[p10 + 3] / 255.0;
                    double p01Premultiplied = source.Data[p01 + channel] * source.Data[p01 + 3] / 255.0;
                    double p11Premultiplied = source.Data[p11 + channel] * source.Data[p11 + 3] / 255.0;
                    double top = p00Premultiplied * (1 - fx) + p10Premultiplied * fx;
                    double bottom = p01Premultiplied * (1 - fx) + p11Premultiplied * fx;
                    double premultiplied = top * (1 - fy) + bottom * fy;
                    int straight = alpha <= 0 ? 0 : (int)Math.Round(premultiplied * 255 / alpha);
                    destination[target + channel] = (byte)Math.Clamp(straight, 0, 255);
                }
            }
        }

        return new Bgra32PixelBuffer(width, height, destination, source.HasAlpha);
    }
}
