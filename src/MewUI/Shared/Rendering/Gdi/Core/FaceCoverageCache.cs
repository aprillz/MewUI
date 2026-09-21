using Aprillz.MewUI.Rendering.Win32;

namespace Aprillz.MewUI.Rendering.Gdi.Core;

/// <summary>
/// Keeps the per-channel coverage a text face rasterized, so drawing the same run again only blends it.
/// A face that rasterizes itself lays the run out, analyses its glyphs and renders them on every call,
/// and this backend has no texture to keep the result in. Coverage carries no colour, so one entry
/// serves the run in every colour. A run is kept from the second time it is drawn: text that is new
/// every frame, a counter or a clock, would only pay for a copy that nothing reads.
/// Shared by every context of the process and safe to use from any thread.
/// </summary>
internal static class FaceCoverageCache
{
    private const long MAX_BYTES = 8L * 1024 * 1024;

    // A run this large is rare and would push many small ones out.
    private const int MAX_ENTRY_BYTES = 1024 * 1024;
    private const int SEEN_SLOTS = 2048;

    private static readonly object _gate = new();
    private static readonly Dictionary<Key, LinkedListNode<Entry>> _map = new();
    private static readonly LinkedList<Entry> _leastRecentFirst = new();
    private static long _bytes;

    // Hash of the last run seen in each slot; a run found here has been drawn before.
    private static readonly int[] _seenOnce = new int[SEEN_SLOTS];

    /// <summary>How many lookups found their run, and how many did not, since the process started.</summary>
    internal static int Hits { get; private set; }

    internal static int Misses { get; private set; }

    /// <summary>
    /// Finds the coverage of a run. <paramref name="hasSubpixelForm"/> is false for a run the face
    /// could not give per-channel coverage for, such as one with colour glyphs.
    /// </summary>
    internal static bool TryGet(
        ReadOnlySpan<char> text,
        IWin32TextFace face,
        in Win32TextRasterizeRequest request,
        out Win32TextCoverage coverage,
        out bool hasSubpixelForm)
    {
        var key = CreateKey(text, face, in request);
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node) && text.SequenceEqual(node.Value.Text))
            {
                _leastRecentFirst.Remove(node);
                _leastRecentFirst.AddLast(node);
                Hits++;
                coverage = node.Value.Coverage;
                hasSubpixelForm = node.Value.HasSubpixelForm;
                return true;
            }

            Misses++;
        }

        coverage = default;
        hasSubpixelForm = false;
        return false;
    }

    /// <summary>Keeps a copy of <paramref name="coverage"/>, which may alias a buffer the caller reuses.</summary>
    internal static void Add(
        ReadOnlySpan<char> text,
        IWin32TextFace face,
        in Win32TextRasterizeRequest request,
        in Win32TextCoverage coverage,
        bool hasSubpixelForm)
    {
        int bytes = hasSubpixelForm ? coverage.WidthPx * coverage.HeightPx * 3 : 0;
        if (bytes > MAX_ENTRY_BYTES)
        {
            return;
        }

        var key = CreateKey(text, face, in request);
        int hash = key.GetHashCode();
        int slot = (int)((uint)hash % SEEN_SLOTS);
        lock (_gate)
        {
            if (_seenOnce[slot] != hash)
            {
                _seenOnce[slot] = hash;
                return;
            }
        }

        var kept = hasSubpixelForm
            ? new Win32TextCoverage(coverage.WidthPx, coverage.HeightPx, coverage.Data.AsSpan(0, bytes).ToArray())
            : default;
        var entry = new Entry(key, text.ToString(), kept, hasSubpixelForm, bytes);

        lock (_gate)
        {
            if (_map.Remove(entry.Key, out var replaced))
            {
                _leastRecentFirst.Remove(replaced);
                _bytes -= replaced.Value.Bytes;
                RenderResourceMetrics.TextCacheBytesChanged(-replaced.Value.Bytes);
            }

            _map[entry.Key] = _leastRecentFirst.AddLast(entry);
            _bytes += bytes;
            RenderResourceMetrics.TextCacheBytesChanged(bytes);

            while (_bytes > MAX_BYTES && _leastRecentFirst.First is LinkedListNode<Entry> oldest)
            {
                _leastRecentFirst.RemoveFirst();
                _map.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bytes;
                RenderResourceMetrics.TextCacheBytesChanged(-oldest.Value.Bytes);
            }
        }
    }

    internal static void Clear()
    {
        lock (_gate)
        {
            RenderResourceMetrics.TextCacheBytesChanged(-_bytes);
            _bytes = 0;
            _map.Clear();
            _leastRecentFirst.Clear();
            Array.Clear(_seenOnce);
        }
    }

    private static Key CreateKey(ReadOnlySpan<char> text, IWin32TextFace face, in Win32TextRasterizeRequest request)
        => new(
            string.GetHashCode(text),
            face,
            request.WidthPx,
            request.HeightPx,
            (int)request.HorizontalAlignment,
            (int)request.VerticalAlignment,
            (int)request.Wrapping,
            (int)request.Trimming,
            (int)Math.Round(request.DpiScale * 1000),
            request.InkInset.Left,
            request.InkInset.Top);

    private readonly record struct Key(
        int TextHash,
        IWin32TextFace Face,
        int WidthPx,
        int HeightPx,
        int HorizontalAlignment,
        int VerticalAlignment,
        int Wrapping,
        int Trimming,
        int ScaleThousandths,
        int InsetLeftPx,
        int InsetTopPx);

    private sealed record Entry(Key Key, string Text, Win32TextCoverage Coverage, bool HasSubpixelForm, int Bytes);
}
