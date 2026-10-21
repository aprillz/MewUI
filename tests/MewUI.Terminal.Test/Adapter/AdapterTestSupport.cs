// Ported from Windows Terminal src/terminal/adapter/ut_adapter/adapterTest.cpp (MIT).
// The mock terminal (TestGetSet) and the helpers the adapter tests share.

using System.Reflection;

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Adapter;

internal enum CursorY
{
    Top,
    Bottom,
    YCenter,
}

internal enum CursorX
{
    Left,
    Right,
    XCenter,
}

internal enum CursorDirection
{
    Up = 0,
    Down = 1,
    Right = 2,
    Left = 3,
    NextLine = 4,
    PrevLine = 5,
}

internal enum AbsolutePosition
{
    CursorHorizontal = 0,
    VerticalLine = 1,
}

/// <summary>The Windows console attribute bits that the original tests build their starting attributes from.</summary>
internal static class LegacyAttributes
{
    public const int FOREGROUND_BLUE = 0x0001;
    public const int FOREGROUND_GREEN = 0x0002;
    public const int FOREGROUND_RED = 0x0004;
    public const int FOREGROUND_INTENSITY = 0x0008;
    public const int BACKGROUND_BLUE = 0x0010;
    public const int BACKGROUND_GREEN = 0x0020;
    public const int BACKGROUND_RED = 0x0040;
    public const int BACKGROUND_INTENSITY = 0x0080;
    public const int COMMON_LVB_GRID_HORIZONTAL = 0x0400;
    public const int COMMON_LVB_REVERSE_VIDEO = 0x4000;

    public const int FG_ATTRS = FOREGROUND_BLUE | FOREGROUND_GREEN | FOREGROUND_RED | FOREGROUND_INTENSITY;
    public const int BG_ATTRS = BACKGROUND_BLUE | BACKGROUND_GREEN | BACKGROUND_RED | BACKGROUND_INTENSITY;

    // The meta bits a TextAttribute keeps: everything but the lead and trail byte flags.
    private const int USED_META_ATTRS = 0xDC00;

    public const int DEFAULT_FILL = FOREGROUND_BLUE | FOREGROUND_GREEN | FOREGROUND_RED;

    /// <summary>The TextAttribute(WORD) constructor of the original: legacy colors become ANSI indexes, 7 and 0 become the defaults.</summary>
    public static TextAttribute Create(int legacy)
    {
        var attributes = (CharacterAttributes)(legacy & USED_META_ATTRS);
        var foreground = CreateColor(legacy & FG_ATTRS, 7);
        var background = CreateColor((legacy & BG_ATTRS) >> 4, 0);
        return new TextAttribute(attributes, foreground, background, 0, default);
    }

    /// <summary>Swaps bits 0 and 2 to convert between the Windows and the ANSI order of the 16 colors.</summary>
    public static byte TransposeIndex(int index)
    {
        int oneBitSet = (index ^ (index >> 2)) & 1;
        return (byte)(index ^ oneBitSet ^ (oneBitSet << 2));
    }

    private static TextColor CreateColor(int legacyIndex, int defaultIndex)
    {
        if (legacyIndex == defaultIndex)
        {
            return default;
        }
        else
        {
            return new TextColor(TransposeIndex(legacyIndex), true);
        }
    }
}

