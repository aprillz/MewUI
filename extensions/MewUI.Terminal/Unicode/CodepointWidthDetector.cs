// Ported from Windows Terminal src/types/CodepointWidthDetector.cpp and src/types/inc/CodepointWidthDetector.hpp (MIT).

namespace Aprillz.MewUI.Terminal.Unicode;

internal enum TextMeasurementMode
{
    // Extended grapheme clusters per UAX #29, with a pairwise join table.
    Graphemes,

    // A character with width followed by any zero-width characters, as wcswidth-based terminals count.
    Wcswidth,

    // One codepoint per cluster, with ambiguous widths asked of the renderer, as the old console did.
    Console,
}

/// <summary>
/// Where a grapheme walk stands: the cluster found last (<see cref="Beg"/>, <see cref="Len"/>, <see cref="Width"/>) and
/// the join state that carries a cluster across the end of one string into the next.
/// </summary>
/// <remarks>
/// <see cref="Beg"/> is an offset into the string last walked. The default value and <see cref="Detach"/> mean "not in
/// the next string", which makes the next walk start at that string's beginning (or end, walking backwards) while
/// keeping the join state; this stands in for the original's pointer comparison against the new string.
/// </remarks>
internal struct GraphemeState
{
    private int _begPlusOne;

    public int Len;
    public int Width;
    internal int State;
    internal int Last;

    public GraphemeState(int beg)
    {
        _begPlusOne = beg + 1;
    }

    public int Beg
    {
        readonly get => _begPlusOne - 1;
        set => _begPlusOne = value + 1;
    }

    public readonly bool IsDetached => _begPlusOne == 0;

    /// <summary>Lets the next walk continue the current cluster in a different string.</summary>
    public void Detach()
    {
        _begPlusOne = 0;
        Len = 0;
    }
}

/// <summary>
/// Splits UTF-16 text into grapheme clusters and gives each a width of 0, 1 or 2 cells, from tables generated out of
/// Unicode 16.0.0.
/// </summary>
internal sealed partial class CodepointWidthDetector
{
    private readonly Dictionary<int, int> _fallbackCache = new();
    private Func<string, bool>? _fallbackMethod;
    private TextMeasurementMode _mode = TextMeasurementMode.Graphemes;
    private int _ambiguousWidth = 1;

    public static CodepointWidthDetector Singleton { get; } = new();

    public TextMeasurementMode Mode => _mode;

    public int AmbiguousWidth
    {
        get => _ambiguousWidth;
        set => _ambiguousWidth = value;
    }

    /// <summary>Asks whether an ambiguous-width character draws wide, for <see cref="TextMeasurementMode.Console"/>.</summary>
    public void SetFallbackMethod(Func<string, bool>? fallback) => _fallbackMethod = fallback;

    public void Reset(TextMeasurementMode mode)
    {
        _mode = mode;
        _fallbackCache.Clear();
    }

    /// <summary>
    /// Finds the cluster after the one in <paramref name="state"/>. Returns false when the string ends, which may leave
    /// the cluster open for a following string.
    /// </summary>
    public bool GraphemeNext(ref GraphemeState state, ReadOnlySpan<char> text)
    {
        if (_mode == TextMeasurementMode.Graphemes)
        {
            return GraphemeNextCore(ref state, text);
        }

        if (_mode == TextMeasurementMode.Wcswidth)
        {
            return GraphemeNextWcswidth(ref state, text);
        }

        return GraphemeNextConsole(ref state, text);
    }

    /// <summary>Finds the cluster before the one in <paramref name="state"/>; the mirror of <see cref="GraphemeNext"/>.</summary>
    public bool GraphemePrev(ref GraphemeState state, ReadOnlySpan<char> text)
    {
        if (_mode == TextMeasurementMode.Graphemes)
        {
            return GraphemePrevCore(ref state, text);
        }

        if (_mode == TextMeasurementMode.Wcswidth)
        {
            return GraphemePrevWcswidth(ref state, text);
        }

        return GraphemePrevConsole(ref state, text);
    }

    private static int UcdLookup(int codepoint)
    {
        int stage0 = Stage0[codepoint >> 11];
        int stage1 = Stage1[stage0 + ((codepoint >> 6) & 31)];
        int stage2 = Stage2[stage1 + ((codepoint >> 3) & 7)];
        return Stage3[stage2 + (codepoint & 7)];
    }

    private static int UcdGraphemeJoins(int state, int lead, int trail)
    {
        int leadIndex = lead & 15;
        int trailIndex = trail & 15;
        return (int)((JoinRules[state * 16 + leadIndex] >> (trailIndex * 2)) & 3);
    }

    private static bool UcdGraphemeDone(int state) => state == 3;

    private static int UcdToCharacterWidth(int value) => value >> 6;

