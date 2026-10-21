// Ported from Windows Terminal src/terminal/parser/OutputStateMachineEngine.hpp and OutputStateMachineEngine.cpp (MIT).

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Parser;

/// <summary>
/// Turns what the parser recognizes in an output stream into <see cref="TermDispatch"/> calls: control characters,
/// ESC, CSI, DCS, OSC and VT52 sequences, by their identifiers.
/// </summary>
internal sealed class OutputStateMachineEngine : IStateMachineEngine
{
    // 2MB, as iTerm2 allows.
    public const int MAX_URL_LENGTH = 2 * 1048576;

    // Like an invalid color, but asks for the color instead of setting it.
    private const uint COLOR_INQUIRY_COLOR = 0xFEFFFFFF;

    private const string HYPERLINK_ID_PARAMETER = "id=";

    private static class EscActionCodes
    {
        public const ulong DECBI_BackIndex = '6';
        public const ulong DECSC_CursorSave = '7';
        public const ulong DECRC_CursorRestore = '8';
        public const ulong DECFI_ForwardIndex = '9';
        public const ulong DECKPAM_KeypadApplicationMode = '=';
        public const ulong DECKPNM_KeypadNumericMode = '>';
        public const ulong IND_Index = 'D';
        public const ulong NEL_NextLine = 'E';
        public const ulong HTS_HorizontalTabSet = 'H';
        public const ulong RI_ReverseLineFeed = 'M';
        public const ulong SS2_SingleShift = 'N';
        public const ulong SS3_SingleShift = 'O';
        public const ulong DECID_IdentifyDevice = 'Z';
        public const ulong ST_StringTerminator = '\\';
        public const ulong RIS_ResetToInitialState = 'c';
        public const ulong LS2_LockingShift = 'n';
        public const ulong LS3_LockingShift = 'o';
        public const ulong LS1R_LockingShift = '~';
        public const ulong LS2R_LockingShift = '}';
        public const ulong LS3R_LockingShift = '|';
        public const ulong DECAC1_AcceptC1Controls = ' ' | ('7' << 8);
        public const ulong S7C1T_Send7bitC1Controls = ' ' | ('F' << 8);
        public const ulong S8C1T_Send8bitC1Controls = ' ' | ('G' << 8);
        public const ulong ACS_AnsiLevel1 = ' ' | ('L' << 8);
        public const ulong ACS_AnsiLevel2 = ' ' | ('M' << 8);
        public const ulong ACS_AnsiLevel3 = ' ' | ('N' << 8);
        public const ulong DECDHL_DoubleHeightLineTop = '#' | ('3' << 8);
        public const ulong DECDHL_DoubleHeightLineBottom = '#' | ('4' << 8);
        public const ulong DECSWL_SingleWidthLine = '#' | ('5' << 8);
        public const ulong DECDWL_DoubleWidthLine = '#' | ('6' << 8);
        public const ulong DECALN_ScreenAlignmentPattern = '#' | ('8' << 8);
    }

    private static class CsiActionCodes
    {
        public const ulong ICH_InsertCharacter = '@';
        public const ulong CUU_CursorUp = 'A';
        public const ulong CUD_CursorDown = 'B';
        public const ulong CUF_CursorForward = 'C';
        public const ulong CUB_CursorBackward = 'D';
        public const ulong CNL_CursorNextLine = 'E';
        public const ulong CPL_CursorPrevLine = 'F';
        public const ulong CHA_CursorHorizontalAbsolute = 'G';
        public const ulong CUP_CursorPosition = 'H';
        public const ulong CHT_CursorForwardTab = 'I';
        public const ulong ED_EraseDisplay = 'J';
        public const ulong DECSED_SelectiveEraseDisplay = '?' | ('J' << 8);
        public const ulong EL_EraseLine = 'K';
        public const ulong DECSEL_SelectiveEraseLine = '?' | ('K' << 8);
        public const ulong IL_InsertLine = 'L';
        public const ulong DL_DeleteLine = 'M';
        public const ulong DCH_DeleteCharacter = 'P';
        public const ulong SU_ScrollUp = 'S';
        public const ulong SD_ScrollDown = 'T';
        public const ulong NP_NextPage = 'U';
        public const ulong PP_PrecedingPage = 'V';
        public const ulong DECST8C_SetTabEvery8Columns = '?' | ('W' << 8);
        public const ulong ECH_EraseCharacters = 'X';
        public const ulong CBT_CursorBackTab = 'Z';
        public const ulong HPA_HorizontalPositionAbsolute = '`';
        public const ulong HPR_HorizontalPositionRelative = 'a';
        public const ulong REP_RepeatCharacter = 'b';
        public const ulong DA_DeviceAttributes = 'c';
        public const ulong DA2_SecondaryDeviceAttributes = '>' | ('c' << 8);
        public const ulong DA3_TertiaryDeviceAttributes = '=' | ('c' << 8);
        public const ulong VPA_VerticalLinePositionAbsolute = 'd';
        public const ulong VPR_VerticalPositionRelative = 'e';
        public const ulong HVP_HorizontalVerticalPosition = 'f';
        public const ulong TBC_TabClear = 'g';
        public const ulong SM_SetMode = 'h';
        public const ulong DECSET_PrivateModeSet = '?' | ('h' << 8);
        public const ulong RM_ResetMode = 'l';
        public const ulong DECRST_PrivateModeReset = '?' | ('l' << 8);
        public const ulong SGR_SetGraphicsRendition = 'm';
        public const ulong DSR_DeviceStatusReport = 'n';
        public const ulong DSR_PrivateDeviceStatusReport = '?' | ('n' << 8);
        public const ulong DECSTBM_SetTopBottomMargins = 'r';
        public const ulong DECSLRM_SetLeftRightMargins = 's';

