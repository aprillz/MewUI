// Ported from Windows Terminal src/cascadia/TerminalCore/Terminal.hpp, Terminal.cpp and terminalrenderdata.cpp (MIT).

using System.Diagnostics;
using System.Text.RegularExpressions;

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Core;

using CursorType = Aprillz.MewUI.Terminal.Buffer.CursorType;

/// <summary>
/// The terminal: the main and alternate buffers, the viewport into them, the VT parser and dispatcher that write to
/// them, and the input modes. All access goes through <see cref="LockForReading"/> or <see cref="LockForWriting"/>.
/// </summary>
internal sealed partial class TerminalCore : ITerminalApi
{
    // A 1-cell viewport can hang reflow on a wide glyph, and VT margins need two rows anyway.
    public const int MINIMUM_VISIBLE_CELLS = 2;

    private const int TASKBAR_MIN_PROGRESS = 10;

    private static readonly Regex _urlPattern = new(
        @"\b(?:https?|ftp|file)://[-A-Za-z0-9+&@#/%?=~_|$!:,.;]*[A-Za-z0-9+&@#/%=~_|$]",
        RegexOptions.CultureInvariant);

    private readonly object _readWriteLock = new();
    private readonly bool _suppressLockChecks;
    private readonly RenderSettings _renderSettings = new();
    private readonly TerminalInput _terminalInput = new();
    private StateMachine? _stateMachine;
    private AdaptDispatch? _dispatch;
    private TextBuffer? _mainBuffer;
    private TextBuffer? _altBuffer;
    private Viewport _mutableViewport;
    private int _scrollbackLines;
    private TilSize _altBufferSize;
    private TilSize? _deferredResize;
    private int _scrollOffset;

    private string? _title;
    private string? _startingTitle;
    private uint? _startingTabColor;
    private CursorType _defaultCursorShape = CursorType.Legacy;
    private int _systemMode = 1 << (int)ITerminalApi.Mode.AutoWrap;
    private bool _focused;
    private bool _snapOnInput = true;
    private bool _altGrAliasing = true;
    private bool _suppressApplicationTitle;
    private bool _trimBlockSelection = true;
    private bool _autoMarkPrompts;
    private int _taskbarState;
    private int _taskbarProgress;
    private string _answerbackMessage = string.Empty;
    private string _workingDirectory = string.Empty;
    private string _wordDelimiters = TerminalSettings.DEFAULT_WORD_DELIMITERS;
    private bool _detectUrls;
    private bool _clipboardOperationsAllowed = true;

    // Detected URLs as [Start, Stop) buffer ranges, with the pattern that found them.
    private List<(TilPoint Start, TilPoint Stop, int Value)> _patterns = new();
    private const int HYPERLINK_PATTERN_ID = 0;

    public TerminalCore()
    {
        _renderSettings.SetColorAlias(ColorAlias.DefaultForeground, TextColor.DEFAULT_FOREGROUND, TextColor.Rgb(255, 255, 255));
        _renderSettings.SetColorAlias(ColorAlias.DefaultBackground, TextColor.DEFAULT_BACKGROUND, TextColor.Rgb(0, 0, 0));
    }

    /// <summary>A terminal for tests, which may be used without taking the lock.</summary>
    public TerminalCore(bool suppressLockChecks)
        : this()
    {
        _suppressLockChecks = suppressLockChecks;
    }

    /// <summary>Raised with text the terminal answers the application with (reports, answerback).</summary>
    public Action<string>? WriteInput { get; set; }

    public Action? WarningBellRequested { get; set; }

    public Action<string>? TitleChanged { get; set; }

    public Action<string>? CopyToClipboardRequested { get; set; }

    /// <summary>Raised with the visible top row, the visible height and the buffer height (for the scroll bar).</summary>
    public Action<int, int, int>? ScrollPositionChanged { get; set; }

    public Action? TaskbarProgressChanged { get; set; }

    public Action<bool>? ShowWindowRequested { get; set; }

    public Action<int, int, TimeSpan>? PlayMidiNoteRequested { get; set; }

    public Action<string, int>? CompletionsChanged { get; set; }

    public Action<string, int>? SearchMissingCommandRequested { get; set; }

    public Action<string, string>? ShowNotificationRequested { get; set; }

    public Action? ClearQuickFixRequested { get; set; }

