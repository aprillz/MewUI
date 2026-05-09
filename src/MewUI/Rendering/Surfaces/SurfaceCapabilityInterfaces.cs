namespace Aprillz.MewUI.Rendering;

public interface ICpuPixelSurface : IRenderSurface
{
    int StrideBytes { get; }

    ReadOnlySpan<byte> GetReadOnlyPixelSpan();

    Span<byte> GetWritablePixelSpan();

    byte[] CopyPixels();

    void IncrementVersion();
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