        // Overlaps with DECSLPP, which is not implemented.
        public const ulong DTTERM_WindowManipulation = 't';
        public const ulong ANSISYSRC_CursorRestore = 'u';
        public const ulong DECREQTPARM_RequestTerminalParameters = 'x';
        public const ulong PPA_PagePositionAbsolute = ' ' | ('P' << 8);
        public const ulong PPR_PagePositionRelative = ' ' | ('Q' << 8);
        public const ulong PPB_PagePositionBack = ' ' | ('R' << 8);
        public const ulong DECSCUSR_SetCursorStyle = ' ' | ('q' << 8);
        public const ulong DECSTR_SoftReset = '!' | ('p' << 8);
        public const ulong DECSCA_SetCharacterProtectionAttribute = '"' | ('q' << 8);
        public const ulong DECRQDE_RequestDisplayedExtent = '"' | ('v' << 8);
        public const ulong XT_PushSgrAlias = '#' | ('p' << 8);
        public const ulong XT_PopSgrAlias = '#' | ('q' << 8);
        public const ulong XT_PushSgr = '#' | ('{' << 8);
        public const ulong XT_PopSgr = '#' | ('}' << 8);
        public const ulong DECRQM_RequestMode = '$' | ('p' << 8);
        public const ulong DECRQM_PrivateRequestMode = '?' | ('$' << 8) | ('p' << 16);
        public const ulong DECCARA_ChangeAttributesRectangularArea = '$' | ('r' << 8);
        public const ulong DECRARA_ReverseAttributesRectangularArea = '$' | ('t' << 8);
        public const ulong DECRQTSR_RequestTerminalStateReport = '$' | ('u' << 8);
        public const ulong DECCRA_CopyRectangularArea = '$' | ('v' << 8);
        public const ulong DECRQPSR_RequestPresentationStateReport = '$' | ('w' << 8);
        public const ulong DECFRA_FillRectangularArea = '$' | ('x' << 8);
        public const ulong DECERA_EraseRectangularArea = '$' | ('z' << 8);
        public const ulong DECSERA_SelectiveEraseRectangularArea = '$' | ('{' << 8);
        public const ulong DECSCPP_SetColumnsPerPage = '$' | ('|' << 8);
        public const ulong DECRQUPSS_RequestUserPreferenceSupplementalSet = '&' | ('u' << 8);
        public const ulong DECIC_InsertColumn = '\'' | ('}' << 8);
        public const ulong DECDC_DeleteColumn = '\'' | ('~' << 8);
        public const ulong DECSACE_SelectAttributeChangeExtent = '*' | ('x' << 8);
        public const ulong DECRQCRA_RequestChecksumRectangularArea = '*' | ('y' << 8);
        public const ulong DECINVM_InvokeMacro = '*' | ('z' << 8);
        public const ulong DECAC_AssignColor = ',' | ('|' << 8);
        public const ulong DECPS_PlaySound = ',' | ('~' << 8);
        public const ulong KKP_KittyKeyboardSet = '=' | ('u' << 8);
        public const ulong KKP_KittyKeyboardQuery = '?' | ('u' << 8);
        public const ulong KKP_KittyKeyboardPush = '>' | ('u' << 8);
        public const ulong KKP_KittyKeyboardPop = '<' | ('u' << 8);
    }

    private static class DcsActionCodes
    {
        public const ulong SIXEL_DefineImage = 'q';
        public const ulong DECDLD_DownloadDRCS = '{';
        public const ulong DECAUPSS_AssignUserPreferenceSupplementalSet = '!' | ('u' << 8);
        public const ulong DECDMAC_DefineMacro = '!' | ('z' << 8);
        public const ulong DECRSTS_RestoreTerminalState = '$' | ('p' << 8);
        public const ulong DECRQSS_RequestSetting = '$' | ('q' << 8);
        public const ulong DECRSPS_RestorePresentationState = '$' | ('t' << 8);
    }

    private static class Vt52ActionCodes
    {
        public const ulong CursorUp = 'A';
        public const ulong CursorDown = 'B';
        public const ulong CursorRight = 'C';
        public const ulong CursorLeft = 'D';
        public const ulong EnterGraphicsMode = 'F';
        public const ulong ExitGraphicsMode = 'G';
        public const ulong CursorToHome = 'H';
        public const ulong ReverseLineFeed = 'I';
        public const ulong EraseToEndOfScreen = 'J';
        public const ulong EraseToEndOfLine = 'K';
        public const ulong DirectCursorAddress = 'Y';
        public const ulong Identify = 'Z';
        public const ulong EnterAlternateKeypadMode = '=';
        public const ulong ExitAlternateKeypadMode = '>';
        public const ulong ExitVt52Mode = '<';
    }

    private static class OscActionCodes
    {
        public const int SetIconAndWindowTitle = 0;
        public const int SetWindowIcon = 1;
        public const int SetWindowTitle = 2;

        // Not implemented.
        public const int SetWindowProperty = 3;
        public const int SetColor = 4;
        public const int CurrentWorkingDirectory = 7;
        public const int Hyperlink = 8;
        public const int ConEmuAction = 9;
        public const int SetForegroundColor = 10;
        public const int SetBackgroundColor = 11;
        public const int SetCursorColor = 12;
        public const int SetHighlightColor = 17;
        public const int DECSWT_SetWindowTitle = 21;
        public const int SetClipboard = 52;
        public const int ResetColor = 104;
        public const int ResetForegroundColor = 110;
        public const int ResetBackgroundColor = 111;
        public const int ResetCursorColor = 112;
        public const int ResetHighlightColor = 117;
        public const int FinalTermAction = 133;
        public const int VsCodeAction = 633;
        public const int UrxvtAction = 777;
        public const int ITerm2Action = 1337;
        public const int WTAction = 9001;
    }