    public Action<int, int>? WindowSizeChangeRequested { get; set; }

    /// <summary>Whether the scroll position is reported on every buffer rotation, even when the visible rows stay put.</summary>
    public bool AlwaysNotifyOnBufferRotation { get; set; }

    public void Create(TilSize viewportSize, int scrollbackLines, Renderer renderer)
    {
        viewportSize = new TilSize(Math.Max(viewportSize.Width, MINIMUM_VISIBLE_CELLS), Math.Max(viewportSize.Height, MINIMUM_VISIBLE_CELLS));
        _mutableViewport = Viewport.FromDimensions(default, viewportSize);
        _scrollbackLines = scrollbackLines;
        var bufferSize = new TilSize(viewportSize.Width, ClampToShortMax(viewportSize.Height + scrollbackLines, 1));
        const int CURSOR_SIZE = 12;
        _mainBuffer = new TextBuffer(bufferSize, default, CURSOR_SIZE, true, renderer);

        _dispatch = new AdaptDispatch(this, renderer, _renderSettings, _terminalInput);
        var engine = new OutputStateMachineEngine(_dispatch);
        _stateMachine = new StateMachine(engine);
    }

    public void HardResetWithoutErase()
    {
        AssertLocked();
        StateMachine.ResetState();
        Dispatch.HardReset(false);
    }

    public void CreateFromSettings(TerminalSettings settings, Renderer renderer)
    {
        var viewportSize = new TilSize(
            ClampToShortMax(settings.InitialCols, MINIMUM_VISIBLE_CELLS),
            ClampToShortMax(settings.InitialRows, MINIMUM_VISIBLE_CELLS));
        Create(viewportSize, ClampToShortMax(settings.HistorySize, 0), renderer);
        UpdateSettings(settings);
    }

    public void UpdateSettings(TerminalSettings settings)
    {
        UpdateAppearance(settings);

        _snapOnInput = settings.SnapOnInput;
        _altGrAliasing = settings.AltGrAliasing;
        _answerbackMessage = settings.AnswerbackMessage;
        _wordDelimiters = settings.WordDelimiters;
        _suppressApplicationTitle = settings.SuppressApplicationTitle;
        _trimBlockSelection = settings.TrimBlockSelection;
        _autoMarkPrompts = settings.AutoMarkPrompts;
        _clipboardOperationsAllowed = settings.AllowVtClipboardWrite;

        if (_stateMachine != null)
        {
            SetOptionalFeatures(settings);
        }

        // The tab color setting is the frame background; the VT DECAC sequence can replace it.
        _renderSettings.SetColorTableEntry(TextColor.FRAME_BACKGROUND, settings.TabColor ?? TextAttribute.INVALID_COLOR);

        // These are the colors a hard reset (RIS) returns to.
        _renderSettings.SaveDefaultSettings();

        _startingTitle ??= settings.StartingTitle;
        if (!_startingTabColor.HasValue && settings.StartingTabColor.HasValue)
        {
            _startingTabColor = settings.StartingTabColor;
        }

        if (_mainBuffer != null)
        {
            _detectUrls = settings.DetectUrls;
            UpdateUrlDetection();
        }
    }

    public void UpdateAppearance(TerminalSettings appearance)
    {
        _renderSettings.SetRenderMode(RenderSettings.Mode.IntenseIsBold, appearance.IntenseIsBold);
        _renderSettings.SetRenderMode(RenderSettings.Mode.IntenseIsBright, appearance.IntenseIsBright);

        var cursorShape = appearance.CursorShape switch
        {
            TerminalCursorShape.Underscore => CursorType.Underscore,
            TerminalCursorShape.FilledBox => CursorType.FullBox,
            TerminalCursorShape.EmptyBox => CursorType.EmptyBox,
            TerminalCursorShape.Vintage => CursorType.Legacy,
            TerminalCursorShape.DoubleUnderscore => CursorType.DoubleUnderscore,
            _ => CursorType.VerticalBar,
        };

        if (_mainBuffer != null)
        {
            ActiveBuffer.Cursor.SetStyle(appearance.CursorHeight, cursorShape);
        }

        _defaultCursorShape = cursorShape;

        UpdateColorScheme(appearance);
    }

