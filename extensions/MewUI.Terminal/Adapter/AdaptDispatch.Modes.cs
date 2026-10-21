// Ported from Windows Terminal src/terminal/adapter/adaptDispatch.cpp (MIT).

using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Unicode;

namespace Aprillz.MewUI.Terminal.Adapter;

internal sealed partial class AdaptDispatch
{
    private void SetColumnMode(bool enable)
    {
        // DECCOLM only works while XTerm's mode 40 allows it.
        if (HasMode(Mode.AllowDECCOLM))
        {
            var page = _pages.VisiblePage();
            int pageHeight = page.Height;
            int pageWidth = enable ? DispatchTypes.DECCOLM_SET_COLUMNS : DispatchTypes.DECCOLM_RESET_COLUMNS;
            _api.ResizeWindow(pageWidth, pageHeight);
            SetModeFlag(Mode.Column, enable);
            SetModeFlag(Mode.Origin | Mode.AllowDECSLRM, false);
            CursorPosition(1, 1);
            EraseInDisplay(EraseType.All);
            DoSetTopBottomScrollingMargins(0, 0);
            DoSetLeftRightScrollingMargins(0, 0);
        }
    }

    private void SetAlternateScreenBufferMode(bool enable)
    {
        // Mode 1049 saves the cursor on the way in and restores it on the way out.
        if (enable)
        {
            CursorSaveState();
            var page = _pages.ActivePage();
            _api.UseAlternateScreenBuffer(GetEraseAttributes(page));
            _usingAltBuffer = true;
        }
        else
        {
            _api.UseMainScreenBuffer();
            _usingAltBuffer = false;
            CursorRestoreState();
        }
    }

