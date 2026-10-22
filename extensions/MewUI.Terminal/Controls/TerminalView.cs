using System.Text;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal;

using CursorType = Aprillz.MewUI.Terminal.Buffer.CursorType;
using TerminalChanges = Aprillz.MewUI.Terminal.Core.Renderer;

/// <summary>
/// A terminal emulator: shows what a <see cref="ITerminalConnection"/> writes, as an xterm-compatible terminal, and sends
/// it keyboard and mouse input.
/// </summary>
/// <remarks>Output is parsed on the connection's thread; the view takes the result on the UI thread.</remarks>
public sealed partial class TerminalView : ContentControl, ITextInputClient, ITextCompositionClient
{
    /// <summary>The connection whose output this terminal shows, or null.</summary>
    public static readonly MewProperty<ITerminalConnection?> ConnectionProperty =
        MewProperty<ITerminalConnection?>.Register<TerminalView>(nameof(Connection), null, MewPropertyOptions.None,
            static (view, oldValue, newValue) => view.OnConnectionChanged(oldValue, newValue));

    /// <summary>The number of rows of output kept above the screen; the default is 9001.</summary>
    public static readonly MewProperty<int> ScrollbackLinesProperty =
        MewProperty<int>.Register<TerminalView>(nameof(ScrollbackLines), TerminalSettings.DEFAULT_HISTORY_SIZE, MewPropertyOptions.None,
            static (view, _, newValue) => view.OnScrollbackLinesChanged(newValue));

    /// <summary>The colors; the default is <see cref="TerminalColorScheme.Campbell"/>.</summary>
    public static readonly MewProperty<TerminalColorScheme> ColorSchemeProperty =
        MewProperty<TerminalColorScheme>.Register<TerminalView>(nameof(ColorScheme), TerminalColorScheme.Campbell, MewPropertyOptions.None,
            static (view, _, _) => view.ApplyAppearance());

    /// <summary>The cursor shape shown until the application chooses one; the default is <see cref="TerminalCursorShape.Bar"/>.</summary>
    public static readonly MewProperty<TerminalCursorShape> CursorShapeProperty =
        MewProperty<TerminalCursorShape>.Register<TerminalView>(nameof(CursorShape), TerminalCursorShape.Bar, MewPropertyOptions.None,
            static (view, _, _) => view.ApplyAppearance());

    /// <summary>The space between the edge and the cells, in DIPs; the default is 4.</summary>
    public static readonly MewProperty<double> TextPaddingProperty =
        MewProperty<double>.Register<TerminalView>(nameof(TextPadding), 4.0, MewPropertyOptions.AffectsLayout);

    /// <summary>Whether Option (Alt) with a key that types a character sends ESC and the key's base character; the default is false.</summary>
    public static readonly MewProperty<bool> OptionIsMetaProperty =
        MewProperty<bool>.Register<TerminalView>(nameof(OptionIsMeta), false, MewPropertyOptions.None);

    private static readonly MewPropertyKey<string> TitlePropertyKey =
        MewProperty<string>.RegisterReadOnly<TerminalView>(nameof(Title), string.Empty);

    /// <summary>The title the application set, or an empty string.</summary>
    public static readonly MewProperty<string> TitleProperty = TitlePropertyKey.Property;

    private static readonly MewPropertyKey<string> WorkingDirectoryPropertyKey =
        MewProperty<string>.RegisterReadOnly<TerminalView>(nameof(WorkingDirectory), string.Empty);

    /// <summary>The working directory the shell reported, or an empty string.</summary>
    public static readonly MewProperty<string> WorkingDirectoryProperty = WorkingDirectoryPropertyKey.Property;

    private const int CURSOR_BLINK_MS = 530;
    private const int WHEEL_SCROLL_LINES = 3;
    // The font sizes Ctrl+wheel stays within.
    private const double MIN_FONT_SIZE = 6;
    private const double MAX_FONT_SIZE = 72;

    private readonly TerminalCore _terminal;
    private readonly TerminalChanges _changes = new();
    private readonly TerminalRenderer _renderer = new();
    private readonly TerminalSurface _surface;
    private readonly ScrollBar _scrollBar;
    private readonly DispatcherTimer _cursorTimer;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly object _decodeLock = new();
    private char[] _decodeBuffer = new char[4096];