    private readonly TermDispatch _dispatch;
    private char _lastPrintedChar = AsciiChars.NUL;

    public OutputStateMachineEngine(TermDispatch dispatch)
    {
        _dispatch = dispatch;
    }

    public TermDispatch Dispatch => _dispatch;

    public void UnknownSequence() => _dispatch.UnknownSequence();

    public bool EncounteredWin32InputModeSequence() => false;

    public bool ActionExecute(char character)
    {
        switch (character)
        {
            case AsciiChars.ENQ:
                _dispatch.EnquireAnswerback();
                break;
            case AsciiChars.BEL:
                _dispatch.WarningBell();
                break;
            case AsciiChars.BS:
                _dispatch.CursorBackward(1);
                break;
            case AsciiChars.TAB:
                _dispatch.ForwardTab(1);
                break;
            case AsciiChars.CR:
                _dispatch.CarriageReturn();
                break;
            case AsciiChars.LF:
            case AsciiChars.FF:
            case AsciiChars.VT:
                _dispatch.LineFeed(LineFeedType.DependsOnMode);
                break;
            case AsciiChars.SI:
                _dispatch.LockingShift(0);
                break;
            case AsciiChars.SO:
                _dispatch.LockingShift(1);
                break;
            case AsciiChars.SUB:
                // SUB shows as the symbol for substitute.
                _dispatch.Print('␦');
                break;
            case AsciiChars.DEL:
                _dispatch.Print(character);
                break;
        }

        ClearLastChar();
        return true;
    }

    public bool ActionExecuteFromEscape(char character) => ActionExecute(character);

    public bool ActionPrint(char character)
    {
        if (character >= AsciiChars.SPC)
        {
            _lastPrintedChar = character;
        }

        _dispatch.Print(character);
        return true;
    }

    public bool ActionPrintString(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return true;
        }

        char last = text[^1];
        if (last >= AsciiChars.SPC)
        {
            _lastPrintedChar = last;
        }

