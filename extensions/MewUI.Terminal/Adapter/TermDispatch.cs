// Ported from Windows Terminal src/terminal/adapter/ITermDispatch.hpp and termDispatch.hpp (MIT): every callback the
// output engine makes, with empty defaults so a dispatcher overrides only what it supports.

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Parser;

namespace Aprillz.MewUI.Terminal.Adapter;

/// <summary>Takes the characters of a DCS string one at a time; returns false to stop taking them.</summary>
internal delegate bool StringHandler(char character);

internal abstract class TermDispatch
{
    [Flags]
    public enum OptionalFeature
    {
        None = 0,
        ChecksumReport = 1 << 0,
        ClipboardWrite = 1 << 1,
        DesktopNotification = 1 << 2,
    }

    public virtual void UnknownSequence()
    {
    }

    public abstract void Print(char printable);

    public abstract void PrintString(ReadOnlySpan<char> text);

    /// <summary>CUU</summary>
    public virtual void CursorUp(int distance)
    {
    }

    /// <summary>CUD</summary>
    public virtual void CursorDown(int distance)
    {
    }

    /// <summary>CUF</summary>
    public virtual void CursorForward(int distance)
    {
    }

    /// <summary>CUB, BS</summary>
    public virtual void CursorBackward(int distance)
    {
    }

    /// <summary>CNL</summary>
    public virtual void CursorNextLine(int distance)
    {
    }

    /// <summary>CPL</summary>
    public virtual void CursorPrevLine(int distance)
    {
    }

    /// <summary>HPA, CHA</summary>
    public virtual void CursorHorizontalPositionAbsolute(int column)
    {
    }

    /// <summary>VPA</summary>
    public virtual void VerticalLinePositionAbsolute(int line)
    {
    }

    /// <summary>HPR</summary>
    public virtual void HorizontalPositionRelative(int distance)
    {
    }

    /// <summary>VPR</summary>
    public virtual void VerticalPositionRelative(int distance)
    {
    }

    /// <summary>CUP, HVP</summary>
    public virtual void CursorPosition(int line, int column)
    {
    }

    /// <summary>DECSC</summary>
    public virtual void CursorSaveState()
    {
    }

    /// <summary>DECRC</summary>
    public virtual void CursorRestoreState()
    {
    }

    /// <summary>ICH</summary>
    public virtual void InsertCharacter(int count)
    {
    }

    /// <summary>DCH</summary>
    public virtual void DeleteCharacter(int count)
    {
    }

    /// <summary>SU</summary>
    public virtual void ScrollUp(int distance)
    {
    }

    /// <summary>SD</summary>
    public virtual void ScrollDown(int distance)
    {
    }

    /// <summary>NP</summary>
    public virtual void NextPage(int pageCount)
    {
    }

    /// <summary>PP</summary>
    public virtual void PrecedingPage(int pageCount)
    {
    }

    /// <summary>PPA</summary>
    public virtual void PagePositionAbsolute(int page)
    {
    }

    /// <summary>PPR</summary>
    public virtual void PagePositionRelative(int pageCount)
    {
    }

    /// <summary>PPB</summary>
    public virtual void PagePositionBack(int pageCount)
    {
    }

    /// <summary>DECRQDE</summary>
    public virtual void RequestDisplayedExtent()
    {
    }

    /// <summary>IL</summary>
    public virtual void InsertLine(int distance)
    {
    }

    /// <summary>DL</summary>
    public virtual void DeleteLine(int distance)
    {
    }

    /// <summary>DECIC</summary>
    public virtual void InsertColumn(int distance)
    {
    }

    /// <summary>DECDC</summary>
    public virtual void DeleteColumn(int distance)
    {
    }

    /// <summary>DECKPAM, DECKPNM</summary>
    public virtual void SetKeypadMode(bool applicationMode)
    {
    }

    /// <summary>DECANM</summary>
    public virtual void SetAnsiMode(bool ansiMode)
    {
    }

    public virtual void SetKittyKeyboardProtocol(VTParameter flags, VTParameter mode)
    {
    }

    public virtual void QueryKittyKeyboardProtocol()
    {
    }

    public virtual void PushKittyKeyboardProtocol(VTParameter flags)
    {
    }

    public virtual void PopKittyKeyboardProtocol(VTParameter count)
    {
    }

    /// <summary>DECSTBM</summary>
    public virtual void SetTopBottomScrollingMargins(int topMargin, int bottomMargin)
    {
    }

    /// <summary>DECSLRM</summary>
    public virtual void SetLeftRightScrollingMargins(int leftMargin, int rightMargin)
    {
    }

    /// <summary>ENQ</summary>
    public virtual void EnquireAnswerback()
    {
    }

    /// <summary>BEL</summary>
    public virtual void WarningBell()
    {
    }

    /// <summary>CR</summary>
    public virtual void CarriageReturn()
    {
    }

    /// <summary>IND, NEL, LF, FF, VT</summary>
    public virtual void LineFeed(LineFeedType lineFeedType)
    {
    }

    /// <summary>RI</summary>
    public virtual void ReverseLineFeed()
    {
    }