    private const int UPDATE_INTERVAL_MS = 16;

    // Set while a UI update is queued, so a burst of output queues one.
    private int _updateQueued;
    private long _lastUpdateTicks;

    // Copies of what the screen shows, taken under the terminal lock so drawing never waits for parsing.
    private readonly List<RowSnapshot> _snapshots = new();
    private readonly RenderSettings _settingsSnapshot = new();
    private CursorSnapshot _cursorSnapshot = new(CursorType.VerticalBar, 25, 1, " ", default, default, false, false);
    private bool _refreshAll;

    private int _columns = 80;
    private int _rows = 24;
    private bool _gridUpdateQueued;
    private double _areaWidth;
    private double _areaHeight;
    private int _visibleTop;
    private bool _settingScroll;
    private bool _cursorBlinkOn = true;
    private string? _composition;
    private bool _selecting;
    private bool _trackingButtonDown;

    // The cursor as of the last update, in viewport cells; row -1 when it is not shown.
    private TilPoint _cursorCell = new(0, -1);

    static TerminalView()
    {
        FocusableProperty.OverrideDefaultValue<TerminalView>(true);
    }

    /// <summary>Creates a terminal with no connection; set <see cref="Connection"/> to start one.</summary>
    public TerminalView()
    {
        _terminal = new TerminalCore();
        using (_terminal.LockForWriting())
        {
            _terminal.Create(new TilSize(_columns, _rows), ScrollbackLines, _changes);
            _terminal.WriteInput = SendInput;
            _terminal.WarningBellRequested = () => PostToUi(() => Bell?.Invoke());
            _terminal.TitleChanged = title => PostToUi(() => OnTitleChanged(title));
            _terminal.CopyToClipboardRequested = text => PostToUi(() => SetClipboardText(text));
            _terminal.ScrollPositionChanged = (_, _, _) => QueueUpdate();
            _changes.SynchronizedOutputChanged += OnSynchronizedOutputChanged;
        }

        _scrollBar = new ScrollBar { Orientation = Orientation.Vertical };
        _scrollBar.ValueChanged += OnScrollBarValueChanged;
        _surface = new TerminalSurface(this, _scrollBar);
        _surface.SetRowCount(_rows);
        Content = _surface;

        _cursorTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(CURSOR_BLINK_MS));
        _cursorTimer.Tick += OnCursorBlink;

