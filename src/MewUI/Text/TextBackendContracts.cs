using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

/// <summary>Backend-private entry point for text measurement sessions.</summary>
internal interface ITextBackendFactory
{
    ITextBackendMeasurementContext CreateTextMeasurementContext(uint dpi);
}

/// <summary>Backend-private measurement surface consumed by the managed text engine.</summary>
internal interface ITextBackendMeasurementContext : IDisposable
{
    bool SupportsUtf16PrefixAdvances { get; }

    Size Measure(ReadOnlySpan<char> text, IFont font);

    double GetRasterBaseline(IFont font);

    double[]? GetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font);

    /// <summary>
    /// Writes the advance of every UTF-16 prefix of the text into <paramref name="destination"/>,
    /// which has to hold one entry per code unit. False when this context measures no advances, or
    /// when it can only answer by allocating, in which case the caller falls back to the array form.
    /// </summary>
    bool TryGetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font, Span<double> destination)
        => false;
}

/// <summary>Glyph ink that extends past a run's advance box and line box, in DIP; never negative.</summary>
internal readonly record struct TextInkOverhang(double Left, double Top, double Right, double Bottom)
{
    public static TextInkOverhang None => default;

    /// <summary>Clamps each side to zero so a run box never shrinks below its layout geometry.</summary>
    public static TextInkOverhang FromEdges(double left, double top, double right, double bottom)
        => new(Math.Max(0, left), Math.Max(0, top), Math.Max(0, right), Math.Max(0, bottom));
}

/// <summary>Opaque backend realization of one positioned text run.</summary>
internal interface ITextBackendRun : IDisposable
{
    /// <summary>Native handle exposed only to lifetime diagnostics; zero when the backend has none.</summary>
    nint NativeHandle { get; }

    /// <summary>Ink the backend draws outside the run box; the run box itself stays layout geometry.</summary>
    TextInkOverhang Ink { get; }
}

/// <summary>
/// Owner identity handed to <see cref="ITextBackendRenderContext.DrawRun"/> for transient text:
/// the backend draws through its per-frame scratch textures and keeps nothing in its text cache.
/// </summary>
internal static class TransientText
{
    public static readonly object Owner = new();
}

/// <summary>Backend-private run realization and drawing surface.</summary>
internal interface ITextBackendRenderContext
{
    ITextBackendRun? CreateRun(ReadOnlySpan<char> text, IFont font, double width, double height);

    void DrawRun(ITextBackendRun run, Point origin, Color color, object? owner);
}