    /// <summary>DECBI</summary>
    public virtual void BackIndex()
    {
    }

    /// <summary>DECFI</summary>
    public virtual void ForwardIndex()
    {
    }

    /// <summary>DECSWT, OSC 0/1/2</summary>
    public virtual void SetWindowTitle(ReadOnlySpan<char> title)
    {
    }

    /// <summary>OSC 7</summary>
    public virtual void SetCurrentWorkingDirectory(ReadOnlySpan<char> uri)
    {
    }

    /// <summary>HTS</summary>
    public virtual void HorizontalTabSet()
    {
    }

    /// <summary>CHT, HT</summary>
    public virtual void ForwardTab(int numTabs)
    {
    }

    /// <summary>CBT</summary>
    public virtual void BackwardsTab(int numTabs)
    {
    }

    /// <summary>TBC</summary>
    public virtual void TabClear(TabClearType clearType)
    {
    }

    /// <summary>DECST8C</summary>
    public virtual void TabSet(VTParameter setType)
    {
    }

    /// <summary>OSC 4</summary>
    public virtual void SetColorTableEntry(int tableIndex, uint color)
    {
    }

    public virtual void RequestColorTableEntry(int tableIndex)
    {
    }

    /// <summary>OSC 104</summary>
    public virtual void ResetColorTable()
    {
    }

    public virtual void ResetColorTableEntry(int tableIndex)
    {
    }

    /// <summary>OSC 10, 11, 12, 17</summary>
    public virtual void SetXtermColorResource(int resource, uint color)
    {
    }

    public virtual void RequestXtermColorResource(int resource)
    {
    }

    /// <summary>OSC 110, 111, 112, 117</summary>
    public virtual void ResetXtermColorResource(int resource)
    {
    }

    /// <summary>DECAC</summary>
    public virtual void AssignColor(ColorItem item, int foregroundIndex, int backgroundIndex)
    {
    }

    /// <summary>ED</summary>
    public virtual void EraseInDisplay(EraseType eraseType)
    {
    }

    /// <summary>EL</summary>
    public virtual void EraseInLine(EraseType eraseType)
    {
    }

    /// <summary>ECH</summary>
    public virtual void EraseCharacters(int numChars)
    {
    }

    /// <summary>DECSED</summary>
    public virtual void SelectiveEraseInDisplay(EraseType eraseType)
    {
    }

    /// <summary>DECSEL</summary>
    public virtual void SelectiveEraseInLine(EraseType eraseType)
    {
    }

    /// <summary>DECCARA</summary>
    public virtual void ChangeAttributesRectangularArea(int top, int left, int bottom, int right, VTParameters attrs)
    {
    }

    /// <summary>DECRARA</summary>
    public virtual void ReverseAttributesRectangularArea(int top, int left, int bottom, int right, VTParameters attrs)
    {
    }

    /// <summary>DECCRA</summary>
    public virtual void CopyRectangularArea(int top, int left, int bottom, int right, int page, int destinationTop, int destinationLeft, int destinationPage)
    {
    }

    /// <summary>DECFRA</summary>
    public virtual void FillRectangularArea(VTParameter character, int top, int left, int bottom, int right)
    {
    }

    /// <summary>DECERA</summary>
    public virtual void EraseRectangularArea(int top, int left, int bottom, int right)
    {
    }

    /// <summary>DECSERA</summary>
    public virtual void SelectiveEraseRectangularArea(int top, int left, int bottom, int right)
    {
    }

    /// <summary>DECSACE</summary>
    public virtual void SelectAttributeChangeExtent(ChangeExtent changeExtent)
    {
    }

    /// <summary>DECRQCRA</summary>
    public virtual void RequestChecksumRectangularArea(int id, int page, int top, int left, int bottom, int right)
    {
    }

    /// <summary>SGR</summary>
    public virtual void SetGraphicsRendition(VTParameters options)
    {
    }

    /// <summary>DECSWL, DECDWL, DECDHL</summary>
    public virtual void SetLineRendition(LineRendition rendition)
    {
    }

    /// <summary>DECSCA</summary>
    public virtual void SetCharacterProtectionAttribute(VTParameters options)
    {
    }

    /// <summary>XTPUSHSGR</summary>
    public virtual void PushGraphicsRendition(VTParameters options)
    {
    }

    /// <summary>XTPOPSGR</summary>
    public virtual void PopGraphicsRendition()
    {
    }

    /// <summary>SM, DECSET</summary>
    public virtual void SetMode(ModeParams param)
    {
    }

    /// <summary>RM, DECRST</summary>
    public virtual void ResetMode(ModeParams param)
    {
    }

    /// <summary>DECRQM</summary>
    public virtual void RequestMode(ModeParams param)
    {
    }

    /// <summary>DSR</summary>
    public virtual void DeviceStatusReport(StatusType statusType, VTParameter id)
    {
    }

    /// <summary>DA1</summary>
    public virtual void DeviceAttributes()
    {
    }

    /// <summary>DA2</summary>
    public virtual void SecondaryDeviceAttributes()
    {
    }

    /// <summary>DA3</summary>
    public virtual void TertiaryDeviceAttributes()
    {
    }

