using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// Extracts the cumulative UTF-16 prefix extents used by the GDI text path.
/// Cluster normalization is intentionally left to the managed text engine.
/// </summary>
internal static class GdiTextAdvances
{
    public static unsafe double[] GetUtf16PrefixAdvances(
        nint hdc,
        GdiFont font,
        ReadOnlySpan<char> text,
        double dpiScale)
    {
        if (text.IsEmpty)
        {
            return [];
        }

        var advances = GC.AllocateUninitializedArray<double>(text.Length);
        Fill(hdc, font, text, dpiScale, advances);
        return advances;
    }

    /// <summary>Writes the prefix advances into a caller-owned span of at least one entry per code unit.</summary>
    public static unsafe void GetUtf16PrefixAdvances(
        nint hdc,
        GdiFont font,
        ReadOnlySpan<char> text,
        double dpiScale,
        Span<double> destination)
    {
        if (!text.IsEmpty)
        {
            Fill(hdc, font, text, dpiScale, destination);
        }
    }

    private static unsafe void Fill(
        nint hdc,
        GdiFont font,
        ReadOnlySpan<char> text,
        double dpiScale,
        Span<double> advances)
    {
        fixed (char* textPointer = text)
        fixed (double* advancesPointer = advances)
        {
            int* cumulativePixels = (int*)advancesPointer;
            var oldFont = Gdi32.SelectObject(hdc, font.Handle);
            try
            {
                SIZE size;
                if (!Gdi32.GetTextExtentExPoint(
                    hdc,
                    textPointer,
                    text.Length,
                    int.MaxValue,
                    null,
                    cumulativePixels,
                    &size))
                {
                    throw new InvalidOperationException("GetTextExtentExPointW failed.");
                }
            }
            finally
            {
                Gdi32.SelectObject(hdc, oldFont);
            }

            double scale = dpiScale > 0 ? dpiScale : 1;
            // The native int output occupies the first half of the double buffer. Convert from
            // the end so writing a double never overwrites an int that has not been read yet.
            for (int i = text.Length - 1; i >= 0; i--)
            {
                advancesPointer[i] = cumulativePixels[i] / scale;
            }
        }
    }
}
