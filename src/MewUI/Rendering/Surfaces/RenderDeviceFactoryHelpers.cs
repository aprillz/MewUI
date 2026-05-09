using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

internal static class RenderDeviceFactoryHelpers
{
    public static IGraphicsContext CreateContext(IGraphicsFactory factory, IRenderSurface surface)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (surface is IBitmapRenderTarget bitmapTarget)
        {
            return factory.CreateContext(bitmapTarget);
        }

        throw new NotSupportedException(
            $"{factory.GetType().Name} can only create contexts for bitmap-backed render surfaces.");
    }

    public static IImage CreateImageView(IGraphicsFactory factory, IRenderSurface surface)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (surface is IBitmapRenderTarget bitmapTarget)
        {
            return factory.CreateImageView((IPixelBufferSource)bitmapTarget);
        }

        throw new NotSupportedException(
            $"{factory.GetType().Name} can only create image views for pixel-backed surfaces.");
    }

    public static bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
    {
        if (source is not ICpuPixelSurface cpuSurface)
        {
            return false;
        }

        int rowBytes = checked(cpuSurface.PixelWidth * 4);
        if (destinationStrideBytes < rowBytes)
        {
            return false;
        }

        int requiredBytes = checked(destinationStrideBytes * Math.Max(0, cpuSurface.PixelHeight - 1) + rowBytes);
        if (destination.Length < requiredBytes)
        {
            return false;
        }

        ReadOnlySpan<byte> sourcePixels = cpuSurface.GetReadOnlyPixelSpan();
        if (sourcePixels.Length < checked(cpuSurface.StrideBytes * Math.Max(0, cpuSurface.PixelHeight - 1) + rowBytes))
        {
            return false;
        }

        for (int y = 0; y < cpuSurface.PixelHeight; y++)
        {
            var sourceRow = sourcePixels.Slice(y * cpuSurface.StrideBytes, rowBytes);
            var destRow = destination.Slice(y * destinationStrideBytes, rowBytes);
            sourceRow.CopyTo(destRow);
        }

        return true;
    }

    public static IRenderOperation RequestReadback(IRenderSurface source)
    {
        return source is IDeferredCpuReadableSurface deferred
            ? deferred.RequestReadback()
            : RenderOperation.Completed;
    }

    public static bool RequiresCpuBitmap(RenderSurfaceDescriptor descriptor)
    {
        var caps = descriptor.RequiredCapabilities;
        return caps.HasFlag(SurfaceCapabilities.CpuWritable)
            || (caps.HasFlag(SurfaceCapabilities.CpuReadable)
                && !caps.HasFlag(SurfaceCapabilities.GpuSampleable));
    }
}
