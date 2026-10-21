// Ported from Windows Terminal src/terminal/adapter/DispatchTypes.hpp (MIT).

using Aprillz.MewUI.Terminal.Parser;

namespace Aprillz.MewUI.Terminal.Adapter;

internal static class DispatchTypes
{
    // Mode and status numbers of the DEC private (CSI ?) set carry this flag, to tell them from the ANSI ones.
    public const int DEC_PRIVATE = 0x01000000;

    public const int DECCOLM_SET_COLUMNS = 132;
    public const int DECCOLM_RESET_COLUMNS = 80;

    public static ModeParams AnsiStandardMode(int value) => (ModeParams)value;

    public static ModeParams DecPrivateMode(int value) => (ModeParams)(value | DEC_PRIVATE);

    public static StatusType AnsiStandardStatus(int value) => (StatusType)value;

    public static StatusType DecPrivateStatus(int value) => (StatusType)(value | DEC_PRIVATE);

    /// <summary>The bare number of a mode, without the DEC private flag.</summary>
    public static int ModeNumber(ModeParams mode) => (int)mode & ~DEC_PRIVATE;

    public static bool IsDecPrivate(ModeParams mode) => ((int)mode & DEC_PRIVATE) != 0;

    public static readonly VTID CHARSET_DEC_SPECIAL_GRAPHICS = VTID.FromString("0");
    public static readonly VTID CHARSET_ASCII = VTID.FromString("B");
    public static readonly VTID CODING_SYSTEM_ISO2022 = VTID.FromString("@");
    public static readonly VTID CODING_SYSTEM_UTF8 = VTID.FromString("G");
}

internal enum ColorItem
{
    NormalText = 1,
    WindowFrame = 2,
}

internal enum ColorModel
{
    HLS = 1,
    RGB = 2,
}

internal enum EraseType
{
    ToEnd = 0,
    FromBeginning = 1,
    All = 2,
    Scrollback = 3,
}

internal enum ChangeExtent
{
    Default = 0,
    Stream = 1,
    Rectangle = 2,
}

internal enum TaskbarState
{
    Clear = 0,
    Set = 1,
    Error = 2,
    Indeterminate = 3,
    Paused = 4,
}

internal enum GraphicsOptions
{
    Off = 0,
    Intense = 1,

    // Also Faint, decreased intensity.
    RGBColorOrFaint = 2,
    Italics = 3,

    // Also the extended underline styles SGR 4:x.
    Underline = 4,

    // Also Blink.
    BlinkOrXterm256Index = 5,
    RapidBlink = 6,
    Negative = 7,
    Invisible = 8,
    CrossedOut = 9,
    DoublyUnderlined = 21,
    NotIntenseOrFaint = 22,
    NotItalics = 23,
    NoUnderline = 24,
    Steady = 25,
    Positive = 27,
    Visible = 28,
    NotCrossedOut = 29,
    ForegroundBlack = 30,
    ForegroundRed = 31,
    ForegroundGreen = 32,
    ForegroundYellow = 33,
    ForegroundBlue = 34,
    ForegroundMagenta = 35,
    ForegroundCyan = 36,
    ForegroundWhite = 37,
    ForegroundExtended = 38,
    ForegroundDefault = 39,
    BackgroundBlack = 40,
    BackgroundRed = 41,
    BackgroundGreen = 42,
    BackgroundYellow = 43,
    BackgroundBlue = 44,
    BackgroundMagenta = 45,
    BackgroundCyan = 46,
    BackgroundWhite = 47,
    BackgroundExtended = 48,
    BackgroundDefault = 49,
    Overline = 53,
    NoOverline = 55,
    UnderlineColor = 58,
    UnderlineColorDefault = 59,
    BrightForegroundBlack = 90,
    BrightForegroundRed = 91,
    BrightForegroundGreen = 92,
    BrightForegroundYellow = 93,
    BrightForegroundBlue = 94,
    BrightForegroundMagenta = 95,
    BrightForegroundCyan = 96,
    BrightForegroundWhite = 97,
    BrightBackgroundBlack = 100,
    BrightBackgroundRed = 101,
    BrightBackgroundGreen = 102,
    BrightBackgroundYellow = 103,
    BrightBackgroundBlue = 104,
    BrightBackgroundMagenta = 105,
    BrightBackgroundCyan = 106,
    BrightBackgroundWhite = 107,
}

internal enum LogicalAttributeOptions
{
    Default = 0,
    Protected = 1,
    Unprotected = 2,
}

internal enum SgrSaveRestoreStackOptions
{
    All = 0,
    Intense = 1,
    Faintness = 2,
    Italics = 3,
    Underline = 4,
    Blink = 5,
    Negative = 7,
    Invisible = 8,
    CrossedOut = 9,
    DoublyUnderlined = 21,
    SaveForegroundColor = 30,
    SaveBackgroundColor = 31,
    Max = SaveBackgroundColor,
}

internal enum StatusType
{
    OperatingStatus = 5,
    CursorPositionReport = 6,
    ExtendedCursorPositionReport = DispatchTypes.DEC_PRIVATE | 6,
    PrinterStatus = DispatchTypes.DEC_PRIVATE | 15,
    UserDefinedKeys = DispatchTypes.DEC_PRIVATE | 25,
    KeyboardStatus = DispatchTypes.DEC_PRIVATE | 26,
    LocatorStatus = DispatchTypes.DEC_PRIVATE | 55,
    LocatorIdentity = DispatchTypes.DEC_PRIVATE | 56,
    MacroSpaceReport = DispatchTypes.DEC_PRIVATE | 62,
    MemoryChecksum = DispatchTypes.DEC_PRIVATE | 63,
    DataIntegrity = DispatchTypes.DEC_PRIVATE | 75,
    MultipleSessionStatus = DispatchTypes.DEC_PRIVATE | 85,
}

