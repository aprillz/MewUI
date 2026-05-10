namespace Aprillz.MewUI.Rendering;

public interface ICpuPixelSurface : IRenderSurface
{
    int StrideBytes { get; }

    ReadOnlySpan<byte> GetReadOnlyPixelSpan();

    Span<byte> GetWritablePixelSpan();

    byte[] CopyPixels();

    void IncrementVersion();

    void Clear(Color color)
    {
        var span = GetWritablePixelSpan();
        byte a = color.A;
        bool premultiply = Capabilities.HasFlag(SurfaceCapabilities.Premultiplied);
        byte b = premultiply ? (byte)((color.B * a + 127) / 255) : color.B;
        byte g = premultiply ? (byte)((color.G * a + 127) / 255) : color.G;
        byte r = premultiply ? (byte)((color.R * a + 127) / 255) : color.R;
        for (int i = 0; i + 3 < span.Length; i += 4)
        {
            span[i + 0] = b;
            span[i + 1] = g;
            span[i + 2] = r;
            span[i + 3] = a;
        }
    }
}

public interface IGpuSampleableSurface : IRenderSurface
{
    bool YFlipped { get; }

    IDisposable RetainSampleHandle();
}

public interface INativeRenderSurface : IRenderSurface
{
    nint NativeHandle { get; }
}

public interface IDeferredCpuReadableSurface : IRenderSurface
{
    bool HasPendingReadback { get; }

    IRenderOperation RequestReadback();

    bool TryFlushReadback();
}