    private void ModeParamsHelper(ModeParams param, bool enable)
    {
        switch (param)
        {
            case ModeParams.IRM_InsertReplaceMode:
                SetModeFlag(Mode.InsertReplace, enable);
                break;
            case ModeParams.LNM_LineFeedNewLineMode:
                // LNM is only changed while the system and input modes agree; a host that set them apart keeps them.
                if (_api.GetSystemMode(ITerminalApi.Mode.LineFeed) == _terminalInput.GetInputMode(TerminalInput.Mode.LineFeed))
                {
                    _api.SetSystemMode(ITerminalApi.Mode.LineFeed, enable);
                    _terminalInput.SetInputMode(TerminalInput.Mode.LineFeed, enable);
                }

                break;
            case ModeParams.DECCKM_CursorKeysMode:
                _terminalInput.SetInputMode(TerminalInput.Mode.CursorKey, enable);
                break;
            case ModeParams.DECANM_AnsiMode:
                SetAnsiMode(enable);
                break;
            case ModeParams.DECCOLM_SetNumberOfColumns:
                SetColumnMode(enable);
                break;
            case ModeParams.DECSCNM_ScreenMode:
                _renderSettings.SetRenderMode(RenderSettings.Mode.ScreenReversed, enable);
                _renderer?.TriggerRedrawAll();
                break;
            case ModeParams.DECOM_OriginMode:
                SetModeFlag(Mode.Origin, enable);
                CursorPosition(1, 1);
                break;
            case ModeParams.DECAWM_AutoWrapMode:
                _api.SetSystemMode(ITerminalApi.Mode.AutoWrap, enable);
                if (!enable)
                {
                    _pages.ActivePage().Cursor.ResetDelayEolWrap();
                }

                break;
            case ModeParams.DECARM_AutoRepeatMode:
                _terminalInput.SetInputMode(TerminalInput.Mode.AutoRepeat, enable);
                break;
            case ModeParams.ATT610_StartCursorBlink:
                _pages.ActivePage().Cursor.SetIsBlinking(enable);
                break;
            case ModeParams.DECTCEM_TextCursorEnableMode:
                _pages.ActivePage().Cursor.SetIsVisible(enable);
                break;
            case ModeParams.XTERM_EnableDECCOLMSupport:
                SetModeFlag(Mode.AllowDECCOLM, enable);
                break;
            case ModeParams.DECPCCM_PageCursorCouplingMode:
                SetModeFlag(Mode.PageCursorCoupling, enable);
                if (enable)
                {
                    _pages.MakeActivePageVisible();
                }

                break;
            case ModeParams.DECNKM_NumericKeypadMode:
                _terminalInput.SetInputMode(TerminalInput.Mode.Keypad, enable);
                break;
            case ModeParams.DECBKM_BackarrowKeyMode:
                _terminalInput.SetInputMode(TerminalInput.Mode.BackarrowKey, enable);
                break;
            case ModeParams.DECLRMM_LeftRightMarginMode:
                SetModeFlag(Mode.AllowDECSLRM, enable);
                DoSetLeftRightScrollingMargins(0, 0);

                // Line renditions cannot be used with left and right margins.
                if (enable)
                {
                    var page = _pages.ActivePage();
                    page.Buffer.ResetLineRenditionRange(page.Top, page.Bottom);
                }

                break;
            case ModeParams.DECSDM_SixelDisplayMode:
                SetModeFlag(Mode.SixelDisplay, enable);
                break;
            case ModeParams.DECECM_EraseColorMode:
                SetModeFlag(Mode.EraseColor, enable);
                break;
            case ModeParams.VT200_MOUSE_MODE:
                _terminalInput.SetInputMode(TerminalInput.Mode.DefaultMouseTracking, enable);
                break;
            case ModeParams.BUTTON_EVENT_MOUSE_MODE:
                _terminalInput.SetInputMode(TerminalInput.Mode.ButtonEventMouseTracking, enable);
                break;
            case ModeParams.ANY_EVENT_MOUSE_MODE:
                _terminalInput.SetInputMode(TerminalInput.Mode.AnyEventMouseTracking, enable);
                break;
            case ModeParams.UTF8_EXTENDED_MODE:
                _terminalInput.SetInputMode(TerminalInput.Mode.Utf8MouseEncoding, enable);
                break;
            case ModeParams.SGR_EXTENDED_MODE:
                _terminalInput.SetInputMode(TerminalInput.Mode.SgrMouseEncoding, enable);
                break;
            case ModeParams.FOCUS_EVENT_MODE:
                _terminalInput.SetInputMode(TerminalInput.Mode.FocusEvent, enable);
                break;
            case ModeParams.ALTERNATE_SCROLL:
                _terminalInput.SetInputMode(TerminalInput.Mode.AlternateScroll, enable);
                break;
            case ModeParams.ASB_AlternateScreenBuffer:
                SetAlternateScreenBufferMode(enable);
                break;
            case ModeParams.XTERM_BracketedPasteMode:
                _api.SetSystemMode(ITerminalApi.Mode.BracketedPaste, enable);
                break;
            case ModeParams.SO_SynchronizedOutput:
                _renderSettings.SetRenderMode(RenderSettings.Mode.SynchronizedOutput, enable);
                _renderer?.NotifySynchronizedOutputChanged(enable);
                break;
            case ModeParams.GCM_GraphemeClusterMode:
                // Grapheme clustering is fixed by the width detector's mode.
                break;
        }
    }

    public override void SetMode(ModeParams param) => ModeParamsHelper(param, true);

    public override void ResetMode(ModeParams param) => ModeParamsHelper(param, false);

    public override void RequestMode(ModeParams param)
    {
        static ModeResponses MapTemp(bool enabled) => enabled ? ModeResponses.DECRPM_Enabled : ModeResponses.DECRPM_Disabled;

        static ModeResponses MapPerm(bool enabled) => enabled ? ModeResponses.DECRPM_PermanentlyEnabled : ModeResponses.DECRPM_PermanentlyDisabled;

        var state = ModeResponses.DECRPM_Unsupported;

        switch (param)
        {
            case ModeParams.IRM_InsertReplaceMode:
                state = MapTemp(HasMode(Mode.InsertReplace));
                break;
            case ModeParams.LNM_LineFeedNewLineMode:
                // LNM is only reported while the system and input modes agree.
                if (_api.GetSystemMode(ITerminalApi.Mode.LineFeed) == _terminalInput.GetInputMode(TerminalInput.Mode.LineFeed))
                {
                    state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.LineFeed));
                }