/// <summary>A viewport the tests move around; the original keeps it as an inclusive rect but hands it out as an exclusive one.</summary>
internal sealed class MutableViewport
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal sealed class TestGetSet : ITerminalApi
{
    private readonly HashSet<ITerminalApi.Mode> _systemMode = [ITerminalApi.Mode.AutoWrap];

    public StateMachine? Machine { get; set; }

    public Renderer Renderer { get; } = new();

    public RenderSettings RenderSettings { get; } = new();

    public TextBuffer ScreenBuffer { get; set; }

    public MutableViewport Viewport { get; } = new();

    public string Response { get; set; } = string.Empty;

    public bool RetainResponse { get; set; }

    public TilPoint ExpectedCursorPos { get; set; }

    public TextAttribute ExpectedAttribute { get; set; }

    public bool ReturnResponseResult { get; set; }

    public bool SetWindowTitleResult { get; set; }

    public string ExpectedWindowTitle { get; set; } = string.Empty;

    public bool ExpectedShowWindow { get; set; }

    public string ExpectedMenuJson { get; set; } = string.Empty;

    public int ExpectedReplaceLength { get; set; }

    public TestGetSet()
    {
        ScreenBuffer = new TextBuffer(new TilSize(100, 600), default, 0, false, Renderer);
    }

    public void ReturnResponse(ReadOnlySpan<char> response)
    {
        if (!ReturnResponseResult)
        {
            throw new InvalidOperationException("ReturnResponse failed.");
        }

        if (RetainResponse)
        {
            Response += response.ToString();
        }
        else
        {
            Response = response.ToString();
        }
    }

    public bool IsConPTY => false;

    public StateMachine GetStateMachine() => Machine ?? throw new InvalidOperationException("No state machine.");

    public BufferState GetBufferAndViewport()
        => new(ScreenBuffer, new TilRect(Viewport.Left, Viewport.Top, Viewport.Right, Viewport.Bottom), true);

    public void SetViewportPosition(TilPoint position)
    {
    }

    public bool IsVtInputEnabled => false;

    public void SetSystemMode(ITerminalApi.Mode mode, bool enabled)
    {
        if (enabled)
        {
            _systemMode.Add(mode);
        }
        else
        {
            _systemMode.Remove(mode);
        }
    }

    public bool GetSystemMode(ITerminalApi.Mode mode) => _systemMode.Contains(mode);

    public void ReturnAnswerback()
    {
    }

    public void WarningBell()
    {
    }

    public void SetWindowTitle(ReadOnlySpan<char> title)
    {
        if (SetWindowTitleResult)
        {
            Assert.AreEqual(ExpectedWindowTitle, title.ToString());
        }
    }

    public void UseAlternateScreenBuffer(TextAttribute attributes)
    {
    }

    public void UseMainScreenBuffer()
    {
    }

    public CursorType GetUserDefaultCursorStyle() => CursorType.Legacy;

    public void ShowWindow(bool showOrHide) => Assert.AreEqual(ExpectedShowWindow, showOrHide);

    public void CopyToClipboard(string content)
    {
    }

    public void SetTaskbarProgress(TaskbarState state, int progress)
    {
    }

    public void SetWorkingDirectory(ReadOnlySpan<char> uri)
    {
    }

    public void PlayMidiNote(int noteNumber, int velocity, TimeSpan duration)
    {
    }

    public bool ResizeWindow(int width, int height) => true;

    public void NotifyBufferRotation(int delta)
    {
    }

    public void NotifyShellIntegrationMark()
    {
    }

    public void InvokeCompletions(ReadOnlySpan<char> menuJson, int replaceLength)
    {
        Assert.AreEqual(ExpectedMenuJson, menuJson.ToString());
        Assert.AreEqual(ExpectedReplaceLength, replaceLength);
    }

    public void SearchMissingCommand(ReadOnlySpan<char> command)
    {
    }

    public void ShowNotification(ReadOnlySpan<char> title, ReadOnlySpan<char> body)
    {
    }

    public void PrepData() => PrepData(CursorDirection.Up);

    public void PrepData(CursorDirection direction)
    {
        switch (direction)
        {
            case CursorDirection.Up:
                PrepData(CursorX.Left, CursorY.Top);
                break;
            case CursorDirection.Down:
                PrepData(CursorX.Left, CursorY.Bottom);
                break;
            case CursorDirection.Left:
                PrepData(CursorX.Left, CursorY.Top);
                break;
            case CursorDirection.Right:
                PrepData(CursorX.Right, CursorY.Top);
                break;
            case CursorDirection.NextLine:
                PrepData(CursorX.Left, CursorY.Bottom);
                break;
            case CursorDirection.PrevLine:
                PrepData(CursorX.Left, CursorY.Top);
                break;
        }
    }

    public void PrepData(CursorX xAction, CursorY yAction)
    {
        // APIs succeed by default.
        ReturnResponseResult = true;

        ScreenBuffer = new TextBuffer(new TilSize(100, 600), default, 0, false, Renderer);

        // The viewport sits in the middle of the buffer, so every side has excess buffer around it.
        Viewport.Top = 20;
        Viewport.Bottom = 49;
        Viewport.Left = 30;
        Viewport.Right = 59;

        PrepCursor(xAction, yAction);

        // The default attribute is gray on black.
        ScreenBuffer.CurrentAttributes = LegacyAttributes.Create(LegacyAttributes.DEFAULT_FILL);
        ExpectedAttribute = ScreenBuffer.CurrentAttributes;

        Response = string.Empty;
        RetainResponse = false;
    }

    public void PrepCursor(CursorX xAction, CursorY yAction)
    {
        int bufferWidth = ScreenBuffer.Size.Dimensions.Width;
        int cursorX = 0;
        int cursorY = 0;

        switch (xAction)
        {
            case CursorX.Left:
                cursorX = 0;
                break;
            case CursorX.Right:
                cursorX = bufferWidth - 1;
                break;
            case CursorX.XCenter:
                cursorX = bufferWidth / 2;
                break;
        }

        switch (yAction)
        {
            case CursorY.Top:
                cursorY = Viewport.Top;
                break;
            case CursorY.Bottom:
                cursorY = Viewport.Bottom - 1;
                break;
            case CursorY.YCenter:
                cursorY = Viewport.Top + ((Viewport.Bottom - Viewport.Top) / 2);
                break;
        }

        var cursorPosition = new TilPoint(cursorX, cursorY);
        ScreenBuffer.Cursor.SetPosition(cursorPosition);
        ExpectedCursorPos = cursorPosition;
    }

    /// <summary>Builds the sub-parameter list and the ranges of the parameters; <see cref="OMITTED"/> stands for an empty sub-parameter.</summary>
    public static (VTParameter[] SubParams, (byte Start, byte End)[] Ranges) MakeSubParamsAndRanges(params int[][] subParamList)
    {
        var subParams = new List<VTParameter>();
        var ranges = new List<(byte Start, byte End)>();
        foreach (var list in subParamList)
        {
            int begin = subParams.Count;
            foreach (int value in list)
            {
                subParams.Add(value == OMITTED ? VTParameter.Omitted : new VTParameter(value));
            }

            ranges.Add(((byte)begin, (byte)(begin + list.Length)));
        }

        return (subParams.ToArray(), ranges.ToArray());
    }

    public const int OMITTED = -1;

    public void ValidateExpectedCursorPos() => Assert.AreEqual(ExpectedCursorPos, ScreenBuffer.Cursor.Position);

    public void ValidateExpectedAttributes() => Assert.AreEqual(ExpectedAttribute, ScreenBuffer.CurrentAttributes);

    public void ValidateInputEvent(string expectedResponse) => Assert.AreEqual(expectedResponse, Response);
}