    public void UpdateColorScheme(TerminalSettings scheme)
    {
        _renderSettings.SetColorAlias(ColorAlias.DefaultBackground, TextColor.DEFAULT_BACKGROUND, scheme.DefaultBackground);
        _renderSettings.SetColorAlias(ColorAlias.DefaultForeground, TextColor.DEFAULT_FOREGROUND, scheme.DefaultForeground);
        _renderSettings.SetColorTableEntry(TextColor.CURSOR_COLOR, scheme.CursorColor);
        _renderSettings.SetColorTableEntry(TextColor.SELECTION_BACKGROUND, scheme.SelectionBackground);

        for (int index = 0; index < 16; index++)
        {
            _renderSettings.SetColorTableEntry(index, scheme.ColorTable[index]);
        }

        // The scroll bar marks are drawn in scheme colors.
        NotifyScrollEvent();
    }

    public void SetCursorStyle(Adapter.CursorStyle cursorStyle) => Dispatch.SetCursorStyle(cursorStyle);

    public void SetOptionalFeatures(TerminalSettings settings)
    {
        var features = TermDispatch.OptionalFeature.None;
        if (settings.AllowVtChecksumReport)
        {
            features |= TermDispatch.OptionalFeature.ChecksumReport;
        }

        if (settings.AllowVtClipboardWrite)
        {
            features |= TermDispatch.OptionalFeature.ClipboardWrite;
        }

        if (settings.AllowOscNotifications)
        {
            features |= TermDispatch.OptionalFeature.DesktopNotification;
        }

        Dispatch.SetOptionalFeatures(features);
    }

    public bool IsXtermBracketedPasteModeEnabled => GetSystemMode(ITerminalApi.Mode.BracketedPaste);

    public string WorkingDirectory => _workingDirectory;

    public StateMachine StateMachine => _stateMachine ?? throw new InvalidOperationException("The terminal has not been created.");

    private AdaptDispatch Dispatch => _dispatch ?? throw new InvalidOperationException("The terminal has not been created.");

    public TerminalInput TerminalInput
    {
        get
        {
            AssertLocked();
            return _terminalInput;
        }
    }

    public RenderSettings RenderSettings
    {
        get
        {
            AssertLocked();
            return _renderSettings;
        }
    }

    public TextBuffer MainBuffer => _mainBuffer ?? throw new InvalidOperationException("The terminal has not been created.");

    /// <summary>The buffer being written and shown: the alternate buffer while it is in use.</summary>
    public TextBuffer ActiveBuffer => InAltBuffer ? _altBuffer! : MainBuffer;

    public bool InAltBuffer
    {
        get
        {
            AssertLocked();
            return _altBuffer != null;
        }
    }

    /// <summary>How many rows the view is scrolled up from the bottom of the output.</summary>
    public int ScrollOffset
    {
        get => _scrollOffset;
        set => _scrollOffset = value;
    }

    public bool DetectUrls
    {
        get => _detectUrls;
        set => _detectUrls = value;
    }

    public string WordDelimiters => _wordDelimiters;

    public bool TrimBlockSelection => _trimBlockSelection;

    public bool AltGrAliasing => _altGrAliasing;

    /// <summary>Changes how many rows are kept above the viewport, reflowing the main buffer into the new size.</summary>
    public void SetScrollbackLines(int scrollbackLines)
    {
        int lines = ClampToShortMax(scrollbackLines, 0);
        if (lines != _scrollbackLines)
        {
            _scrollbackLines = lines;
            UserResize(GetMutableViewport().Dimensions, force: true);
        }
    }

    /// <summary>Resizes the viewport, reflowing the main buffer; false when the size did not change.</summary>
    public bool UserResize(TilSize requestedSize) => UserResize(requestedSize, force: false);