                break;
            case ModeParams.DECCKM_CursorKeysMode:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.CursorKey));
                break;
            case ModeParams.DECANM_AnsiMode:
                state = MapTemp(_api.GetStateMachine().GetParserMode(StateMachine.Mode.Ansi));
                break;
            case ModeParams.DECCOLM_SetNumberOfColumns:
                state = MapTemp(HasMode(Mode.Column));
                break;
            case ModeParams.DECSCNM_ScreenMode:
                state = MapTemp(_renderSettings.GetRenderMode(RenderSettings.Mode.ScreenReversed));
                break;
            case ModeParams.DECOM_OriginMode:
                state = MapTemp(HasMode(Mode.Origin));
                break;
            case ModeParams.DECAWM_AutoWrapMode:
                state = MapTemp(_api.GetSystemMode(ITerminalApi.Mode.AutoWrap));
                break;
            case ModeParams.DECARM_AutoRepeatMode:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.AutoRepeat));
                break;
            case ModeParams.ATT610_StartCursorBlink:
                state = MapTemp(_pages.ActivePage().Cursor.IsBlinking);
                break;
            case ModeParams.DECTCEM_TextCursorEnableMode:
                state = MapTemp(_pages.ActivePage().Cursor.IsVisible);
                break;
            case ModeParams.XTERM_EnableDECCOLMSupport:
                state = MapTemp(HasMode(Mode.AllowDECCOLM));
                break;
            case ModeParams.DECPCCM_PageCursorCouplingMode:
                state = MapTemp(HasMode(Mode.PageCursorCoupling));
                break;
            case ModeParams.DECNKM_NumericKeypadMode:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.Keypad));
                break;
            case ModeParams.DECBKM_BackarrowKeyMode:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.BackarrowKey));
                break;
            case ModeParams.DECLRMM_LeftRightMarginMode:
                state = MapTemp(HasMode(Mode.AllowDECSLRM));
                break;
            case ModeParams.DECSDM_SixelDisplayMode:
                state = MapTemp(HasMode(Mode.SixelDisplay));
                break;
            case ModeParams.DECECM_EraseColorMode:
                state = MapTemp(HasMode(Mode.EraseColor));
                break;
            case ModeParams.VT200_MOUSE_MODE:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.DefaultMouseTracking));
                break;
            case ModeParams.BUTTON_EVENT_MOUSE_MODE:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.ButtonEventMouseTracking));
                break;
            case ModeParams.ANY_EVENT_MOUSE_MODE:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.AnyEventMouseTracking));
                break;
            case ModeParams.UTF8_EXTENDED_MODE:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.Utf8MouseEncoding));
                break;
            case ModeParams.SGR_EXTENDED_MODE:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.SgrMouseEncoding));
                break;
            case ModeParams.FOCUS_EVENT_MODE:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.FocusEvent));
                break;
            case ModeParams.ALTERNATE_SCROLL:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.AlternateScroll));
                break;
            case ModeParams.ASB_AlternateScreenBuffer:
                state = MapTemp(_usingAltBuffer);
                break;
            case ModeParams.XTERM_BracketedPasteMode:
                state = MapTemp(_api.GetSystemMode(ITerminalApi.Mode.BracketedPaste));
                break;
            case ModeParams.SO_SynchronizedOutput:
                state = MapTemp(_renderSettings.GetRenderMode(RenderSettings.Mode.SynchronizedOutput));
                break;
            case ModeParams.GCM_GraphemeClusterMode:
                state = MapPerm(CodepointWidthDetector.Singleton.Mode == TextMeasurementMode.Graphemes);
                break;
            case ModeParams.W32IM_Win32InputMode:
                state = MapTemp(_terminalInput.GetInputMode(TerminalInput.Mode.Win32));
                break;
        }

        string prefix = DispatchTypes.IsDecPrivate(param) ? "?" : string.Empty;
        ReturnCsiResponse($"{prefix}{DispatchTypes.ModeNumber(param)};{(int)state}$y");
    }

    public override void SetKeypadMode(bool applicationMode) => _terminalInput.SetInputMode(TerminalInput.Mode.Keypad, applicationMode);

    public override void SetAnsiMode(bool ansiMode)
    {
        // Switching between ANSI and VT52 also resets the character sets.
        _termOutput.SoftReset();
        _api.GetStateMachine().SetParserMode(StateMachine.Mode.Ansi, ansiMode);
        _terminalInput.SetInputMode(TerminalInput.Mode.Ansi, ansiMode);
    }
}