        Cursor = Aprillz.MewUI.CursorType.IBeam;
        ApplyAppearance();
        RegisterCommands();
    }

    /// <summary>Occurs when the application rings the bell.</summary>
    public event Action? Bell;

    /// <summary>Occurs when the application sets a title, with the new title.</summary>
    public event Action<string>? TitleChanged;

    /// <summary>Occurs when the user Ctrl+clicks a hyperlink, with its URI.</summary>
    public event Action<string>? HyperlinkActivated;

    /// <summary>Occurs when the connection ends, with its exit code or -1.</summary>
    public event Action<int>? ConnectionClosed;

    /// <summary>Gets or sets the connection whose output this terminal shows; the terminal does not start or dispose it.</summary>
    public ITerminalConnection? Connection
    {
        get => GetValue(ConnectionProperty);
        set => SetValue(ConnectionProperty, value);
    }

    /// <summary>Gets or sets the number of rows of output kept above the screen; the default is 9001.</summary>
    public int ScrollbackLines
    {
        get => GetValue(ScrollbackLinesProperty);
        set => SetValue(ScrollbackLinesProperty, value);
    }

    /// <summary>Gets or sets the colors; the default is <see cref="TerminalColorScheme.Campbell"/>.</summary>
    public TerminalColorScheme ColorScheme
    {
        get => GetValue(ColorSchemeProperty);
        set => SetValue(ColorSchemeProperty, value);
    }

    /// <summary>Gets or sets the cursor shape shown until the application chooses one; the default is <see cref="TerminalCursorShape.Bar"/>.</summary>
    public TerminalCursorShape CursorShape
    {
        get => GetValue(CursorShapeProperty);
        set => SetValue(CursorShapeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether Option (Alt) with a key that types a character, such as Option+O typing ø on a Mac, sends ESC
    /// and the key's base character (Meta) instead of the character; the default is false.
    /// </summary>
    /// <remarks>Keys whose Alt combination types nothing of its own send ESC either way.</remarks>
    public bool OptionIsMeta
    {
        get => GetValue(OptionIsMetaProperty);
        set => SetValue(OptionIsMetaProperty, value);
    }

    /// <summary>Gets or sets the space between the edge and the cells, in DIPs; the default is 4.</summary>
    public double TextPadding
    {
        get => GetValue(TextPaddingProperty);
        set => SetValue(TextPaddingProperty, value);
    }

    /// <summary>Gets the title the application set (OSC 0 and 2), or an empty string.</summary>
    public string Title => GetValue(TitleProperty);

    /// <summary>Gets the working directory the shell reported (OSC 7 and OSC 9;9), or an empty string.</summary>
    public string WorkingDirectory => GetValue(WorkingDirectoryProperty);

    /// <summary>Gets the number of columns.</summary>
    public int Columns => _columns;

    /// <summary>Gets the number of rows.</summary>
    public int Rows => _rows;

    /// <summary>Gets or sets the columns the terminal asks for when it is measured.</summary>
    public int InitialColumns { get; set; } = 80;

    /// <summary>Gets or sets the rows the terminal asks for when it is measured.</summary>
    public int InitialRows { get; set; } = 24;

    internal double CellWidth => _renderer.CellWidth;

    internal double CellHeight => _renderer.CellHeight;

    /// <summary>Shows <paramref name="text"/> as if the connection had written it; callable from any thread.</summary>
    public void Write(string text)
    {
        using (_terminal.LockForWriting())
        {
            _terminal.Write(text);
        }

        QueueUpdate();
    }

    /// <summary>Copies the selection to the clipboard; returns false when nothing is selected.</summary>
    public bool Copy()
    {
        string text;
        using (_terminal.LockForReading())
        {
            text = _terminal.RetrieveSelectedTextFromBuffer(singleLine: false);
        }

        if (text.Length == 0)
        {
            return false;
        }

        SetClipboardText(text);
        return true;
    }

    /// <summary>Sends the clipboard text to the connection, as a bracketed paste when the application asked for one.</summary>
    public void Paste()
    {
        var clipboard = Application.IsRunning ? Application.Current.PlatformServices.Clipboard : null;
        if (clipboard != null && clipboard.TryGetText(out string text) && text.Length > 0)
        {
            PasteText(text);
        }
    }

    /// <summary>Sends <paramref name="text"/> to the connection as pasted text.</summary>
    public void PasteText(string text)
    {
        // Line breaks are sent as the Enter key sends them; bracketed paste lets the application tell paste from typing.
        string normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
        bool bracketed;
        using (_terminal.LockForWriting())
        {
            _terminal.TrySnapOnInput();
            bracketed = _terminal.IsXtermBracketedPasteModeEnabled;
        }

        SendInput(bracketed ? "\u001b[200~" + normalized + "\u001b[201~" : normalized);
        QueueUpdate();
    }

    /// <summary>Selects all the output.</summary>
    public void SelectAll()
    {
        using (_terminal.LockForWriting())
        {
            _terminal.SelectAll();
        }

        RefreshScreen();
        QueueUpdate();
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection()
    {
        using (_terminal.LockForWriting())
        {
            _terminal.ClearSelection();
        }

        RefreshScreen();
    }

    /// <summary>Gets the selected text, or an empty string.</summary>
    public string GetSelectedText()
    {
        using (_terminal.LockForReading())
        {
            return _terminal.RetrieveSelectedTextFromBuffer(singleLine: false);
        }
    }

    /// <summary>Scrolls the view by <paramref name="lines"/> rows; negative scrolls toward older output.</summary>
    public void ScrollLines(int lines)
    {
        using (_terminal.LockForWriting())
        {
            _terminal.UserScrollViewport(_terminal.GetScrollOffset() + lines);
        }

        QueueUpdate();
    }

    /// <summary>Scrolls to the newest output.</summary>
    public void ScrollToBottom()
    {
        using (_terminal.LockForWriting())
        {
            _terminal.UserScrollViewport(_terminal.ViewStartIndex);
        }

        QueueUpdate();
    }

    /// <summary>Gets the text of a visible row, without trailing spaces.</summary>
    public string GetRowText(int row)
    {
        using (_terminal.LockForReading())
        {
            var buffer = _terminal.GetTextBuffer();
            int bufferRow = _terminal.GetViewport().Top + row;
            if (row < 0 || row >= _rows || bufferRow >= buffer.TotalRowCount)
            {
                return string.Empty;
            }

            return buffer.GetRowByOffset(bufferRow).GetText().ToString().TrimEnd(' ');
        }
    }

    /// <summary>Brings the view up to date with what has been written; the UI thread does this by itself shortly after output.</summary>
    public void Flush() => UpdateFromTerminal();

    protected override void OnDispose()
    {
        Connection = null;
        _cursorTimer.Stop();
        _cursorTimer.Dispose();
        base.OnDispose();
    }

    protected override void OnFontCacheInvalidated(MewProperty property)
    {
        base.OnFontCacheInvalidated(property);
        _surface.InvalidateMeasure();
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _surface.InvalidateMeasure();
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        string? output;
        using (_terminal.LockForWriting())
        {
            output = _terminal.FocusChanged(true);
        }

        SendInput(output);
        RestartCursorBlink();
        _surface.CursorVisual.InvalidateVisual();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        string? output;
        using (_terminal.LockForWriting())
        {
            output = _terminal.FocusChanged(false);
        }

        SendInput(output);
        _composition = null;
        _cursorTimer.Stop();
        _cursorBlinkOn = true;
        ArrangeCursor();
        _surface.CursorVisual.InvalidateVisual();
    }

    internal void EnsureMetrics()
    {
        if (!Application.IsRunning)
        {
            return;
        }

        if (_renderer.UpdateMetrics(GetGraphicsFactory().TextEngine, GetTextRunStyle(), GetDpi()))
        {
            _areaWidth = -1;
            InvalidateRows();
        }
    }

    internal void OnTextAreaArranged(double width, double height)
    {
        if (width == _areaWidth && height == _areaHeight)
        {
            return;
        }

        _areaWidth = width;
        _areaHeight = height;
        if (_gridUpdateQueued)
        {
            return;
        }

        // Changing the grid adds and removes row elements, which must not happen during arrange.
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher == null)
        {
            UpdateGridFromArea();
        }
        else
        {
            _gridUpdateQueued = true;
            dispatcher.BeginInvoke(UpdateGridFromArea);
        }
    }

    internal void ReleaseRow(TerminalRowVisual row) => _renderer.ReleaseOwner(row);

    internal void RenderBackground(IGraphicsContext context, Rect bounds)
    {
        uint background = _settingsSnapshot.GetAttributeColors(default(TextAttribute)).Background;
        context.FillRectangle(bounds, TerminalRenderer.ToColor(background));
    }

    internal void RenderRow(IGraphicsContext context, int index, Rect bounds, object owner)
    {
        if (index >= _snapshots.Count || !_snapshots[index].HasRow)
        {
            return;
        }

        var snapshot = _snapshots[index];
        _renderer.RenderRow(context, snapshot.Row, bounds, _settingsSnapshot, snapshot.Selection, snapshot.Highlights, owner);
    }

    /// <summary>Copies the visible rows that changed, and the cursor and colors, for drawing outside the terminal lock.</summary>
    private void TakeSnapshots(int visibleTop, int firstRow, int lastRow)
    {
        var buffer = _terminal.GetTextBuffer();
        int width = buffer.Size.Width;
        while (_snapshots.Count < _rows)
        {
            _snapshots.Add(new RowSnapshot());
        }

        var selectionSpans = _terminal.GetSelectionSpans();
        var searchHighlights = _terminal.GetSearchHighlights();
        var focused = _terminal.GetSearchHighlightFocused();

        for (int index = firstRow; index < lastRow && index < _rows; index++)
        {
            var snapshot = _snapshots[index];
            int bufferRow = visibleTop + index;
            if (bufferRow < 0 || bufferRow >= buffer.TotalRowCount)
            {
                snapshot.HasRow = false;
                continue;
            }

            if (snapshot.Row.Size != width)
            {
                snapshot.Row = new Row(width, default);
            }

            snapshot.Row.CopyFrom(buffer.GetRowByOffset(bufferRow));
            snapshot.HasRow = true;
            snapshot.Selection = SpansOnRow(selectionSpans, bufferRow, width);
            snapshot.Highlights.Clear();
            foreach (var (start, end) in SpansOnRow(searchHighlights, bufferRow, width))
            {
                bool isFocused = focused is TilPointSpan focusedSpan && focusedSpan.Start.Y <= bufferRow && focusedSpan.End.Y >= bufferRow
                    && start < (focusedSpan.End.Y == bufferRow ? focusedSpan.End.X : int.MaxValue)
                    && end > (focusedSpan.Start.Y == bufferRow ? focusedSpan.Start.X : int.MinValue);
                snapshot.Highlights.Add((start, end, isFocused));
            }
        }

        _settingsSnapshot.CopyFrom(_terminal.RenderSettings);

        var cursor = buffer.Cursor;
        var position = cursor.Position;
        var cursorRow = buffer.GetRowByOffset(position.Y);
        var attr = cursorRow.GetAttrByColumn(position.X);
        var (foreground, background) = _settingsSnapshot.GetAttributeColors(attr);
        uint cursorColorRef = _settingsSnapshot.GetColorTableEntry(TextColor.CURSOR_COLOR);
        _cursorSnapshot = new CursorSnapshot(
            cursor.Type,
            cursor.Size,
            cursorRow.DbcsAttrAt(position.X) == DbcsAttribute.Leading ? 2 : 1,
            cursorRow.GlyphAt(position.X).ToString(),
            cursorColorRef == TextAttribute.INVALID_COLOR ? TerminalRenderer.ToColor(foreground) : TerminalRenderer.ToColor(cursorColorRef),
            TerminalRenderer.ToColor(background),
            _settingsSnapshot.GetRenderMode(RenderSettings.Mode.IntenseIsBold) && attr.IsIntense(),
            attr.IsItalic());
    }

    /// <summary>The column ranges [start, end) of <paramref name="spans"/> on one buffer row; span ends are exclusive.</summary>
    private static List<(int Start, int End)> SpansOnRow(List<TilPointSpan> spans, int bufferRow, int width)
    {
        var result = new List<(int Start, int End)>();
        foreach (var span in spans)
        {
            if (bufferRow < span.Start.Y || bufferRow > span.End.Y)
            {
                continue;
            }

            int start = span.Start.Y == bufferRow ? span.Start.X : 0;
            int end = span.End.Y == bufferRow ? span.End.X : width;
            if (end > start)
            {
                result.Add((start, end));
            }
        }

        return result;
    }

    internal void RenderCursor(IGraphicsContext context, Rect bounds)
    {
        if (bounds.Width <= 0)
        {
            return;
        }

        if (_composition != null)
        {
            var defaults = _settingsSnapshot.GetAttributeColors(default(TextAttribute));
            _renderer.RenderComposition(context, bounds.Position, _composition, TerminalRenderer.ToColor(defaults.Foreground), TerminalRenderer.ToColor(defaults.Background));
            return;
        }

        var snapshot = _cursorSnapshot;
        var type = snapshot.Type;
        var cell = bounds;
        if (type == CursorType.Legacy)
        {
            // The console's cursor: a box of the cursor size's percentage of the cell, at the bottom.
            double height = Math.Max(1, CellHeight * snapshot.Size / 100.0);
            cell = new Rect(bounds.X, bounds.Bottom - height, bounds.Width, height);
            type = CursorType.FullBox;
        }

        string? glyph = type == CursorType.FullBox && cell == bounds ? snapshot.Glyph : null;
        _renderer.RenderCursor(context, cell, type, IsFocused, snapshot.Color, glyph, snapshot.GlyphColor, snapshot.Bold, snapshot.Italic, snapshot.Span);
    }

    /// <summary>Places the cursor element on the cursor cell, or hides it.</summary>
    internal void ArrangeCursor()
    {
        bool shown = _cursorCell.Y >= 0 && _cursorCell.Y < _rows && (_cursorBlinkOn || !IsFocused || _composition != null);
        if (!shown)
        {
            _surface.CursorVisual.Arrange(new Rect(_surface.TextArea.X, _surface.TextArea.Y, 0, 0));
            return;
        }

        int span = _composition != null ? _renderer.MeasureCompositionCells(_composition) : _cursorSnapshot.Span;
        _surface.CursorVisual.Arrange(_surface.CellRect(_cursorCell.X, _cursorCell.Y, span));
    }

    /// <summary>A visible row as of the last update, with its selection and search highlights.</summary>
    private sealed class RowSnapshot
    {
        public Row Row = new(1, default);
        public bool HasRow;
        public List<(int Start, int End)> Selection = new();
        public readonly List<(int Start, int End, bool Focused)> Highlights = new();
    }

    /// <summary>The cursor as of the last update.</summary>
    private readonly record struct CursorSnapshot(CursorType Type, int Size, int Span, string Glyph, Color Color, Color GlyphColor, bool Bold, bool Italic);

    private void RegisterCommands()
    {
        Commands.Register(StandardCommands.Copy, () => Copy(), () => HasSelection());
        Commands.Register(StandardCommands.Paste, Paste);
        Commands.Register(StandardCommands.SelectAll, SelectAll);

        // Ctrl+C and Ctrl+V belong to the application, so the terminal copies and pastes with Shift added, or with Cmd on macOS.
        if (PlatformConventions.Current.PrimaryModifier == ModifierKeys.Meta)
        {
            MapCommand(StandardCommands.Copy, "Meta+C");
            MapCommand(StandardCommands.Paste, "Meta+V");
        }
        else
        {
            MapCommand(StandardCommands.Copy, "Ctrl+Shift+C", "Ctrl+Insert");
            MapCommand(StandardCommands.Paste, "Ctrl+Shift+V", "Shift+Insert");
        }
    }

    private readonly List<KeyGesture> _commandGestures = new();

    private void MapCommand(Command command, string primary, params string[] alternatives)
    {
        var alternativeSequences = new KeySequence[alternatives.Length];
        for (int index = 0; index < alternatives.Length; index++)
        {
            alternativeSequences[index] = KeySequence.Parse(alternatives[index]);
            _commandGestures.Add(KeyGesture.Parse(alternatives[index]));
        }

        InputMap.Map(command, KeySequence.Parse(primary), alternativeSequences);
        _commandGestures.Add(KeyGesture.Parse(primary));
    }

    private bool HasSelection()
    {
        using (_terminal.LockForReading())
        {
            return _terminal.IsSelectionActive;
        }
    }

    private void OnConnectionChanged(ITerminalConnection? oldValue, ITerminalConnection? newValue)
    {
        if (oldValue != null)
        {
            oldValue.Output -= OnConnectionOutput;
            oldValue.Closed -= OnConnectionClosed;
        }

        if (newValue != null)
        {
            newValue.Output += OnConnectionOutput;
            newValue.Closed += OnConnectionClosed;
            newValue.Resize(_columns, _rows);
        }
    }

    private void OnConnectionOutput(ReadOnlySpan<byte> data)
    {
        string text;
        lock (_decodeLock)
        {
            int needed = _decoder.GetCharCount(data, false);
            if (_decodeBuffer.Length < needed)
            {
                _decodeBuffer = new char[Math.Max(needed, _decodeBuffer.Length * 2)];
            }

            int count = _decoder.GetChars(data, _decodeBuffer, false);
            if (count == 0)
            {
                return;
            }

            // The parser may keep a reference to an unfinished sequence, so each block is its own string.
            text = new string(_decodeBuffer, 0, count);
        }

        using (_terminal.LockForWriting())
        {
            _terminal.Write(text);
        }

        QueueUpdate();
    }

    private void OnConnectionClosed(int exitCode) => PostToUi(() => ConnectionClosed?.Invoke(exitCode));

    /// <summary>Sends text the terminal produced (keys, reports) to the connection as UTF-8.</summary>
    private void SendInput(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var connection = Connection;
        if (connection != null)
        {
            connection.Write(Encoding.UTF8.GetBytes(text));
        }
    }

    private void OnScrollbackLinesChanged(int lines)
    {
        using (_terminal.LockForWriting())
        {
            _terminal.SetScrollbackLines(lines);
        }

        QueueUpdate();
    }

    private void ApplyAppearance()
    {
        var scheme = ColorScheme;
        var settings = new TerminalSettings
        {
            HistorySize = ScrollbackLines,
            InitialCols = _columns,
            InitialRows = _rows,
            CursorShape = CursorShape,
            DefaultForeground = TerminalColorScheme.ToColorRef(scheme.Foreground),
            DefaultBackground = TerminalColorScheme.ToColorRef(scheme.Background),
            CursorColor = TerminalColorScheme.ToColorRef(scheme.Cursor),
            SelectionBackground = TerminalColorScheme.ToColorRef(scheme.SelectionBackground),
            ColorTable = scheme.AnsiColors.Select(TerminalColorScheme.ToColorRef).ToArray(),
        };

        using (_terminal.LockForWriting())
        {
            _terminal.UpdateSettings(settings);
        }

        RefreshScreen();
        _surface.InvalidateVisual();
    }

    private void OnTitleChanged(string title)
    {
        SetValue(TitlePropertyKey, title);
        TitleChanged?.Invoke(title);
    }

    private void OnSynchronizedOutputChanged(bool synchronizing)
    {
        if (!synchronizing)
        {
            QueueUpdate();
        }
    }

    /// <summary>Queues one UI update for all changes since the last; any thread.</summary>
    private void QueueUpdate()
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) != 0)
        {
            return;
        }

        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher == null)
        {
            _updateQueued = 0;
            return;
        }

        // Under a flood of output the screen is taken at most once a frame, so drawing does not hold up parsing.
        long elapsed = Environment.TickCount64 - Interlocked.Read(ref _lastUpdateTicks);
        if (elapsed >= UPDATE_INTERVAL_MS)
        {
            dispatcher.BeginInvoke(DispatcherPriority.Render, UpdateFromTerminal);
        }
        else
        {
            Task.Delay((int)(UPDATE_INTERVAL_MS - elapsed)).ContinueWith(
                _ => dispatcher.BeginInvoke(DispatcherPriority.Render, UpdateFromTerminal), TaskScheduler.Default);
        }
    }

    private static void PostToUi(Action action)
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>Takes what changed in the terminal: invalidates the changed rows, moves the cursor and the scroll bar.</summary>
    private void UpdateFromTerminal()
    {
        Interlocked.Exchange(ref _lastUpdateTicks, Environment.TickCount64);
        Interlocked.Exchange(ref _updateQueued, 0);

        bool allDirty;
        int dirtyTop;
        int dirtyBottom;
        int visibleTop;
        int scrollTop;
        int scrollHeight;
        int bufferHeight;
        TilPoint cursorCell;
        string workingDirectory;
        using (_terminal.LockForWriting())
        {
            // Under synchronized output (mode 2026) the screen is shown only when the application finishes it.
            if (_terminal.RenderSettings.GetRenderMode(RenderSettings.Mode.SynchronizedOutput))
            {
                return;
            }

            var viewport = _terminal.GetViewport();
            visibleTop = viewport.Top;
            allDirty = _refreshAll || _changes.IsAllDirty || _changes.IsSelectionDirty || visibleTop != _visibleTop || _snapshots.Count != _rows;
            _refreshAll = false;
            (dirtyTop, dirtyBottom) = _changes.DirtyRows;
            _changes.Clear();

            if (allDirty)
            {
                TakeSnapshots(visibleTop, 0, _rows);
            }
            else
            {
                TakeSnapshots(visibleTop, Math.Max(0, dirtyTop - visibleTop), Math.Min(_rows, dirtyBottom - visibleTop));
            }

            scrollTop = visibleTop;
            scrollHeight = viewport.Height;
            bufferHeight = _terminal.BufferHeight;

            var cursor = _terminal.GetTextBuffer().Cursor;
            var relative = cursor.Position - viewport.Origin;
            bool visible = cursor.IsVisible && relative.Y >= 0 && relative.Y < viewport.Height;
            cursorCell = visible ? relative : new TilPoint(0, -1);
            workingDirectory = _terminal.WorkingDirectory;
        }

        _visibleTop = visibleTop;
        if (allDirty)
        {
            InvalidateRows();
        }
        else
        {
            int first = Math.Max(0, dirtyTop - visibleTop);
            int last = Math.Min(_rows, dirtyBottom - visibleTop);
            for (int index = first; index < last; index++)
            {
                _surface.Rows[index].InvalidateVisual();
            }
        }

        if (cursorCell != _cursorCell)
        {
            _cursorCell = cursorCell;
            RestartCursorBlink();
        }

        ArrangeCursor();
        _surface.CursorVisual.InvalidateVisual();

        _settingScroll = true;
        _scrollBar.Minimum = 0;
        _scrollBar.Maximum = Math.Max(0, bufferHeight - scrollHeight);
        _scrollBar.ViewportSize = scrollHeight;
        _scrollBar.SmallChange = 1;
        _scrollBar.LargeChange = Math.Max(1, scrollHeight - 1);
        _scrollBar.Value = Math.Min(scrollTop, _scrollBar.Maximum);
        _settingScroll = false;

        if (workingDirectory != WorkingDirectory)
        {
            SetValue(WorkingDirectoryPropertyKey, workingDirectory);
        }
    }

    private void UpdateGridFromArea()
    {
        _gridUpdateQueued = false;
        if (_areaWidth <= 0 || _areaHeight <= 0)
        {
            return;
        }

        int columns = Math.Max(TerminalCore.MINIMUM_VISIBLE_CELLS, (int)Math.Floor((_areaWidth + 0.01) / CellWidth));
        int rows = Math.Max(TerminalCore.MINIMUM_VISIBLE_CELLS, (int)Math.Floor((_areaHeight + 0.01) / CellHeight));
        if (columns == _columns && rows == _rows)
        {
            return;
        }

        _columns = columns;
        _rows = rows;
        using (_terminal.LockForWriting())
        {
            _terminal.UserResize(new TilSize(columns, rows));
        }

        _surface.SetRowCount(rows);
        Connection?.Resize(columns, rows);
        InvalidateRows();
        QueueUpdate();
    }

    /// <summary>Takes every visible row again and redraws it, after a change the terminal does not report (selection, colors).</summary>
    private void RefreshScreen()
    {
        _refreshAll = true;
        UpdateFromTerminal();
    }

    private void InvalidateRows()
    {
        foreach (var row in _surface.Rows)
        {
            row.InvalidateVisual();
        }

        _surface.CursorVisual.InvalidateVisual();
    }

    private void OnScrollBarValueChanged(double value)
    {
        if (_settingScroll)
        {
            return;
        }

        using (_terminal.LockForWriting())
        {
            _terminal.UserScrollViewport((int)Math.Round(value));
        }

        QueueUpdate();
    }

    private void RestartCursorBlink()
    {
        _cursorBlinkOn = true;
        _cursorTimer.Stop();
        if (IsFocused)
        {
            _cursorTimer.Start();
        }
    }

    private void OnCursorBlink()
    {
        bool blinking;
        using (_terminal.LockForReading())
        {
            blinking = _terminal.GetTextBuffer().Cursor.IsBlinking;
        }

        if (!blinking)
        {
            if (!_cursorBlinkOn)
            {
                _cursorBlinkOn = true;
                ArrangeCursor();
            }

            return;
        }

        _cursorBlinkOn = !_cursorBlinkOn;
        ArrangeCursor();
        _surface.CursorVisual.InvalidateVisual();
    }

    private static void SetClipboardText(string text)
    {
        var clipboard = Application.IsRunning ? Application.Current.PlatformServices.Clipboard : null;
        clipboard?.TrySetText(text);
    }
}