/// <summary>Reads the private state of the dispatcher that the original tests inspect directly.</summary>
internal static class DispatchInspector
{
    private const BindingFlags PRIVATE_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;

    private static FieldInfo Field(string name)
        => typeof(AdaptDispatch).GetField(name, PRIVATE_INSTANCE) ?? throw new InvalidOperationException("Missing field " + name);

    public static PageManager Pages(AdaptDispatch dispatch) => (PageManager)Field("_pages").GetValue(dispatch)!;

    public static TerminalOutput TermOutput(AdaptDispatch dispatch) => (TerminalOutput)Field("_termOutput").GetValue(dispatch)!;

    /// <summary>The top and bottom scroll margins as stored: 0-based, and both 0 when unset.</summary>
    public static (int Top, int Bottom) VerticalMargins(AdaptDispatch dispatch)
        => ((int)Field("_scrollMarginTop").GetValue(dispatch)!, (int)Field("_scrollMarginBottom").GetValue(dispatch)!);

    public static void ClearVerticalMargins(AdaptDispatch dispatch)
    {
        Field("_scrollMarginTop").SetValue(dispatch, 0);
        Field("_scrollMarginBottom").SetValue(dispatch, 0);
    }

    /// <summary>Whether the private Mode flag with the given name (Origin, InsertReplace...) is set.</summary>
    public static bool HasMode(AdaptDispatch dispatch, string modeName)
    {
        var field = Field("_modes");
        var flag = (Enum)Enum.Parse(field.FieldType, modeName);
        return ((Enum)field.GetValue(dispatch)!).HasFlag(flag);
    }
}