    private bool UserResize(TilSize requestedSize, bool force)
    {
        var viewportSize = new TilSize(Math.Max(requestedSize.Width, MINIMUM_VISIBLE_CELLS), Math.Max(requestedSize.Height, MINIMUM_VISIBLE_CELLS));
        var oldDimensions = GetMutableViewport().Dimensions;
        if (viewportSize == oldDimensions && !force)
        {
            return false;
        }

        // The alternate buffer is resized without reflow, and the main buffer when the application returns to it.
        if (InAltBuffer)
        {
            _deferredResize = viewportSize;
            _altBuffer!.ResizeTraditional(viewportSize);
            _altBufferSize = viewportSize;
            _altBuffer.TriggerRedrawAll();
            return true;
        }

        int newBufferHeight = Math.Clamp(viewportSize.Height + _scrollbackLines, 1, short.MaxValue);
        var bufferSize = new TilSize(viewportSize.Width, newBufferHeight);

        // A view at the bottom stays at the bottom; a scrolled view keeps its rows.
        bool originalOffsetWasZero = _scrollOffset == 0;

        var mainBuffer = MainBuffer;
        var newTextBuffer = new TextBuffer(bufferSize, default, 0, mainBuffer.IsActiveBuffer, mainBuffer.Renderer);

        var positionInfo = new PositionInformation
        {
            MutableViewportTop = _mutableViewport.Top,
            VisibleViewportTop = VisibleStartIndex,
        };
        TextBuffer.Reflow(mainBuffer, newTextBuffer, _mutableViewport, positionInfo);
        newTextBuffer.CurrentAttributes = mainBuffer.CurrentAttributes;

        // The new viewport is placed so the last text (or the cursor) is on screen, as close to where the old
        // viewport was as possible.
        var newCursorPos = newTextBuffer.Cursor.Position;
        var newLastChar = newTextBuffer.GetLastNonSpaceCharacter();

        int maxRow = Math.Max(newLastChar.Y, newCursorPos.Y);
        int proposedTopFromLastLine = maxRow - viewportSize.Height + 1;
        int proposedTopFromScrollback = positionInfo.MutableViewportTop;

        int proposedTop = Math.Max(proposedTopFromLastLine, proposedTopFromScrollback);

        // Narrowing can wrap the line above the viewport into it; the viewport moves up a line to keep that line whole.
        if (proposedTop == proposedTopFromScrollback)
        {
            var proposedViewFromTop = Viewport.FromDimensions(new TilPoint(0, proposedTopFromScrollback), viewportSize);
            if (maxRow < proposedViewFromTop.BottomInclusive)
            {
                if (viewportSize.Width < oldDimensions.Width && proposedTop > 0)
                {
                    var row = newTextBuffer.GetRowByOffset(proposedTop - 1);
                    if (row.WasWrapForced)
                    {
                        proposedTop--;
                    }
                }
            }
        }

        // If the text does not fit below the old top, the viewport follows the last line instead.
        var viewFromScrollback = Viewport.FromDimensions(new TilPoint(0, proposedTopFromScrollback), viewportSize);
        if (maxRow > viewFromScrollback.BottomInclusive)
        {
            proposedTop = proposedTopFromLastLine;
        }

        proposedTop = Math.Max(0, proposedTop);

        var newView = Viewport.FromDimensions(new TilPoint(0, proposedTop), viewportSize);
        int proposedBottom = newView.BottomExclusive;
        if (proposedBottom > bufferSize.Height)
        {
            proposedTop -= proposedBottom - bufferSize.Height;
        }

        // The cursor must stay in the viewport.
        proposedTop = Math.Min(proposedTop, newCursorPos.Y);

        _mutableViewport = Viewport.FromDimensions(new TilPoint(0, proposedTop), viewportSize);

        _mainBuffer = newTextBuffer;

        // A scrolled view keeps showing the rows it showed, as far as they still exist.
        int newVisibleTop = Math.Min(positionInfo.VisibleViewportTop, _mutableViewport.Top);
        newVisibleTop = Math.Max(newVisibleTop, 0);
        _scrollOffset = originalOffsetWasZero ? 0 : _mutableViewport.Top - newVisibleTop;

        _mainBuffer.TriggerRedrawAll();
        NotifyScrollEvent();
        return true;
    }

    /// <summary>Parses and carries out application output.</summary>
    public void Write(ReadOnlyMemory<char> text) => StateMachine.ProcessString(text);

    public void Write(string text) => StateMachine.ProcessString(text);

    /// <summary>Scrolls back to the bottom when input is typed, if the user asked for that.</summary>
    public void TrySnapOnInput()
    {
        if (_snapOnInput && _scrollOffset != 0)
        {
            _scrollOffset = 0;
            NotifyScrollEvent();
        }
    }

    public string GetHyperlinkAtViewportPosition(TilPoint viewportPos) => GetHyperlinkAtBufferPosition(ConvertToBufferCell(viewportPos, false));

