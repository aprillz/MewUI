// Ported from Windows Terminal src/buffer/out/textBuffer.cpp (MIT): the OSC 8 hyperlink maps and shell-integration marks.

using System.Text;

using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Buffer;

/// <summary>The cells a shell-integration mark covers: prompt, command and output, each ending where the next starts.</summary>
internal sealed class MarkExtents
{
    public ScrollbarData Data { get; init; } = new();

    public TilPoint Start { get; set; }

    // Exclusive.
    public TilPoint End { get; set; }

    public TilPoint? CommandEnd { get; set; }

    public TilPoint? OutputEnd { get; set; }

    public bool HasCommand() => CommandEnd.HasValue && CommandEnd.Value != End;

    public bool HasOutput() => OutputEnd.HasValue && OutputEnd != CommandEnd;

    public (TilPoint Start, TilPoint End) GetExtent() => (Start, OutputEnd ?? CommandEnd ?? End);
}

internal readonly record struct ScrollMark(int Row, ScrollbarData Data);

internal sealed partial class TextBuffer
{
    private Dictionary<ushort, string> _hyperlinkMap = new();
    private Dictionary<string, ushort> _hyperlinkCustomIdMap = new(StringComparer.Ordinal);
    private ushort _currentHyperlinkId = 1;

    public void AddHyperlinkToMap(string uri, ushort id) => _hyperlinkMap[id] = uri;

    public string GetHyperlinkUriFromId(ushort id) => _hyperlinkMap[id];

    public bool TryGetHyperlinkUri(ushort id, out string uri) => _hyperlinkMap.TryGetValue(id, out uri!);

    /// <summary>
    /// The id a hyperlink's cells carry: a new one per link, or the same one for every link sharing a custom
    /// <paramref name="id"/> and URI (OSC 8's <c>id=</c> parameter).
    /// </summary>
    public ushort GetHyperlinkId(string uri, string id)
    {
        ushort numericId;
        if (id.Length == 0)
        {
            numericId = _currentHyperlinkId;
            ++_currentHyperlinkId;
        }
        else
        {
            // The URI's hash is part of the key, so equal ids for different URIs stay apart.
            string newId = id + "%" + StringComparer.Ordinal.GetHashCode(uri).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!_hyperlinkCustomIdMap.TryGetValue(newId, out numericId))
            {
                numericId = _currentHyperlinkId;
                _hyperlinkCustomIdMap[newId] = numericId;
                ++_currentHyperlinkId;
            }
        }

        // The id wraps around after 65535 links and 0 means "no link".
        if (_currentHyperlinkId == 0)
        {
            ++_currentHyperlinkId;
        }