    /// <summary>Decodes the codepoint at <paramref name="index"/>; a lone surrogate reads as U+FFFD.</summary>
    private static int Utf16NextOrFffd(ReadOnlySpan<char> text, int index, out int codepoint)
    {
        int unit = text[index++];
        if ((unit & 0xF800) == 0xD800)
        {
            int lead = unit;
            unit = 0xFFFD;
            if ((lead & 0x400) == 0 && index != text.Length)
            {
                int trail = text[index];
                if ((trail & 0xFC00) == 0xDC00)
                {
                    unit = (lead << 10) - 0x35FDC00 + trail;
                    index++;
                }
            }
        }

        codepoint = unit;
        return index;
    }

    /// <summary>Decodes the codepoint ending before <paramref name="index"/>; a lone surrogate reads as U+FFFD.</summary>
    private static int Utf16PrevOrFffd(ReadOnlySpan<char> text, int index, out int codepoint)
    {
        int unit = text[--index];
        if ((unit & 0xF800) == 0xD800)
        {
            int trail = unit;
            unit = 0xFFFD;
            if ((trail & 0x400) != 0 && index != 0)
            {
                int lead = text[index - 1];
                if ((lead & 0xFC00) == 0xD800)
                {
                    unit = (lead << 10) - 0x35FDC00 + trail;
                    index--;
                }
            }
        }

        codepoint = unit;
        return index;
    }

    private static int ClusterStartForNext(in GraphemeState state, int length)
    {
        if (state.IsDetached)
        {
            return 0;
        }

        int start = state.Beg + state.Len;
        return start < 0 || start > length ? 0 : start;
    }

    private static int ClusterEndForPrev(in GraphemeState state, int length)
    {
        if (state.IsDetached)
        {
            return length;
        }

        int end = state.Beg;
        return end < 0 || end > length ? length : end;
    }

    private bool GraphemeNextCore(ref GraphemeState state, ReadOnlySpan<char> text)
    {
        int end = text.Length;
        int clusterBeg = ClusterStartForNext(state, end);
        int width = state.Width;
        int joinState = state.State;
        int lead = state.Last;
        int clusterEnd = clusterBeg;

        if (clusterEnd >= end)
        {
            return false;
        }

        int codepoint = 0;

        // A stored state is kept bit-flipped, so that zero can mean "none stored".
        bool resumed = joinState != 0;
        joinState = ~joinState;
        if (!resumed)
        {
            clusterEnd = Utf16NextOrFffd(text, clusterEnd, out codepoint);
            lead = UcdLookup(codepoint);
            width = 0;
            joinState = 0;
        }

        while (true)
        {
            if (!resumed)
            {
                width += CharacterWidth(lead, codepoint);
                if (clusterEnd >= end)
                {
                    break;
                }
            }

            resumed = false;
            int clusterEndNext = Utf16NextOrFffd(text, clusterEnd, out codepoint);
            int trail = UcdLookup(codepoint);

            joinState = UcdGraphemeJoins(joinState, lead, trail);
            if (UcdGraphemeDone(joinState))
            {
                joinState = ~0;
                lead = 0;
                break;
            }

            clusterEnd = clusterEndNext;
            lead = trail;
        }

        joinState = ~joinState;
        width = width > 2 ? 2 : width;

        state.Beg = clusterBeg;
        state.Len = clusterEnd - clusterBeg;
        state.Width = width;
        state.State = joinState;
        state.Last = lead;
        return clusterEnd < end;
    }

    private bool GraphemePrevCore(ref GraphemeState state, ReadOnlySpan<char> text)
    {
        int clusterEnd = ClusterEndForPrev(state, text.Length);
        int width = state.Width;
        int joinState = state.State;
        int trail = state.Last;
        int clusterBeg = clusterEnd;

        if (clusterEnd <= 0)
        {
            return false;
        }

        int codepoint = 0;
        bool resumed = joinState != 0;
        joinState = ~joinState;
        if (!resumed)
        {
            clusterBeg = Utf16PrevOrFffd(text, clusterBeg, out codepoint);
            trail = UcdLookup(codepoint);
            width = 0;
            joinState = 0;
        }

        while (true)
        {
            if (!resumed)
            {
                width += CharacterWidth(trail, codepoint);
                if (clusterBeg <= 0)
                {
                    break;
                }
            }

            resumed = false;
            int clusterBegNext = Utf16PrevOrFffd(text, clusterBeg, out codepoint);
            int lead = UcdLookup(codepoint);

            joinState = UcdGraphemeJoins(joinState, lead, trail);
            if (UcdGraphemeDone(joinState))
            {
                joinState = ~0;
                trail = 0;
                break;
            }

            clusterBeg = clusterBegNext;
            trail = lead;
        }

        joinState = ~joinState;
        width = width > 2 ? 2 : width;

        state.Beg = clusterBeg;
        state.Len = clusterEnd - clusterBeg;
        state.Width = width;
        state.State = joinState;
        state.Last = trail;
        return clusterBeg > 0;
    }

    private int CharacterWidth(int value, int codepoint)
    {
        int width = UcdToCharacterWidth(value);
        if (width == 3)
        {
            width = _ambiguousWidth;
        }

        // VS16 turns an unqualified emoji into a qualified, wide one; the cluster width is clamped to 2 later.
        if (codepoint == 0xFE0F)
        {
            width = 2;
        }

        return width;
    }

