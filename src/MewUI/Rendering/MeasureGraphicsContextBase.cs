using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Backend-private base for text measurement sessions. Measurement sessions are not frame
/// graphics contexts and deliberately expose no drawing surface.
/// </summary>
internal abstract class MeasureGraphicsContextBase : ITextBackendMeasurementContext
{
    public abstract double DpiScale { get; }

    public abstract Size MeasureText(ReadOnlySpan<char> text, IFont font);

    public abstract Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth);

    bool ITextBackendMeasurementContext.SupportsUtf16PrefixAdvances => this is ITextAdvanceSource;

    Size ITextBackendMeasurementContext.Measure(ReadOnlySpan<char> text, IFont font)
        => MeasureText(text, font);

    double[]? ITextBackendMeasurementContext.GetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font)
        => this is ITextAdvanceSource source ? source.GetUtf16PrefixAdvances(text, font) : null;

    bool ITextBackendMeasurementContext.TryGetUtf16PrefixAdvances(
        ReadOnlySpan<char> text, IFont font, Span<double> destination)
        => this is ITextAdvanceSource source && source.TryGetUtf16PrefixAdvances(text, font, destination);

    public virtual void Dispose()
    {
    }
}
