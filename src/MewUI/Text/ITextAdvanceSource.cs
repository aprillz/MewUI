using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

/// <summary>
/// Backend-internal source of cumulative UTF-16 prefix advances. Values must come from the same
/// metric path used to position text during drawing, letter spacing included.
/// </summary>
internal interface ITextAdvanceSource
{
    double[] GetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font, double letterSpacing);

    /// <summary>Writes the same advances into a caller-owned span; false when the source cannot fill it without allocating.</summary>
    bool TryGetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font, double letterSpacing, Span<double> destination) => false;
}