internal enum ModeParams
{
    IRM_InsertReplaceMode = 4,
    LNM_LineFeedNewLineMode = 20,
    DECCKM_CursorKeysMode = DispatchTypes.DEC_PRIVATE | 1,
    DECANM_AnsiMode = DispatchTypes.DEC_PRIVATE | 2,
    DECCOLM_SetNumberOfColumns = DispatchTypes.DEC_PRIVATE | 3,
    DECSCNM_ScreenMode = DispatchTypes.DEC_PRIVATE | 5,
    DECOM_OriginMode = DispatchTypes.DEC_PRIVATE | 6,
    DECAWM_AutoWrapMode = DispatchTypes.DEC_PRIVATE | 7,
    DECARM_AutoRepeatMode = DispatchTypes.DEC_PRIVATE | 8,
    ATT610_StartCursorBlink = DispatchTypes.DEC_PRIVATE | 12,
    DECTCEM_TextCursorEnableMode = DispatchTypes.DEC_PRIVATE | 25,
    XTERM_EnableDECCOLMSupport = DispatchTypes.DEC_PRIVATE | 40,
    DECPCCM_PageCursorCouplingMode = DispatchTypes.DEC_PRIVATE | 64,
    DECNKM_NumericKeypadMode = DispatchTypes.DEC_PRIVATE | 66,
    DECBKM_BackarrowKeyMode = DispatchTypes.DEC_PRIVATE | 67,
    DECLRMM_LeftRightMarginMode = DispatchTypes.DEC_PRIVATE | 69,
    DECSDM_SixelDisplayMode = DispatchTypes.DEC_PRIVATE | 80,
    DECECM_EraseColorMode = DispatchTypes.DEC_PRIVATE | 117,
    VT200_MOUSE_MODE = DispatchTypes.DEC_PRIVATE | 1000,
    BUTTON_EVENT_MOUSE_MODE = DispatchTypes.DEC_PRIVATE | 1002,
    ANY_EVENT_MOUSE_MODE = DispatchTypes.DEC_PRIVATE | 1003,
    FOCUS_EVENT_MODE = DispatchTypes.DEC_PRIVATE | 1004,
    UTF8_EXTENDED_MODE = DispatchTypes.DEC_PRIVATE | 1005,
    SGR_EXTENDED_MODE = DispatchTypes.DEC_PRIVATE | 1006,
    ALTERNATE_SCROLL = DispatchTypes.DEC_PRIVATE | 1007,
    ASB_AlternateScreenBuffer = DispatchTypes.DEC_PRIVATE | 1049,
    XTERM_BracketedPasteMode = DispatchTypes.DEC_PRIVATE | 2004,
    SO_SynchronizedOutput = DispatchTypes.DEC_PRIVATE | 2026,
    GCM_GraphemeClusterMode = DispatchTypes.DEC_PRIVATE | 2027,
    W32IM_Win32InputMode = DispatchTypes.DEC_PRIVATE | 9001,
}

internal enum ModeResponses
{
    DECRPM_Unsupported = 0,
    DECRPM_Enabled = 1,
    DECRPM_Disabled = 2,
    DECRPM_PermanentlyEnabled = 3,
    DECRPM_PermanentlyDisabled = 4,
}

internal enum TabClearType
{
    ClearCurrentColumn = 0,
    ClearAllColumns = 3,
}

internal enum TabSetType
{
    SetEvery8Columns = 5,
}

internal enum WindowManipulationType
{
    Invalid = 0,
    DeIconifyWindow = 1,
    IconifyWindow = 2,
    RefreshWindow = 7,
    ResizeWindowInCharacters = 8,
    ReportTextSizeInPixels = 14,
    ReportCharacterCellSize = 16,
    ReportTextSizeInCharacters = 18,
}

internal enum CursorStyle
{
    // Restores the cursor to the user's default.
    UserDefault = 0,
    BlinkingBlock = 1,
    SteadyBlock = 2,
    BlinkingUnderline = 3,
    SteadyUnderline = 4,
    BlinkingBar = 5,
    SteadyBar = 6,
}

internal enum ReportingPermission
{
    Unsolicited = 0,
    Solicited = 1,
}

internal enum LineFeedType
{
    WithReturn,
    WithoutReturn,
    DependsOnMode,
}

internal enum SixelBackground
{
    Default = 0,
    Transparent = 1,
    Opaque = 2,
}

internal enum DrcsEraseControl
{
    AllChars = 0,
    ReloadedChars = 1,
    AllRenditions = 2,
}

internal enum DrcsCellMatrix
{
    Default = 0,
    Invalid = 1,
    Size5x10 = 2,
    Size6x10 = 3,
    Size7x10 = 4,
}

internal enum DrcsFontSet
{
    Default = 0,
    Size80x24 = 1,
    Size132x24 = 2,
    Size80x36 = 11,
    Size132x36 = 12,
    Size80x48 = 21,
    Size132x48 = 22,
}

internal enum DrcsFontUsage
{
    Default = 0,
    Text = 1,
    FullCell = 2,
}

internal enum CharsetSize
{
    Size94 = 0,
    Size96 = 1,
}

internal enum MacroDeleteControl
{
    DeleteId = 0,
    DeleteAll = 1,
}

internal enum MacroEncoding
{
    Text = 0,
    HexPair = 1,
}

internal enum ReportFormat
{
    TerminalStateReport = 1,
    ColorTableReport = 2,
}

internal enum PresentationReportFormat
{
    CursorInformationReport = 1,
    TabulationStopReport = 2,
}