    /// <summary>VT52 Identify</summary>
    public virtual void Vt52DeviceAttributes()
    {
    }

    /// <summary>DECREQTPARM</summary>
    public virtual void RequestTerminalParameters(ReportingPermission permission)
    {
    }

    /// <summary>DOCS</summary>
    public virtual void DesignateCodingSystem(VTID codingSystem)
    {
    }

    /// <summary>SCS</summary>
    public virtual void Designate94Charset(int gsetNumber, VTID charset)
    {
    }

    /// <summary>SCS</summary>
    public virtual void Designate96Charset(int gsetNumber, VTID charset)
    {
    }

    /// <summary>LS0, LS1, LS2, LS3</summary>
    public virtual void LockingShift(int gsetNumber)
    {
    }

    /// <summary>LS1R, LS2R, LS3R</summary>
    public virtual void LockingShiftRight(int gsetNumber)
    {
    }

    /// <summary>SS2, SS3</summary>
    public virtual void SingleShift(int gsetNumber)
    {
    }

    /// <summary>DECAC1</summary>
    public virtual void AcceptC1Controls(bool enabled)
    {
    }

    /// <summary>S8C1T, S7C1T</summary>
    public virtual void SendC1Controls(bool enabled)
    {
    }

    /// <summary>ACS</summary>
    public virtual void AnnounceCodeStructure(int ansiLevel)
    {
    }

    /// <summary>DECSTR</summary>
    public virtual void SoftReset()
    {
    }

    /// <summary>RIS</summary>
    public virtual void HardReset(bool erase)
    {
    }

    /// <summary>DECALN</summary>
    public virtual void ScreenAlignmentPattern()
    {
    }

    /// <summary>DECSCUSR</summary>
    public virtual void SetCursorStyle(CursorStyle cursorStyle)
    {
    }

    /// <summary>OSC 52</summary>
    public virtual void SetClipboard(string content)
    {
    }

    /// <summary>XTWINOPS</summary>
    public virtual void WindowManipulation(WindowManipulationType function, VTParameter parameter1, VTParameter parameter2)
    {
    }

    /// <summary>OSC 8</summary>
    public virtual void AddHyperlink(ReadOnlySpan<char> uri, ReadOnlySpan<char> parameters)
    {
    }

    public virtual void EndHyperlink()
    {
    }

    /// <summary>OSC 9</summary>
    public virtual void DoConEmuAction(ReadOnlySpan<char> text)
    {
    }

    /// <summary>OSC 1337</summary>
    public virtual void DoITerm2Action(ReadOnlySpan<char> text)
    {
    }

    /// <summary>OSC 133</summary>
    public virtual void DoFinalTermAction(ReadOnlySpan<char> text)
    {
    }

    /// <summary>OSC 633</summary>
    public virtual void DoVsCodeAction(ReadOnlySpan<char> text)
    {
    }

    /// <summary>OSC 9001</summary>
    public virtual void DoWTAction(ReadOnlySpan<char> text)
    {
    }

    /// <summary>OSC 777</summary>
    public virtual void DoUrxvtAction(ReadOnlySpan<char> text)
    {
    }

    /// <summary>SIXEL</summary>
    public virtual StringHandler? DefineSixelImage(int macroParameter, SixelBackground backgroundSelect, VTParameter backgroundColor) => null;

    /// <summary>DECDLD</summary>
    public virtual StringHandler? DownloadDRCS(int fontNumber, VTParameter startChar, DrcsEraseControl eraseControl, DrcsCellMatrix cellMatrix,
        DrcsFontSet fontSet, DrcsFontUsage fontUsage, VTParameter cellHeight, CharsetSize charsetSize) => null;

    /// <summary>DECRQUPSS</summary>
    public virtual void RequestUserPreferenceCharset()
    {
    }

    /// <summary>DECAUPSS</summary>
    public virtual StringHandler? AssignUserPreferenceCharset(CharsetSize charsetSize) => null;

    /// <summary>DECDMAC</summary>
    public virtual StringHandler? DefineMacro(int macroId, MacroDeleteControl deleteControl, MacroEncoding encoding) => null;

    /// <summary>DECINVM</summary>
    public virtual void InvokeMacro(int macroId)
    {
    }

    /// <summary>DECRQTSR</summary>
    public virtual void RequestTerminalStateReport(ReportFormat format, VTParameter formatOption)
    {
    }

    /// <summary>DECRSTS</summary>
    public virtual StringHandler? RestoreTerminalState(ReportFormat format) => null;

    /// <summary>DECRQSS</summary>
    public virtual StringHandler? RequestSetting() => null;

    /// <summary>DECRQPSR</summary>
    public virtual void RequestPresentationStateReport(PresentationReportFormat format)
    {
    }

    /// <summary>DECRSPS</summary>
    public virtual StringHandler? RestorePresentationState(PresentationReportFormat format) => null;

    /// <summary>DECPS</summary>
    public virtual void PlaySounds(VTParameters parameters)
    {
    }

    public virtual void SetOptionalFeatures(OptionalFeature features)
    {
    }
}