    /// <summary>The URI of an OSC 8 hyperlink at a cell, or the text of a detected URL there, or an empty string.</summary>
    public string GetHyperlinkAtBufferPosition(TilPoint bufferPos)
    {
        var buffer = ActiveBuffer;
        var attr = buffer.GetRowByOffset(bufferPos.Y).GetAttrByColumn(bufferPos.X);
        if (attr.IsHyperlink())
        {
            return buffer.GetHyperlinkUriFromId(attr.HyperlinkId);
        }

        foreach (var pattern in FindPatterns(bufferPos))
        {
            if (pattern.Value == HYPERLINK_PATTERN_ID)
            {
                return buffer.GetPlainText(pattern.Start, pattern.Stop);
            }
        }

        return string.Empty;
    }

    public ushort GetHyperlinkIdAtViewportPosition(TilPoint viewportPos)
    {
        var bufferPos = ConvertToBufferCell(viewportPos, false);
        return ActiveBuffer.GetRowByOffset(bufferPos.Y).GetAttrByColumn(bufferPos.X).HyperlinkId;
    }

    /// <summary>The extent of the detected URL at a viewport cell, in viewport rows.</summary>
    public (TilPoint Start, TilPoint Stop)? GetHyperlinkIntervalFromViewportPosition(TilPoint viewportPos)
    {
        int visStart = VisibleStartIndex;
        var bufferPos = new TilPoint(viewportPos.X, viewportPos.Y + visStart);
        foreach (var pattern in FindPatterns(bufferPos))
        {
            if (pattern.Value == HYPERLINK_PATTERN_ID)
            {
                return (pattern.Start with { Y = pattern.Start.Y - visStart }, pattern.Stop with { Y = pattern.Stop.Y - visStart });
            }
        }

        return null;
    }

    /// <summary>The pattern ids (detected URLs) at a viewport cell.</summary>
    public List<int> GetPatternId(TilPoint viewportPos)
    {
        AssertLocked();
        var bufferPos = new TilPoint(viewportPos.X, viewportPos.Y + VisibleStartIndex);
        var result = new List<int>();
        foreach (var pattern in FindPatterns(bufferPos))
        {
            result.Add(pattern.Value);
        }

        return result;
    }

    /// <summary>The detected patterns whose [Start, Stop) range contains a cell.</summary>
    private IEnumerable<(TilPoint Start, TilPoint Stop, int Value)> FindPatterns(TilPoint bufferPos)
    {
        var cellEnd = bufferPos with { X = bufferPos.X + 1 };
        foreach (var pattern in _patterns)
        {
            if (pattern.Start <= bufferPos && pattern.Stop >= cellEnd)
            {
                yield return pattern;
            }
        }
    }

    private void InvalidatePatternTree()
    {
        foreach (var pattern in _patterns)
        {
            InvalidateFromCoords(pattern.Start, pattern.Stop);
        }
    }

    /// <summary>Redraws the cells from <paramref name="start"/> to <paramref name="end"/> in reading order.</summary>
    private void InvalidateFromCoords(TilPoint start, TilPoint end)
    {
        var buffer = ActiveBuffer;
        if (start.Y == end.Y)
        {
            buffer.TriggerRedraw(Viewport.FromInclusive(new TilInclusiveRect(start.X, start.Y, end.X, end.Y)));
        }
        else
        {
            int rowSize = buffer.GetRowByOffset(0).Size;

            buffer.TriggerRedraw(Viewport.FromInclusive(new TilInclusiveRect(start.X, start.Y, rowSize - 1, start.Y)));

            if (end.Y - start.Y > 1)
            {
                buffer.TriggerRedraw(Viewport.FromInclusive(new TilInclusiveRect(0, start.Y + 1, rowSize - 1, end.Y - 1)));
            }

            buffer.TriggerRedraw(Viewport.FromInclusive(new TilInclusiveRect(0, end.Y, end.X, end.Y)));
        }
    }

    /// <summary>A lock scope; disposing it releases the terminal lock.</summary>
    public readonly struct LockScope : IDisposable
    {
        private readonly object _lock;

        public LockScope(object lockObject)
        {
            _lock = lockObject;
            Monitor.Enter(_lock);
        }

        public void Dispose() => Monitor.Exit(_lock);
    }

    public LockScope LockForReading() => new(_readWriteLock);

    public LockScope LockForWriting() => new(_readWriteLock);

