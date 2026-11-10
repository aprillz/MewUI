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
        double dpiScale,
        double letterSpacing = 0)
    {
        if (text.IsEmpty)
        {
            return [];
        }

        var advances = GC.AllocateUninitializedArray<double>(text.Length);
        Fill(hdc, font, text, dpiScale, letterSpacing, advances);
        return advances;
    }

    /// <summary>Writes the prefix advances into a caller-owned span of at least one entry per code unit.</summary>
    public static unsafe void GetUtf16PrefixAdvances(
        nint hdc,
        GdiFont font,
        ReadOnlySpan<char> text,
        double dpiScale,
        double letterSpacing,
        Span<double> destination)
    {
        if (!text.IsEmpty)
        {
            Fill(hdc, font, text, dpiScale, letterSpacing, destination);
        }
    }

    /// <summary>
    /// Letter spacing as the whole device pixels GDI adds after every character; measuring and drawing
    /// both take this value, so they stay in step.
    /// </summary>
    public static int ToCharacterExtraPx(double letterSpacing, double dpiScale)
        => (int)Math.Round(letterSpacing * (dpiScale > 0 ? dpiScale : 1), MidpointRounding.AwayFromZero);

    /// <summary>Draws text with <paramref name="characterExtraPx"/> pixels after every character.</summary>
    public static unsafe int DrawText(nint hdc, char* text, int length, ref RECT rect, uint format, int characterExtraPx)
    {
        if (characterExtraPx == 0)
        {
            return Gdi32.DrawText(hdc, text, length, ref rect, format);
        }

        int previousExtra = Gdi32.SetTextCharacterExtra(hdc, characterExtraPx);
        try
        {
            return Gdi32.DrawText(hdc, text, length, ref rect, format);
        }
        finally
        {
            Gdi32.SetTextCharacterExtra(hdc, previousExtra);
        }
    }

    private static unsafe void Fill(
        nint hdc,
        GdiFont font,
        ReadOnlySpan<char> text,
        double dpiScale,
        double letterSpacing,
        Span<double> advances)
    {
        int characterExtraPx = ToCharacterExtraPx(letterSpacing, dpiScale);
        fixed (char* textPointer = text)
        fixed (double* advancesPointer = advances)
        {
            int* cumulativePixels = (int*)advancesPointer;
            var oldFont = Gdi32.SelectObject(hdc, font.Handle);
            int previousExtra = characterExtraPx != 0 ? Gdi32.SetTextCharacterExtra(hdc, characterExtraPx) : 0;
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
                if (characterExtraPx != 0)
                {
                    Gdi32.SetTextCharacterExtra(hdc, previousExtra);
                }

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