        return numericId;
    }

    public void RemoveHyperlinkFromMap(ushort id)
    {
        _hyperlinkMap.Remove(id);
        foreach (var pair in _hyperlinkCustomIdMap)
        {
            if (pair.Value == id)
            {
                _hyperlinkCustomIdMap.Remove(pair.Key);
                break;
            }
        }
    }

    public string GetCustomIdFromId(ushort id)
    {
        foreach (var pair in _hyperlinkCustomIdMap)
        {
            if (pair.Value == id)
            {
                return pair.Key;
            }
        }

        return string.Empty;
    }

    public void CopyHyperlinkMaps(TextBuffer other)
    {
        _hyperlinkMap = new Dictionary<ushort, string>(other._hyperlinkMap);
        _hyperlinkCustomIdMap = new Dictionary<string, ushort>(other._hyperlinkCustomIdMap, StringComparer.Ordinal);
        _currentHyperlinkId = other._currentHyperlinkId;
    }

    /// <summary>Forgets the links of the row about to be recycled that no other row still uses.</summary>
    private void PruneHyperlinks()
    {
        var hyperlinks = GetRowByOffset(0).GetHyperlinks();
        if (hyperlinks.Count == 0)
        {
            return;
        }

        var firstRowRefs = new HashSet<ushort>(hyperlinks);
        int total = TotalRowCount;
        for (int index = 1; index < total && firstRowRefs.Count != 0; index++)
        {
            foreach (ushort id in GetRowByOffset(index).GetHyperlinks())
            {
                firstRowRefs.Remove(id);
            }
        }

        foreach (ushort id in firstRowRefs)
        {
            RemoveHyperlinkFromMap(id);
        }
    }

    public List<ScrollMark> GetMarkRows()
    {
        var marks = new List<ScrollMark>();
        int bottom = EstimateOffsetOfLastCommittedRow();
        for (int y = 0; y <= bottom; y++)
        {
            var data = GetRowByOffset(y).ScrollbarData;
            if (data != null)
            {
                marks.Add(new ScrollMark(y, data));
            }
        }

        return marks;
    }

    /// <summary>The extents of the newest <paramref name="limit"/> shell-integration marks, oldest first.</summary>
    public List<MarkExtents> GetMarkExtents(int limit = int.MaxValue)
    {
        var marks = new List<MarkExtents>();
        if (limit == 0)
        {
            return marks;
        }

        int bottom = EstimateOffsetOfLastCommittedRow();
        int lastPromptY = bottom;
        for (int promptY = bottom; promptY >= 0; promptY--)
        {
            var data = GetRowByOffset(promptY).ScrollbarData;

            // Default marks come from the UI, not from shell integration.
            if (data == null || data.Category == MarkCategory.Default)
            {
                continue;
            }

            marks.Add(ScrollMarkExtentForRow(promptY, lastPromptY));
            if (marks.Count >= limit)
            {
                break;
            }

            lastPromptY = promptY;
        }

        marks.Reverse();
        return marks;
    }

    /// <summary>Removes the marks on the rows from <paramref name="start"/> to <paramref name="end"/>, inclusive.</summary>
    public void ClearMarksInRange(TilPoint start, TilPoint end)
    {
        int top = Math.Clamp(Math.Min(start.Y, end.Y), 0, _height - 1);
        int bottom = Math.Clamp(Math.Max(start.Y, end.Y), 0, EstimateOffsetOfLastCommittedRow());

        for (int y = top; y <= bottom; y++)
        {
            var row = GetMutableRowByOffset(y);
            row.ScrollbarData = null;
            foreach (ref var run in row.Attributes.MutableRuns)
            {
                run.Value.MarkAttributes = MarkKind.None;
            }

            row.Attributes.Compact();
        }
    }

    public void ClearAllMarks() => ClearMarksInRange(new TilPoint(0, 0), new TilPoint(_width - 1, _height - 1));

    private MarkExtents ScrollMarkExtentForRow(int rowOffset, int bottomInclusive)
    {
        var startRow = GetRowByOffset(rowOffset);
        var mark = new MarkExtents { Data = startRow.ScrollbarData!.Clone() };

        bool startedPrompt = false;
        bool startedCommand = false;
        bool startedOutput = false;

        void EndThisMark(TilPoint at)
        {
            if (startedOutput)
            {
                mark.OutputEnd = at;
            }

            if (!startedOutput && startedCommand)
            {
                mark.CommandEnd = at;
            }

            if (!startedCommand)
            {
                mark.End = at;
            }
        }

        var lastMarkedText = new TilPoint(0, rowOffset);
        for (int y = rowOffset; y <= bottomInclusive; y++)
        {
            int x = 0;
            foreach (var run in GetRowByOffset(y).Attributes.Runs)
            {
                int nextX = x + run.Length;
                var markKind = run.Value.MarkAttributes;
                if (markKind != MarkKind.None)
                {
                    lastMarkedText = new TilPoint(nextX, y);

                    if (markKind == MarkKind.Prompt)
                    {
                        // A new prompt after a command or output ends this mark.
                        if (startedCommand || startedOutput)
                        {
                            break;
                        }

                        if (!startedPrompt)
                        {
                            startedPrompt = true;
                            mark.Start = new TilPoint(x, y);
                        }

                        EndThisMark(lastMarkedText);
                    }
                    else if (markKind == MarkKind.Command && startedPrompt)
                    {
                        startedCommand = true;
                        EndThisMark(lastMarkedText);
                    }
                    else if (markKind == MarkKind.Output && startedPrompt)
                    {
                        startedOutput = true;
                        if (!mark.CommandEnd.HasValue)
                        {
                            // Output with no command: the command ends where the prompt does.
                            mark.CommandEnd = mark.End;
                            startedCommand = true;
                        }

                        EndThisMark(lastMarkedText);
                    }
                }

                x = nextX;
            }
        }

        if (!startedCommand)
        {
            EndThisMark(lastMarkedText);
        }

        return mark;
    }

    private string CommandForRow(int rowOffset, int bottomInclusive, bool clipAtCursor = false)
    {
        var commandBuilder = new StringBuilder();
        var lastMarkKind = MarkKind.Prompt;
        var cursorPosition = _cursor.Position;
        for (int y = rowOffset; y <= bottomInclusive; y++)
        {
            bool onCursorRow = clipAtCursor && y == cursorPosition.Y;
            var row = GetRowByOffset(y);
            int x = 0;
            foreach (var run in row.Attributes.Runs)
            {
                int nextX = x + run.Length;
                if (onCursorRow)
                {
                    nextX = Math.Min(nextX, cursorPosition.X);
                }

                var markKind = run.Value.MarkAttributes;
                if (markKind != lastMarkKind)
                {
                    if (lastMarkKind == MarkKind.Command)
                    {
                        return commandBuilder.ToString();
                    }

                    lastMarkKind = markKind;
                }

                if (markKind == MarkKind.Command)
                {
                    commandBuilder.Append(row.GetText(x, nextX));
                }

                x = nextX;
                if (onCursorRow && x == cursorPosition.X)
                {
                    return commandBuilder.ToString();
                }
            }
        }

        return commandBuilder.ToString();
    }

    /// <summary>The command typed after the last prompt, up to the cursor.</summary>
    public string CurrentCommand()
    {
        for (int promptY = _cursor.Position.Y; promptY >= 0; promptY--)
        {
            if (GetRowByOffset(promptY).ScrollbarData != null)
            {
                return CommandForRow(promptY, EstimateOffsetOfLastCommittedRow(), true);
            }
        }

        return string.Empty;
    }

    public List<string> Commands()
    {
        var commands = new List<string>();
        int bottom = EstimateOffsetOfLastCommittedRow();
        int lastPromptY = bottom;
        for (int promptY = bottom; promptY >= 0; promptY--)
        {
            if (GetRowByOffset(promptY).ScrollbarData == null)
            {
                continue;
            }

            string command = CommandForRow(promptY, lastPromptY);
            if (command.Length != 0)
            {
                commands.Add(command);
            }

            lastPromptY = promptY;
        }

        commands.Reverse();
        return commands;
    }

    /// <summary>OSC 133;A: the cursor's row starts a prompt and new text is marked as prompt.</summary>
    public void StartPrompt()
    {
        GetMutableRowByOffset(_cursor.Position.Y).StartPrompt();
        var attributes = _currentAttributes;
        attributes.MarkAttributes = MarkKind.Prompt;
        _currentAttributes = attributes;
    }

    /// <summary>Starts a mark on the cursor's row when command or output comes without a prompt that is still open.</summary>
    private bool CreatePromptMarkIfNeeded()
    {
        var mostRecentMarks = GetMarkExtents(1);
        if (mostRecentMarks.Count != 0)
        {
            var mostRecentMark = mostRecentMarks[0];
            if (!mostRecentMark.HasOutput() || mostRecentMark.HasCommand())
            {
                return false;
            }
        }

        GetMutableRowByOffset(_cursor.Position.Y).StartPrompt();
        return true;
    }

    /// <summary>OSC 133;B: new text is marked as the command.</summary>
    public bool StartCommand()
    {
        bool createdMark = CreatePromptMarkIfNeeded();
        var attributes = _currentAttributes;
        attributes.MarkAttributes = MarkKind.Command;
        _currentAttributes = attributes;
        return createdMark;
    }

    /// <summary>OSC 133;C: new text is marked as the command's output.</summary>
    public bool StartOutput()
    {
        bool createdMark = CreatePromptMarkIfNeeded();
        var attributes = _currentAttributes;
        attributes.MarkAttributes = MarkKind.Output;
        _currentAttributes = attributes;
        return createdMark;
    }

    /// <summary>OSC 133;D: ends marking and stores the exit code on the row the prompt started on.</summary>
    public void EndCurrentCommand(uint? error)
    {
        var attributes = _currentAttributes;
        attributes.MarkAttributes = MarkKind.None;
        _currentAttributes = attributes;

        for (int y = _cursor.Position.Y; y >= 0; y--)
        {
            var row = GetMutableRowByOffset(y);
            if (row.ScrollbarData != null)
            {
                row.EndOutput(error);
                return;
            }
        }
    }

    public void SetScrollbarData(ScrollbarData mark, int y) => GetMutableRowByOffset(y).ScrollbarData = mark;

    public void ManuallyMarkRowAsPrompt(int y)
    {
        var row = GetMutableRowByOffset(y);
        foreach (ref var run in row.Attributes.MutableRuns)
        {
            run.Value.MarkAttributes = MarkKind.Prompt;
        }

        row.Attributes.Compact();
    }
}