    private bool GraphemeNextWcswidth(ref GraphemeState state, ReadOnlySpan<char> text)
    {
        int end = text.Length;
        int clusterBeg = ClusterStartForNext(state, end);
        int width = state.Width;
        int leadSeen = state.State;

        if (clusterBeg >= end)
        {
            return false;
        }

        int clusterEnd = clusterBeg;

        // A zero-width character may lead a cluster, so whether a lead was seen is tracked apart from the width.
        if (leadSeen == 0)
        {
            width = 0;
        }

        while (true)
        {
            int clusterEndNext = Utf16NextOrFffd(text, clusterEnd, out int codepoint);
            int characterWidth = UcdToCharacterWidth(UcdLookup(codepoint));
            if (characterWidth == 3)
            {
                characterWidth = _ambiguousWidth;
            }

            if (leadSeen != 0 && characterWidth != 0)
            {
                leadSeen = 0;
                break;
            }

            width += characterWidth;
            leadSeen = 1;
            clusterEnd = clusterEndNext;

            if (clusterEnd >= end)
            {
                break;
            }
        }

        state.Beg = clusterBeg;
        state.Len = clusterEnd - clusterBeg;
        state.Width = width;
        state.State = leadSeen;
        return clusterEnd < end;
    }

    private bool GraphemePrevWcswidth(ref GraphemeState state, ReadOnlySpan<char> text)
    {
        int clusterEnd = ClusterEndForPrev(state, text.Length);
        if (clusterEnd <= 0)
        {
            return false;
        }

        int clusterBeg = clusterEnd;
        int width = state.Width;
        int delayedCompletion = 0;

        // Like the grapheme walk, a cluster reaching the start stays open until the next call returns an empty one.
        if (state.State == 0)
        {
            width = 0;
            while (true)
            {
                clusterBeg = Utf16PrevOrFffd(text, clusterBeg, out int codepoint);
                int characterWidth = UcdToCharacterWidth(UcdLookup(codepoint));
                if (characterWidth == 3)
                {
                    characterWidth = _ambiguousWidth;
                }

                width += characterWidth;

                bool hasWidth = width != 0;
                bool atStart = clusterBeg <= 0;
                if (hasWidth || atStart)
                {
                    delayedCompletion = hasWidth && atStart ? 1 : 0;
                    break;
                }
            }
        }

        state.Beg = clusterBeg;
        state.Len = clusterEnd - clusterBeg;
        state.Width = width;
        state.State = delayedCompletion;
        return clusterBeg > 0;
    }

    private bool GraphemeNextConsole(ref GraphemeState state, ReadOnlySpan<char> text)
    {
        int end = text.Length;
        int clusterBeg = ClusterStartForNext(state, end);
        if (clusterBeg >= end)
        {
            return false;
        }

        int clusterEnd = clusterBeg;
        int width = state.Width;
        int delayedCompletion = 0;

        if (state.State == 0)
        {
            clusterEnd = Utf16NextOrFffd(text, clusterEnd, out int codepoint);
            width = UcdToCharacterWidth(UcdLookup(codepoint));
            if (width == 3)
            {
                width = CheckFallbackViaCache(codepoint);
            }

            delayedCompletion = clusterEnd >= end ? 1 : 0;
        }

        state.Beg = clusterBeg;
        state.Len = clusterEnd - clusterBeg;
        state.Width = width;
        state.State = delayedCompletion;
        return delayedCompletion == 0;
    }

    private bool GraphemePrevConsole(ref GraphemeState state, ReadOnlySpan<char> text)
    {
        int clusterEnd = ClusterEndForPrev(state, text.Length);
        if (clusterEnd <= 0)
        {
            return false;
        }

        int clusterBeg = clusterEnd;
        int width = state.Width;
        int delayedCompletion = 0;

        if (state.State == 0)
        {
            clusterBeg = Utf16PrevOrFffd(text, clusterEnd, out int codepoint);
            width = UcdToCharacterWidth(UcdLookup(codepoint));
            if (width == 3)
            {
                width = CheckFallbackViaCache(codepoint);
            }

            delayedCompletion = clusterBeg <= 0 ? 1 : 0;
        }

        state.Beg = clusterBeg;
        state.Len = clusterEnd - clusterBeg;
        state.Width = width;
        state.State = delayedCompletion;
        return delayedCompletion == 0;
    }

    /// <summary>Turns an ambiguous width into 1 or 2 through the fallback, narrow when there is none.</summary>
    private int CheckFallbackViaCache(int codepoint)
    {
        if (_fallbackMethod == null)
        {
            return 1;
        }

        if (_fallbackCache.TryGetValue(codepoint, out int cached))
        {
            return cached;
        }

        int width = _fallbackMethod(char.ConvertFromUtf32(codepoint)) ? 2 : 1;
        _fallbackCache[codepoint] = width;
        return width;
    }
}