    public bool IsLockHeld => Monitor.IsEntered(_readWriteLock);

    [Conditional("DEBUG")]
    private void AssertLocked() => Debug.Assert(_suppressLockChecks || Monitor.IsEntered(_readWriteLock), "The terminal lock is not held.");

    private Viewport GetMutableViewport() => InAltBuffer ? Viewport.FromDimensions(default, _altBufferSize) : _mutableViewport;

    /// <summary>The bottom of the scrollable area: the end of the mutable viewport.</summary>
    public int BufferHeight => GetMutableViewport().BottomExclusive;

    public int ViewStartIndex => InAltBuffer ? 0 : _mutableViewport.Top;

    public int ViewEndIndex => InAltBuffer ? _altBufferSize.Height - 1 : _mutableViewport.BottomInclusive;

    public bool IsFocused => _focused;

    private int VisibleStartIndex => InAltBuffer ? 0 : Math.Max(0, _mutableViewport.Top - _scrollOffset);

    private int VisibleEndIndex => InAltBuffer ? _altBufferSize.Height - 1 : Math.Max(0, _mutableViewport.BottomInclusive - _scrollOffset);

    /// <summary>The rows the user sees: the mutable viewport moved up by the scroll offset.</summary>
    public Viewport GetViewport() => GetVisibleViewport();

    public Viewport MutableViewport => _mutableViewport;

    private Viewport GetVisibleViewport()
    {
        var origin = new TilPoint(0, VisibleStartIndex);
        var size = InAltBuffer ? _altBufferSize : _mutableViewport.Dimensions;
        return Viewport.FromDimensions(origin, size);
    }

    /// <summary>Keeps a scrolled-up view (or a selection) on the same text while output moves the viewport down.</summary>
    private void PreserveUserScrollOffset(int viewportDelta)
    {
        if (viewportDelta > 0 && (IsSelectionActive || _scrollOffset != 0))
        {
            int maxScrollOffset = ActiveBuffer.Size.Height - _mutableViewport.Height;
            _scrollOffset = Math.Min(_scrollOffset + viewportDelta, maxScrollOffset);
        }
    }

    /// <summary>Scrolls the view so <paramref name="viewTop"/> is the top visible row.</summary>
    public void UserScrollViewport(int viewTop)
    {
        // The pattern ranges are for the old visible rows.
        ClearPatternTree();

        if (InAltBuffer)
        {
            return;
        }

        int clampedNewTop = Math.Max(0, viewTop);
        int realTop = ViewStartIndex;
        int newDelta = realTop - clampedNewTop;
        _scrollOffset = Math.Max(0, newDelta);

        ActiveBuffer.TriggerScroll();
    }

    public int GetScrollOffset() => VisibleStartIndex;

    private void NotifyScrollEvent()
    {
        ClearPatternTree();

        var handler = ScrollPositionChanged;
        if (handler != null)
        {
            var visible = GetVisibleViewport();
            handler(visible.Top, visible.Height, BufferHeight);
        }
    }

    /// <summary>Detects URLs in the visible rows and one screen above and below them.</summary>
    public void UpdatePatternsUnderLock()
    {
        InvalidatePatternTree();

        int visStart = VisibleStartIndex;
        int visEnd = VisibleEndIndex;
        int viewportHeight = visEnd - visStart;
        var bufferSize = ActiveBuffer.Size;
        int begin = Math.Max(0, visStart - viewportHeight);
        int end = Math.Min(bufferSize.BottomInclusive, visEnd + viewportHeight);

        _patterns = GetPatterns(begin, end);
        InvalidatePatternTree();
    }

    private void ClearPatternTree()
    {
        AssertLocked();
        if (_patterns.Count != 0)
        {
            InvalidatePatternTree();
            _patterns = new List<(TilPoint, TilPoint, int)>();
        }
    }

    /// <summary>The tab color: the starting color if given, otherwise the frame background, if set.</summary>
    public uint? GetTabColor()
    {
        if (_startingTabColor.HasValue)
        {
            return _startingTabColor;
        }
        else
        {
            uint tabColor = _renderSettings.GetColorAlias(ColorAlias.FrameBackground);
            return tabColor == TextAttribute.INVALID_COLOR ? null : tabColor;
        }
    }

    public int TaskbarState => _taskbarState;

    public int TaskbarProgress => _taskbarProgress;

