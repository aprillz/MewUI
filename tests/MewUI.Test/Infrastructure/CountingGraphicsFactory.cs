using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Infrastructure;

/// <summary>
/// Delegating factory that counts the text measurement and layout calls a backend receives, so
/// tests can assert on the work a control asks of the engine.
/// </summary>
internal sealed class CountingGraphicsFactory(IGraphicsFactory inner) : IGraphicsFactory, ITextBackendFactory
{
    public int MeasureTextCalls;
    public int CreateTextLayoutCalls;

    public void Reset()
    {
        MeasureTextCalls = 0;
        CreateTextLayoutCalls = 0;
    }

    public string Backend => inner.Backend;
    public RenderDeviceIdentity RenderIdentity => inner.RenderIdentity;

    ITextBackendMeasurementContext ITextBackendFactory.CreateTextMeasurementContext(uint dpi)
    {
        if (inner is not ITextBackendFactory backendFactory)
        {
            throw new NotSupportedException($"{inner.GetType().Name} has no text measurement backend.");
        }

        return new CountingContext(backendFactory.CreateTextMeasurementContext(dpi), this);
    }

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
        => inner.CreateFont(family, size, weight, italic, underline, strikethrough);

    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
        => inner.CreateFont(family, size, dpi, weight, italic, underline, strikethrough);

    public IImage CreateImageFromFile(string path) => inner.CreateImageFromFile(path);
    public IImage CreateImageFromBytes(byte[] data) => inner.CreateImageFromBytes(data);
    public IGraphicsContext CreateContext(IRenderTarget target) => inner.CreateContext(target);
    public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor) => inner.CreateSurface(descriptor);
    public IGraphicsContext CreateContext(IRenderSurface surface) => inner.CreateContext(surface);
    public IImage CreateImageView(IRenderSurface surface) => inner.CreateImageView(surface);
    public IImage CreateImageView(IPixelBufferSource source) => inner.CreateImageView(source);
    public IImage CreateImageView(IExternalRasterSource source) => inner.CreateImageView(source);

    public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
        => inner.TryReadPixels(source, destination, destinationStrideBytes);

    public IRenderOperation RequestReadback(IRenderSurface source) => inner.RequestReadback(source);
    public IRenderOperation FlushAsyncWork() => inner.FlushAsyncWork();
    public IRenderResourceCache? ResourceCache => inner.ResourceCache;
    public IRenderEffectDevice? Effects => inner.Effects;

    public void Dispose() { }

    // Forwards ITextAdvanceSource so the engine takes the same path a real backend context does.
    private sealed class CountingContext(ITextBackendMeasurementContext inner, CountingGraphicsFactory owner)
        : ITextBackendMeasurementContext
    {
        public bool SupportsUtf16PrefixAdvances => inner.SupportsUtf16PrefixAdvances;

        public Size Measure(ReadOnlySpan<char> text, IFont font)
        {
            owner.MeasureTextCalls++;
            return inner.Measure(text, font);
        }

        public double[]? GetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font)
            => inner.GetUtf16PrefixAdvances(text, font);

        public void Dispose() => inner.Dispose();
    }
}
