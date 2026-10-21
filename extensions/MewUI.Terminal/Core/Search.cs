// Ported from Windows Terminal src/buffer/out/search.h and search.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Core;

/// <summary>A find session: the matches of a needle in the buffer and which one is current.</summary>
internal sealed class Search
{
    private TerminalCore? _terminal;
    private string _needle = string.Empty;
    private SearchFlag _flags;
    private ulong _lastMutationId;
    private bool _ok;
    private List<TilPointSpan> _results = new();
    private int _index;
    private int _step;

    /// <summary>Whether the results are out of date: another terminal or needle, or the buffer changed since.</summary>
    public bool IsStale(TerminalCore terminal, string needle, SearchFlag flags) =>
        !ReferenceEquals(_terminal, terminal)
        || _needle != needle
        || _flags != flags
        || _lastMutationId != terminal.GetTextBuffer().LastMutationId;

    /// <summary>Searches again; the current match starts at the selection or the previously focused match.</summary>
    public void Reset(TerminalCore terminal, string needle, SearchFlag flags, bool reverse)
    {
        var textBuffer = terminal.GetTextBuffer();

        _terminal = terminal;
        _needle = needle;
        _flags = flags;
        _lastMutationId = textBuffer.LastMutationId;

        var result = textBuffer.SearchText(needle, _flags);
        _ok = result != null;
        _results = result ?? new List<TilPointSpan>();
        _index = reverse ? _results.Count - 1 : 0;
        _step = reverse ? -1 : 1;

        if (terminal.IsSelectionActive)
        {
            MoveToPoint(textBuffer.ScreenToBufferPosition(terminal.GetSelectionAnchor()));
        }
        else if (terminal.GetSearchHighlightFocused() is TilPointSpan span)
        {
            MoveToPoint(_step > 0 ? span.Start : span.End);
        }
    }

    /// <summary>Makes the first match at or after (or, searching backward, at or before) <paramref name="anchor"/> current.</summary>
    public void MoveToPoint(TilPoint anchor)
    {
        if (_results.Count == 0)
        {
            return;
        }

        int count = _results.Count;
        int index;
        if (_step < 0)
        {
            for (index = count - 1; index >= 0 && _results[index].Start > anchor; --index)
            {
            }
        }
        else
        {
            for (index = 0; index < count && _results[index].Start < anchor; ++index)
            {
            }
        }

        _index = (index + count) % count;
    }

    /// <summary>Makes the first match strictly after (or before) <paramref name="anchor"/> current.</summary>
    public void MovePastPoint(TilPoint anchor)
    {
        if (_results.Count == 0)
        {
            return;
        }

        int count = _results.Count;
        int index;
        if (_step < 0)
        {
            for (index = count - 1; index >= 0 && _results[index].Start >= anchor; --index)
            {
            }
        }
        else
        {
            for (index = 0; index < count && _results[index].Start <= anchor; ++index)
            {
            }
        }

        _index = (index + count) % count;
    }

    /// <summary>Moves to the next match, wrapping around.</summary>
    public void FindNext(bool reverse)
    {
        _step = reverse ? -1 : 1;
        int count = _results.Count;
        if (count != 0)
        {
            _index = (_index + _step + count) % count;
        }
    }

    public TilPointSpan? GetCurrent()
    {
        if (_index >= 0 && _index < _results.Count)
        {
            return _results[_index];
        }

        return null;
    }

    /// <summary>Selects the current match; clears the selection when there is none.</summary>
    public bool SelectCurrent()
    {
        if (GetCurrent() is TilPointSpan current && _terminal != null)
        {
            var textBuffer = _terminal.GetTextBuffer();
            var selectionStart = textBuffer.BufferToScreenPosition(current.Start);
            var selectionEnd = textBuffer.BufferToScreenPosition(current.End);
            _terminal.SelectNewRegion(selectionStart, selectionEnd);
            return true;
        }

        _terminal?.ClearSelection();
        return false;
    }

    public List<TilPointSpan> Results => _results;

    public int CurrentMatch => _index;

    /// <summary>False when the needle was an invalid regular expression.</summary>
    public bool IsOk => _ok;
}