    private void UpdateUrlDetection()
    {
        if (_detectUrls)
        {
            UpdatePatternsUnderLock();
        }
        else
        {
            ClearPatternTree();
        }
    }

    /// <summary>Finds URLs in rows [<paramref name="begin"/>, <paramref name="end"/>], including URLs that wrap.</summary>
    private List<(TilPoint Start, TilPoint Stop, int Value)> GetPatterns(int begin, int end)
    {
        var intervals = new List<(TilPoint, TilPoint, int)>();
        if (!_detectUrls)
        {
            return intervals;
        }

        var buffer = ActiveBuffer;
        int rowBegin = Math.Max(0, begin);
        int rowEnd = Math.Min(buffer.Size.BottomExclusive, end + 1);
        if (rowBegin >= rowEnd)
        {
            return intervals;
        }

        var range = new BufferTextRange(buffer, rowBegin, rowEnd);
        foreach (Match match in _urlPattern.Matches(range.Text))
        {
            var span = range.RangeFromMatch(match);
            intervals.Add((span.Start, span.End, HYPERLINK_PATTERN_ID));
        }

        return intervals;
    }

    public void AddMarkFromUI(ScrollbarData mark, int y)
    {
        if (InAltBuffer)
        {
            return;
        }

        ActiveBuffer.SetScrollbarData(mark, y);
        NotifyScrollEvent();
    }

    /// <summary>Clears the marks at the cursor, or in the selection if there is one.</summary>
    public void ClearMark()
    {
        var cursor = ActiveBuffer.Cursor.Position;
        var start = cursor;
        var end = cursor;
        if (IsSelectionActive)
        {
            start = GetSelectionAnchor();
            end = GetSelectionEnd();
        }

        ActiveBuffer.ClearMarksInRange(start, end);
        NotifyScrollEvent();
    }

    public void ClearAllMarks()
    {
        ActiveBuffer.ClearAllMarks();
        NotifyScrollEvent();
    }

    public List<ScrollMark> GetMarkRows() => InAltBuffer ? new List<ScrollMark>() : ActiveBuffer.GetMarkRows();

    public List<MarkExtents> GetMarkExtents() => InAltBuffer ? new List<MarkExtents>() : ActiveBuffer.GetMarkExtents();

    /// <summary>The scroll bar color of a mark: its own color, or one from the scheme by category.</summary>
    public uint GetColorForMark(ScrollbarData markData)
    {
        if (markData.Color is uint color)
        {
            return color;
        }

        return markData.Category switch
        {
            MarkCategory.Error => _renderSettings.GetColorTableEntry(TextColor.BRIGHT_RED),
            MarkCategory.Warning => _renderSettings.GetColorTableEntry(TextColor.BRIGHT_YELLOW),
            MarkCategory.Success => _renderSettings.GetColorTableEntry(TextColor.BRIGHT_GREEN),
            _ => _renderSettings.GetColorAlias(ColorAlias.DefaultForeground),
        };
    }

    public string CurrentCommand() => ActiveBuffer.CurrentCommand();

    /// <summary>The cursor position relative to the visible rows.</summary>
    public TilPoint GetViewportRelativeCursorPosition()
    {
        var absoluteCursorPosition = ActiveBuffer.Cursor.Position;
        var mutableViewport = GetMutableViewport();
        var relativeCursorPos = absoluteCursorPosition - mutableViewport.Origin;
        return new TilPoint(relativeCursorPos.X, relativeCursorPos.Y + _scrollOffset);
    }

    public TilPoint GetTextBufferEndPosition() => new(GetMutableViewport().Width - 1, ViewEndIndex);

    public TextBuffer GetTextBuffer() => ActiveBuffer;

    public string GetHyperlinkUri(ushort id) => ActiveBuffer.GetHyperlinkUriFromId(id);

    public string GetHyperlinkCustomId(ushort id) => ActiveBuffer.GetCustomIdFromId(id);

    public (uint Foreground, uint Background) GetAttributeColors(in TextAttribute attr) => RenderSettings.GetAttributeColors(attr);

    /// <summary>The window title: the one the application set, else the starting title.</summary>
    public string GetConsoleTitle()
    {
        AssertLocked();
        return _title ?? _startingTitle ?? string.Empty;
    }

    private static int ClampToShortMax(int value, int min) => Math.Clamp(value, min, short.MaxValue);
}