        _dispatch.PrintString(text);
        return true;
    }

    public bool ActionPassThroughString(ReadOnlySpan<char> text) => true;

    public bool ActionEscDispatch(VTID id)
    {
        switch (id.Value)
        {
            case EscActionCodes.ST_StringTerminator:
                break;
            case EscActionCodes.DECBI_BackIndex:
                _dispatch.BackIndex();
                break;
            case EscActionCodes.DECSC_CursorSave:
                _dispatch.CursorSaveState();
                break;
            case EscActionCodes.DECRC_CursorRestore:
                _dispatch.CursorRestoreState();
                break;
            case EscActionCodes.DECFI_ForwardIndex:
                _dispatch.ForwardIndex();
                break;
            case EscActionCodes.DECKPAM_KeypadApplicationMode:
                _dispatch.SetKeypadMode(true);
                break;
            case EscActionCodes.DECKPNM_KeypadNumericMode:
                _dispatch.SetKeypadMode(false);
                break;
            case EscActionCodes.NEL_NextLine:
                _dispatch.LineFeed(LineFeedType.WithReturn);
                break;
            case EscActionCodes.IND_Index:
                _dispatch.LineFeed(LineFeedType.WithoutReturn);
                break;
            case EscActionCodes.RI_ReverseLineFeed:
                _dispatch.ReverseLineFeed();
                break;
            case EscActionCodes.HTS_HorizontalTabSet:
                _dispatch.HorizontalTabSet();
                break;
            case EscActionCodes.DECID_IdentifyDevice:
                _dispatch.DeviceAttributes();
                break;
            case EscActionCodes.RIS_ResetToInitialState:
                _dispatch.HardReset(true);
                break;
            case EscActionCodes.SS2_SingleShift:
                _dispatch.SingleShift(2);
                break;
            case EscActionCodes.SS3_SingleShift:
                _dispatch.SingleShift(3);
                break;
            case EscActionCodes.LS2_LockingShift:
                _dispatch.LockingShift(2);
                break;
            case EscActionCodes.LS3_LockingShift:
                _dispatch.LockingShift(3);
                break;
            case EscActionCodes.LS1R_LockingShift:
                _dispatch.LockingShiftRight(1);
                break;
            case EscActionCodes.LS2R_LockingShift:
                _dispatch.LockingShiftRight(2);
                break;
            case EscActionCodes.LS3R_LockingShift:
                _dispatch.LockingShiftRight(3);
                break;
            case EscActionCodes.DECAC1_AcceptC1Controls:
                _dispatch.AcceptC1Controls(true);
                break;
            case EscActionCodes.S7C1T_Send7bitC1Controls:
                _dispatch.SendC1Controls(false);
                break;
            case EscActionCodes.S8C1T_Send8bitC1Controls:
                _dispatch.SendC1Controls(true);
                break;
            case EscActionCodes.ACS_AnsiLevel1:
                _dispatch.AnnounceCodeStructure(1);
                break;
            case EscActionCodes.ACS_AnsiLevel2:
                _dispatch.AnnounceCodeStructure(2);
                break;
            case EscActionCodes.ACS_AnsiLevel3:
                _dispatch.AnnounceCodeStructure(3);
                break;
            case EscActionCodes.DECDHL_DoubleHeightLineTop:
                _dispatch.SetLineRendition(LineRendition.DoubleHeightTop);
                break;
            case EscActionCodes.DECDHL_DoubleHeightLineBottom:
                _dispatch.SetLineRendition(LineRendition.DoubleHeightBottom);
                break;
            case EscActionCodes.DECSWL_SingleWidthLine:
                _dispatch.SetLineRendition(LineRendition.SingleWidth);
                break;
            case EscActionCodes.DECDWL_DoubleWidthLine:
                _dispatch.SetLineRendition(LineRendition.DoubleWidth);
                break;
            case EscActionCodes.DECALN_ScreenAlignmentPattern:
                _dispatch.ScreenAlignmentPattern();
                break;
            default:
                DispatchCharsetDesignation(id);
                break;
        }

        ClearLastChar();
        return true;
    }

    private void DispatchCharsetDesignation(VTID id)
    {
        char commandChar = id[0];
        var commandParameter = id.SubSequence(1);
        switch (commandChar)
        {
            case '%':
                _dispatch.DesignateCodingSystem(commandParameter);
                break;
            case '(':
                _dispatch.Designate94Charset(0, commandParameter);
                break;
            case ')':
                _dispatch.Designate94Charset(1, commandParameter);
                break;
            case '*':
                _dispatch.Designate94Charset(2, commandParameter);
                break;
            case '+':
                _dispatch.Designate94Charset(3, commandParameter);
                break;
            case '-':
                _dispatch.Designate96Charset(1, commandParameter);
                break;
            case '.':
                _dispatch.Designate96Charset(2, commandParameter);
                break;
            case '/':
                _dispatch.Designate96Charset(3, commandParameter);
                break;
        }
    }

    public bool ActionVt52EscDispatch(VTID id, VTParameters parameters)
    {
        switch (id.Value)
        {
            case Vt52ActionCodes.CursorUp:
                _dispatch.CursorUp(1);
                break;
            case Vt52ActionCodes.CursorDown:
                _dispatch.CursorDown(1);
                break;
            case Vt52ActionCodes.CursorRight:
                _dispatch.CursorForward(1);
                break;
            case Vt52ActionCodes.CursorLeft:
                _dispatch.CursorBackward(1);
                break;
            case Vt52ActionCodes.EnterGraphicsMode:
                _dispatch.Designate94Charset(0, DispatchTypes.CHARSET_DEC_SPECIAL_GRAPHICS);
                break;
            case Vt52ActionCodes.ExitGraphicsMode:
                _dispatch.Designate94Charset(0, DispatchTypes.CHARSET_ASCII);
                break;
            case Vt52ActionCodes.CursorToHome:
                _dispatch.CursorPosition(1, 1);
                break;
            case Vt52ActionCodes.ReverseLineFeed:
                _dispatch.ReverseLineFeed();
                break;
            case Vt52ActionCodes.EraseToEndOfScreen:
                _dispatch.EraseInDisplay(EraseType.ToEnd);
                break;
            case Vt52ActionCodes.EraseToEndOfLine:
                _dispatch.EraseInLine(EraseType.ToEnd);
                break;
            case Vt52ActionCodes.DirectCursorAddress:
                _dispatch.CursorPosition(parameters.At(0).Value - ' ' + 1, parameters.At(1).Value - ' ' + 1);
                break;
            case Vt52ActionCodes.Identify:
                _dispatch.Vt52DeviceAttributes();
                break;
            case Vt52ActionCodes.EnterAlternateKeypadMode:
                _dispatch.SetKeypadMode(true);
                break;
            case Vt52ActionCodes.ExitAlternateKeypadMode:
                _dispatch.SetKeypadMode(false);
                break;
            case Vt52ActionCodes.ExitVt52Mode:
                _dispatch.SetMode(ModeParams.DECANM_AnsiMode);
                break;
        }

        ClearLastChar();
        return true;
    }

    public bool ActionCsiDispatch(VTID id, VTParameters parameters)
    {
        // Only a few sequences take sub-parameters; any other sequence that has them is ignored.
        if (parameters.HasSubParams && !CanSeqAcceptSubParam(id, parameters))
        {
            return true;
        }

        switch (id.Value)
        {
            case CsiActionCodes.CUU_CursorUp:
                _dispatch.CursorUp(parameters.At(0));
                break;
            case CsiActionCodes.CUD_CursorDown:
                _dispatch.CursorDown(parameters.At(0));
                break;
            case CsiActionCodes.CUF_CursorForward:
                _dispatch.CursorForward(parameters.At(0));
                break;
            case CsiActionCodes.CUB_CursorBackward:
                _dispatch.CursorBackward(parameters.At(0));
                break;
            case CsiActionCodes.CNL_CursorNextLine:
                _dispatch.CursorNextLine(parameters.At(0));
                break;
            case CsiActionCodes.CPL_CursorPrevLine:
                _dispatch.CursorPrevLine(parameters.At(0));
                break;
            case CsiActionCodes.CHA_CursorHorizontalAbsolute:
            case CsiActionCodes.HPA_HorizontalPositionAbsolute:
                _dispatch.CursorHorizontalPositionAbsolute(parameters.At(0));
                break;
            case CsiActionCodes.VPA_VerticalLinePositionAbsolute:
                _dispatch.VerticalLinePositionAbsolute(parameters.At(0));
                break;
            case CsiActionCodes.HPR_HorizontalPositionRelative:
                _dispatch.HorizontalPositionRelative(parameters.At(0));
                break;
            case CsiActionCodes.VPR_VerticalPositionRelative:
                _dispatch.VerticalPositionRelative(parameters.At(0));
                break;
            case CsiActionCodes.CUP_CursorPosition:
            case CsiActionCodes.HVP_HorizontalVerticalPosition:
                _dispatch.CursorPosition(parameters.At(0), parameters.At(1));
                break;
            case CsiActionCodes.DECSTBM_SetTopBottomMargins:
                _dispatch.SetTopBottomScrollingMargins(parameters.At(0).ValueOr(0), parameters.At(1).ValueOr(0));
                break;
            case CsiActionCodes.DECSLRM_SetLeftRightMargins:
                _dispatch.SetLeftRightScrollingMargins(parameters.At(0).ValueOr(0), parameters.At(1).ValueOr(0));
                break;
            case CsiActionCodes.ICH_InsertCharacter:
                _dispatch.InsertCharacter(parameters.At(0));
                break;
            case CsiActionCodes.DCH_DeleteCharacter:
                _dispatch.DeleteCharacter(parameters.At(0));
                break;
            case CsiActionCodes.ED_EraseDisplay:
                foreach (var eraseType in parameters.Values)
                {
                    _dispatch.EraseInDisplay((EraseType)eraseType.Selective);
                }

                break;
            case CsiActionCodes.DECSED_SelectiveEraseDisplay:
                foreach (var eraseType in parameters.Values)
                {
                    _dispatch.SelectiveEraseInDisplay((EraseType)eraseType.Selective);
                }

                break;
            case CsiActionCodes.EL_EraseLine:
                foreach (var eraseType in parameters.Values)
                {
                    _dispatch.EraseInLine((EraseType)eraseType.Selective);
                }

                break;
            case CsiActionCodes.DECSEL_SelectiveEraseLine:
                foreach (var eraseType in parameters.Values)
                {
                    _dispatch.SelectiveEraseInLine((EraseType)eraseType.Selective);
                }

                break;
            case CsiActionCodes.SM_SetMode:
                foreach (var mode in parameters.Values)
                {
                    _dispatch.SetMode(DispatchTypes.AnsiStandardMode(mode));
                }

                break;
            case CsiActionCodes.DECSET_PrivateModeSet:
                foreach (var mode in parameters.Values)
                {
                    _dispatch.SetMode(DispatchTypes.DecPrivateMode(mode));
                }

                break;
            case CsiActionCodes.RM_ResetMode:
                foreach (var mode in parameters.Values)
                {
                    _dispatch.ResetMode(DispatchTypes.AnsiStandardMode(mode));
                }

                break;
            case CsiActionCodes.DECRST_PrivateModeReset:
                foreach (var mode in parameters.Values)
                {
                    _dispatch.ResetMode(DispatchTypes.DecPrivateMode(mode));
                }

                break;
            case CsiActionCodes.SGR_SetGraphicsRendition:
                _dispatch.SetGraphicsRendition(parameters);
                break;
            case CsiActionCodes.DSR_DeviceStatusReport:
                _dispatch.DeviceStatusReport(DispatchTypes.AnsiStandardStatus(parameters.At(0)), parameters.At(1));
                break;
            case CsiActionCodes.DSR_PrivateDeviceStatusReport:
                _dispatch.DeviceStatusReport(DispatchTypes.DecPrivateStatus(parameters.At(0)), parameters.At(1));
                break;
            case CsiActionCodes.DA_DeviceAttributes:
                if (parameters.At(0).ValueOr(0) == 0)
                {
                    _dispatch.DeviceAttributes();
                }

                break;
            case CsiActionCodes.DA2_SecondaryDeviceAttributes:
                if (parameters.At(0).ValueOr(0) == 0)
                {
                    _dispatch.SecondaryDeviceAttributes();
                }

                break;
            case CsiActionCodes.DA3_TertiaryDeviceAttributes:
                if (parameters.At(0).ValueOr(0) == 0)
                {
                    _dispatch.TertiaryDeviceAttributes();
                }

                break;
            case CsiActionCodes.DECREQTPARM_RequestTerminalParameters:
                _dispatch.RequestTerminalParameters((ReportingPermission)parameters.At(0).Selective);
                break;
            case CsiActionCodes.SU_ScrollUp:
                _dispatch.ScrollUp(parameters.At(0));
                break;
            case CsiActionCodes.SD_ScrollDown:
                _dispatch.ScrollDown(parameters.At(0));
                break;
            case CsiActionCodes.NP_NextPage:
                _dispatch.NextPage(parameters.At(0));
                break;
            case CsiActionCodes.PP_PrecedingPage:
                _dispatch.PrecedingPage(parameters.At(0));
                break;
            case CsiActionCodes.ANSISYSRC_CursorRestore:
                _dispatch.CursorRestoreState();
                break;
            case CsiActionCodes.IL_InsertLine:
                _dispatch.InsertLine(parameters.At(0));
                break;
            case CsiActionCodes.DL_DeleteLine:
                _dispatch.DeleteLine(parameters.At(0));
                break;
            case CsiActionCodes.CHT_CursorForwardTab:
                _dispatch.ForwardTab(parameters.At(0));
                break;
            case CsiActionCodes.CBT_CursorBackTab:
                _dispatch.BackwardsTab(parameters.At(0));
                break;
            case CsiActionCodes.TBC_TabClear:
                foreach (var clearType in parameters.Values)
                {
                    _dispatch.TabClear((TabClearType)clearType.Selective);
                }

                break;
            case CsiActionCodes.DECST8C_SetTabEvery8Columns:
                foreach (var setType in parameters.Values)
                {
                    _dispatch.TabSet(setType);
                }

                break;
            case CsiActionCodes.ECH_EraseCharacters:
                _dispatch.EraseCharacters(parameters.At(0));
                break;
            case CsiActionCodes.DTTERM_WindowManipulation:
                _dispatch.WindowManipulation((WindowManipulationType)parameters.At(0).Selective, parameters.At(1), parameters.At(2));
                break;
            case CsiActionCodes.REP_RepeatCharacter:
                // Repeats only a printed character, never a control or a sequence.
                if (_lastPrintedChar != AsciiChars.NUL)
                {
                    int repeatCount = parameters.At(0);
                    _dispatch.PrintString(new string(_lastPrintedChar, repeatCount));
                }

                break;
            case CsiActionCodes.PPA_PagePositionAbsolute:
                _dispatch.PagePositionAbsolute(parameters.At(0));
                break;
            case CsiActionCodes.PPR_PagePositionRelative:
                _dispatch.PagePositionRelative(parameters.At(0));
                break;
            case CsiActionCodes.PPB_PagePositionBack:
                _dispatch.PagePositionBack(parameters.At(0));
                break;
            case CsiActionCodes.DECSCUSR_SetCursorStyle:
                _dispatch.SetCursorStyle((CursorStyle)parameters.At(0).Selective);
                break;
            case CsiActionCodes.DECSTR_SoftReset:
                _dispatch.SoftReset();
                break;
            case CsiActionCodes.DECSCA_SetCharacterProtectionAttribute:
                _dispatch.SetCharacterProtectionAttribute(parameters);
                break;
            case CsiActionCodes.DECRQDE_RequestDisplayedExtent:
                _dispatch.RequestDisplayedExtent();
                break;
            case CsiActionCodes.XT_PushSgr:
            case CsiActionCodes.XT_PushSgrAlias:
                _dispatch.PushGraphicsRendition(parameters);
                break;
            case CsiActionCodes.XT_PopSgr:
            case CsiActionCodes.XT_PopSgrAlias:
                _dispatch.PopGraphicsRendition();
                break;
            case CsiActionCodes.DECRQM_RequestMode:
                _dispatch.RequestMode(DispatchTypes.AnsiStandardMode(parameters.At(0)));
                break;
            case CsiActionCodes.DECRQM_PrivateRequestMode:
                _dispatch.RequestMode(DispatchTypes.DecPrivateMode(parameters.At(0)));
                break;
            case CsiActionCodes.DECCARA_ChangeAttributesRectangularArea:
                _dispatch.ChangeAttributesRectangularArea(parameters.At(0), parameters.At(1), parameters.At(2).ValueOr(0), parameters.At(3).ValueOr(0), parameters.Subspan(4));
                break;
            case CsiActionCodes.DECRARA_ReverseAttributesRectangularArea:
                _dispatch.ReverseAttributesRectangularArea(parameters.At(0), parameters.At(1), parameters.At(2).ValueOr(0), parameters.At(3).ValueOr(0), parameters.Subspan(4));
                break;
            case CsiActionCodes.DECCRA_CopyRectangularArea:
                _dispatch.CopyRectangularArea(parameters.At(0), parameters.At(1), parameters.At(2).ValueOr(0), parameters.At(3).ValueOr(0), parameters.At(4), parameters.At(5), parameters.At(6), parameters.At(7));
                break;
            case CsiActionCodes.DECRQTSR_RequestTerminalStateReport:
                _dispatch.RequestTerminalStateReport((ReportFormat)parameters.At(0).Selective, parameters.At(1));
                break;
            case CsiActionCodes.DECRQPSR_RequestPresentationStateReport:
                _dispatch.RequestPresentationStateReport((PresentationReportFormat)parameters.At(0).Selective);
                break;
            case CsiActionCodes.DECFRA_FillRectangularArea:
                _dispatch.FillRectangularArea(parameters.At(0), parameters.At(1), parameters.At(2), parameters.At(3).ValueOr(0), parameters.At(4).ValueOr(0));
                break;
            case CsiActionCodes.DECERA_EraseRectangularArea:
                _dispatch.EraseRectangularArea(parameters.At(0), parameters.At(1), parameters.At(2).ValueOr(0), parameters.At(3).ValueOr(0));
                break;
            case CsiActionCodes.DECSERA_SelectiveEraseRectangularArea:
                _dispatch.SelectiveEraseRectangularArea(parameters.At(0), parameters.At(1), parameters.At(2).ValueOr(0), parameters.At(3).ValueOr(0));
                break;
            case CsiActionCodes.DECRQUPSS_RequestUserPreferenceSupplementalSet:
                _dispatch.RequestUserPreferenceCharset();
                break;
            case CsiActionCodes.DECIC_InsertColumn:
                _dispatch.InsertColumn(parameters.At(0));
                break;
            case CsiActionCodes.DECDC_DeleteColumn:
                _dispatch.DeleteColumn(parameters.At(0));
                break;
            case CsiActionCodes.DECSACE_SelectAttributeChangeExtent:
                _dispatch.SelectAttributeChangeExtent((ChangeExtent)parameters.At(0).Selective);
                break;
            case CsiActionCodes.DECRQCRA_RequestChecksumRectangularArea:
                _dispatch.RequestChecksumRectangularArea(parameters.At(0).ValueOr(0), parameters.At(1).ValueOr(0), parameters.At(2), parameters.At(3), parameters.At(4).ValueOr(0), parameters.At(5).ValueOr(0));
                break;
            case CsiActionCodes.DECINVM_InvokeMacro:
                _dispatch.InvokeMacro(parameters.At(0).ValueOr(0));
                break;
            case CsiActionCodes.DECAC_AssignColor:
                _dispatch.AssignColor((ColorItem)parameters.At(0).Selective, parameters.At(1).ValueOr(0), parameters.At(2).ValueOr(0));
                break;
            case CsiActionCodes.DECPS_PlaySound:
                _dispatch.PlaySounds(parameters);
                break;
            case CsiActionCodes.KKP_KittyKeyboardSet:
                _dispatch.SetKittyKeyboardProtocol(parameters.At(0), parameters.At(1));
                break;
            case CsiActionCodes.KKP_KittyKeyboardQuery:
                _dispatch.QueryKittyKeyboardProtocol();
                break;
            case CsiActionCodes.KKP_KittyKeyboardPush:
                _dispatch.PushKittyKeyboardProtocol(parameters.At(0));
                break;
            case CsiActionCodes.KKP_KittyKeyboardPop:
                _dispatch.PopKittyKeyboardProtocol(parameters.At(0));
                break;
            default:
                _dispatch.UnknownSequence();
                break;
        }

        ClearLastChar();
        return true;
    }

    public StringHandler? ActionDcsDispatch(VTID id, VTParameters parameters)
    {
        StringHandler? handler = null;
        switch (id.Value)
        {
            case DcsActionCodes.SIXEL_DefineImage:
                handler = _dispatch.DefineSixelImage(parameters.At(0), (SixelBackground)parameters.At(1).Selective, parameters.At(2));
                break;
            case DcsActionCodes.DECDLD_DownloadDRCS:
                handler = _dispatch.DownloadDRCS(
                    parameters.At(0),
                    parameters.At(1),
                    (DrcsEraseControl)parameters.At(2).Selective,
                    (DrcsCellMatrix)parameters.At(3).Selective,
                    (DrcsFontSet)parameters.At(4).Selective,
                    (DrcsFontUsage)parameters.At(5).Selective,
                    parameters.At(6),
                    (CharsetSize)parameters.At(7).Selective);
                break;
            case DcsActionCodes.DECAUPSS_AssignUserPreferenceSupplementalSet:
                handler = _dispatch.AssignUserPreferenceCharset((CharsetSize)parameters.At(0).Selective);
                break;
            case DcsActionCodes.DECDMAC_DefineMacro:
                handler = _dispatch.DefineMacro(parameters.At(0).ValueOr(0), (MacroDeleteControl)parameters.At(1).Selective, (MacroEncoding)parameters.At(2).Selective);
                break;
            case DcsActionCodes.DECRSTS_RestoreTerminalState:
                handler = _dispatch.RestoreTerminalState((ReportFormat)parameters.At(0).Selective);
                break;
            case DcsActionCodes.DECRQSS_RequestSetting:
                handler = _dispatch.RequestSetting();
                break;
            case DcsActionCodes.DECRSPS_RestorePresentationState:
                handler = _dispatch.RestorePresentationState((PresentationReportFormat)parameters.At(0).Selective);
                break;
            default:
                _dispatch.UnknownSequence();
                break;
        }

        ClearLastChar();
        return handler;
    }

    public bool ActionOscDispatch(int parameter, ReadOnlySpan<char> text)
    {
        switch (parameter)
        {
            case OscActionCodes.SetIconAndWindowTitle:
            case OscActionCodes.SetWindowIcon:
            case OscActionCodes.SetWindowTitle:
            case OscActionCodes.DECSWT_SetWindowTitle:
                _dispatch.SetWindowTitle(text);
                break;
            case OscActionCodes.SetColor:
                if (TryGetOscSetColorTable(text, out var tableIndexes, out var colors))
                {
                    for (int index = 0; index < tableIndexes.Count; index++)
                    {
                        if (colors[index] == COLOR_INQUIRY_COLOR)
                        {
                            _dispatch.RequestColorTableEntry(tableIndexes[index]);
                        }
                        else
                        {
                            _dispatch.SetColorTableEntry(tableIndexes[index], colors[index]);
                        }
                    }
                }

                break;
            case OscActionCodes.SetForegroundColor:
            case OscActionCodes.SetBackgroundColor:
            case OscActionCodes.SetCursorColor:
            case OscActionCodes.SetHighlightColor:
                if (TryGetOscSetColor(text, out var resourceColors))
                {
                    // Each further color sets the next resource: OSC 10;fg;bg sets both.
                    int resource = parameter;
                    foreach (uint color in resourceColors)
                    {
                        if (color == COLOR_INQUIRY_COLOR)
                        {
                            _dispatch.RequestXtermColorResource(resource);
                        }
                        else if (color != TextAttribute.INVALID_COLOR)
                        {
                            _dispatch.SetXtermColorResource(resource, color);
                        }

                        resource++;
                    }
                }

                break;
            case OscActionCodes.SetClipboard:
                if (TryGetOscSetClipboard(text, out string content, out bool queryClipboard) && !queryClipboard)
                {
                    _dispatch.SetClipboard(content);
                }

                break;
            case OscActionCodes.ResetColor:
                if (text.IsEmpty)
                {
                    _dispatch.ResetColorTable();
                }
                else
                {
                    foreach (string part in VTUtils.SplitString(text, ';'))
                    {
                        if (!VTUtils.TryStringToUint(part, out uint index))
                        {
                            break;
                        }

                        _dispatch.ResetColorTableEntry((int)index);
                    }
                }

                break;
            case OscActionCodes.ResetForegroundColor:
            case OscActionCodes.ResetBackgroundColor:
            case OscActionCodes.ResetCursorColor:
            case OscActionCodes.ResetHighlightColor:
                if (text.IsEmpty)
                {
                    _dispatch.ResetXtermColorResource(parameter - 100);
                }

                break;
            case OscActionCodes.CurrentWorkingDirectory:
                _dispatch.SetCurrentWorkingDirectory(text);
                break;
            case OscActionCodes.Hyperlink:
                if (TryParseHyperlink(text, out string parameters, out string uri))
                {
                    if (uri.Length == 0)
                    {
                        _dispatch.EndHyperlink();
                    }
                    else
                    {
                        _dispatch.AddHyperlink(uri, parameters);
                    }
                }

                break;
            case OscActionCodes.ConEmuAction:
                _dispatch.DoConEmuAction(text);
                break;
            case OscActionCodes.ITerm2Action:
                _dispatch.DoITerm2Action(text);
                break;
            case OscActionCodes.FinalTermAction:
                _dispatch.DoFinalTermAction(text);
                break;
            case OscActionCodes.VsCodeAction:
                _dispatch.DoVsCodeAction(text);
                break;
            case OscActionCodes.WTAction:
                _dispatch.DoWTAction(text);
                break;
            case OscActionCodes.UrxvtAction:
                _dispatch.DoUrxvtAction(text);
                break;
            default:
                _dispatch.UnknownSequence();
                break;
        }

        ClearLastChar();
        return true;
    }

    public bool ActionSs3Dispatch(char character, VTParameters parameters)
    {
        _dispatch.UnknownSequence();
        ClearLastChar();
        return true;
    }

    /// <summary>Parses OSC 4's <c>index;color</c> pairs, where a color of "?" asks for the current one.</summary>
    private static bool TryGetOscSetColorTable(ReadOnlySpan<char> text, out List<int> tableIndexes, out List<uint> colors)
    {
        tableIndexes = new List<int>();
        colors = new List<uint>();
        var parts = VTUtils.SplitString(text, ';');
        if (parts.Count < 2)
        {
            return false;
        }

        for (int indexPart = 0, colorPart = 1; colorPart < parts.Count; indexPart += 2, colorPart += 2)
        {
            if (!VTUtils.TryStringToUint(parts[indexPart], out uint tableIndex))
            {
                continue;
            }

            string color = parts[colorPart];
            if (color == "?")
            {
                tableIndexes.Add((int)tableIndex);
                colors.Add(COLOR_INQUIRY_COLOR);
            }
            else if (VTUtils.ColorFromXTermColor(color) is uint parsed)
            {
                tableIndexes.Add((int)tableIndex);
                colors.Add(parsed);
            }
        }

        return tableIndexes.Count > 0 && colors.Count > 0;
    }

    /// <summary>Parses OSC 52's <c>selection;base64</c>; a payload of "?" is a query, which is refused.</summary>
    private static bool TryGetOscSetClipboard(ReadOnlySpan<char> text, out string content, out bool queryClipboard)
    {
        content = string.Empty;
        queryClipboard = false;
        int position = text.IndexOf(';');
        if (position < 0)
        {
            return false;
        }

        var payload = text[(position + 1)..];
        if (payload is "?")
        {
            queryClipboard = true;
            return true;
        }

        return Base64.TryDecode(payload, out content);
    }

    /// <summary>Parses OSC 8's <c>params;uri</c>; an empty URI ends the link and <c>id=</c> in params names it.</summary>
    private static bool TryParseHyperlink(ReadOnlySpan<char> text, out string parameters, out string uri)
    {
        parameters = string.Empty;
        uri = string.Empty;
        if (text is ";")
        {
            return true;
        }

        int midPosition = text.IndexOf(';');
        if (midPosition < 0)
        {
            return false;
        }

        var uriText = text[(midPosition + 1)..];
        uri = new string(uriText[..Math.Min(uriText.Length, MAX_URL_LENGTH)]);
        foreach (string part in VTUtils.SplitString(text[..midPosition], ':'))
        {
            int idPosition = part.IndexOf(HYPERLINK_ID_PARAMETER, StringComparison.Ordinal);
            if (idPosition >= 0)
            {
                parameters = part[(idPosition + HYPERLINK_ID_PARAMETER.Length)..];
            }
        }

        return true;
    }

    /// <summary>Parses OSC 10 and on: colors separated by ';', where "?" asks and an unparsable one is skipped.</summary>
    private static bool TryGetOscSetColor(ReadOnlySpan<char> text, out List<uint> colors)
    {
        colors = new List<uint>();
        var parts = VTUtils.SplitString(text, ';');
        if (parts.Count < 1)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (part == "?")
            {
                colors.Add(COLOR_INQUIRY_COLOR);
            }
            else if (VTUtils.ColorFromXTermColor(part) is uint parsed)
            {
                colors.Add(parsed);
            }
            else
            {
                colors.Add(TextAttribute.INVALID_COLOR);
            }
        }

        return colors.Count > 0;
    }

    private static bool CanSeqAcceptSubParam(VTID id, VTParameters parameters)
    {
        switch (id.Value)
        {
            case CsiActionCodes.SGR_SetGraphicsRendition:
                return true;
            case CsiActionCodes.DECCARA_ChangeAttributesRectangularArea:
            case CsiActionCodes.DECRARA_ReverseAttributesRectangularArea:
                return !parameters.HasSubParamsFor(0) && !parameters.HasSubParamsFor(1) && !parameters.HasSubParamsFor(2) && !parameters.HasSubParamsFor(3);
            default:
                return false;
        }
    }

    private void ClearLastChar() => _lastPrintedChar = AsciiChars.NUL;
}
