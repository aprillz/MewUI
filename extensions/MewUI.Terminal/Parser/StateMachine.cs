// Ported from Windows Terminal src/terminal/parser/stateMachine.hpp, stateMachine.cpp, IStateMachineEngine.hpp and
// ascii.hpp (MIT). The parser follows the VT state diagram of vt100.net; the engine decides what each action means.

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Parser;

internal static class AsciiChars
{
    public const char NUL = '\u0000';
    public const char ENQ = '\u0005';
    public const char BEL = '\u0007';
    public const char BS = '\u0008';
    public const char TAB = '\u0009';
    public const char LF = '\u000A';
    public const char VT = '\u000B';
    public const char FF = '\u000C';
    public const char CR = '\u000D';
    public const char SO = '\u000E';
    public const char SI = '\u000F';
    public const char ETB = '\u0017';
    public const char CAN = '\u0018';
    public const char EM = '\u0019';
    public const char SUB = '\u001A';
    public const char ESC = '\u001B';
    public const char FS = '\u001C';
    public const char US = '\u001F';
    public const char SPC = ' ';
    public const char DEL = '\u007F';
}

/// <summary>What the state machine reports as it recognizes characters and sequences.</summary>
internal interface IStateMachineEngine
{
    void UnknownSequence();

    bool EncounteredWin32InputModeSequence();

    bool ActionExecute(char character);

    bool ActionExecuteFromEscape(char character);

    bool ActionPrint(char character);

    bool ActionPrintString(ReadOnlySpan<char> text);

    bool ActionPassThroughString(ReadOnlySpan<char> text);

    bool ActionEscDispatch(VTID id);

    bool ActionVt52EscDispatch(VTID id, VTParameters parameters);

    bool ActionCsiDispatch(VTID id, VTParameters parameters);

    StringHandler? ActionDcsDispatch(VTID id, VTParameters parameters);

    bool ActionOscDispatch(int parameter, ReadOnlySpan<char> text);

    bool ActionSs3Dispatch(char character, VTParameters parameters);
}

/// <summary>
/// Splits a character stream into printable runs, control characters and escape sequences (ESC, CSI, OSC, DCS, SS3,
/// VT52), with parameters, sub-parameters and intermediates, and hands each to an <see cref="IStateMachineEngine"/>.
/// A sequence may span several calls to <see cref="ProcessString(ReadOnlyMemory{char})"/>.
/// </summary>
internal sealed class StateMachine
{
    // DEC STD 070 asks for at least 16384; 65535 is what xterm and VTE take, and win32-input-mode needs it.
    public const int MAX_PARAMETER_VALUE = 65535;

    // DEC STD 070 asks for at least 16; terminals commonly allow about twice that.
    public const int MAX_PARAMETER_COUNT = 32;

    public const int MAX_SUBPARAMETER_COUNT = 6;

    [Flags]
    public enum Mode
    {
        None = 0,
        AcceptC1 = 1 << 0,
        Ansi = 1 << 1,
    }

    private enum VTStates
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiIntermediate,
        CsiIgnore,
        CsiParam,
        CsiSubParam,
        OscParam,
        OscString,
        OscTermination,
        Ss3Entry,
        Ss3Param,
        Vt52Param,
        DcsEntry,
        DcsIgnore,
        DcsIntermediate,
        DcsParam,
        DcsPassThrough,
        SosPmApcString,
    }

    private readonly IStateMachineEngine _engine;
    private readonly bool _isEngineForInput;
    private readonly List<VTParameter> _parameters = new();
    private readonly List<VTParameter> _subParameters = new();
    private readonly List<(byte Start, byte End)> _subParameterRanges = new();
    private readonly System.Text.StringBuilder _oscString = new();

    private VTStates _state = VTStates.Ground;
    private Mode _parserMode = Mode.Ansi;
    private ReadOnlyMemory<char> _currentString;
    private int _runOffset;
    private int _runSize;
    private VTIDBuilder _identifier;
    private bool _parameterLimitOverflowed;
    private bool _subParameterLimitOverflowed;
    private int _subParameterCounter;
    private int _oscParameter;
    private StringHandler? _dcsStringHandler;
    private System.Text.StringBuilder? _cachedSequence;
    private bool _processingLastCharacter;
    private Action? _onCsiCompleteCallback;

    public StateMachine(IStateMachineEngine engine, bool isEngineForInput = false)
    {
        _engine = engine;
        _isEngineForInput = isEngineForInput;
        SetParserMode(Mode.AcceptC1, isEngineForInput);
        ActionClear();
    }

    public IStateMachineEngine Engine => _engine;

    /// <summary>Whether the character being processed ends the current chunk of input.</summary>
    public bool IsProcessingLastCharacter => _processingLastCharacter;

    public void SetParserMode(Mode mode, bool enabled)
    {
        if (enabled)
        {
            _parserMode |= mode;
        }
        else
        {
            _parserMode &= ~mode;
        }
    }

    public bool GetParserMode(Mode mode) => (_parserMode & mode) != 0;

    /// <summary>Runs <paramref name="callback"/> once the CSI sequence being dispatched has finished.</summary>
    public void OnCsiComplete(Action callback) => _onCsiCompleteCallback = callback;

    /// <summary>Abandons any sequence in progress.</summary>
    public void ResetState() => EnterGround();

    private static bool IsNumericParamValue(char character) => character is >= '0' and <= '9';

    private static bool IsC0Code(char character)
        => (character >= AsciiChars.NUL && character <= AsciiChars.ETB) || character == AsciiChars.EM || (character >= AsciiChars.FS && character <= AsciiChars.US);

    private static bool IsC1ControlCharacter(char character) => character is >= '\u0080' and <= '\u009F';

    private static char C1To7Bit(char character) => (char)(character - 0x40);

    private static bool IsIntermediate(char character) => character is >= ' ' and <= '/';

    private static bool IsDelete(char character) => character == AsciiChars.DEL;

    private static bool IsEscape(char character) => character == AsciiChars.ESC;

    private static bool IsParameterDelimiter(char character) => character == ';';

    private static bool IsSubParameterDelimiter(char character) => character == ':';

    private static bool IsCsiIndicator(char character) => character == '[';

    private static bool IsCsiPrivateMarker(char character) => character is '<' or '=' or '>' or '?';

    private static bool IsIntermediateInvalid(char character)
        => IsNumericParamValue(character) || IsSubParameterDelimiter(character) || IsParameterDelimiter(character) || IsCsiPrivateMarker(character);

    private static bool IsParameterInvalid(char character) => IsCsiPrivateMarker(character);

    private static bool IsStringTerminatorIndicator(char character) => character == '\\';

    private static bool IsSs3Indicator(char character) => character == 'O';

    private static bool IsVt52CursorAddress(char character) => character == 'Y';

    private static bool IsOscIndicator(char character) => character == ']';

    private static bool IsOscDelimiter(char character) => character == ';';

    private static bool IsOscInvalid(char character)
        => character <= '\u0017' || character == '\u0019' || (character >= '\u001C' && character <= '\u001F');

    private static bool IsOscTerminator(char character) => character == AsciiChars.BEL;

    private static bool IsDcsIndicator(char character) => character == 'P';

    private static bool IsDcsPassThroughValid(char character) => character >= AsciiChars.SPC && character < AsciiChars.DEL;

    private static bool IsSosIndicator(char character) => character == 'X';

    private static bool IsPmIndicator(char character) => character == '^';

    private static bool IsApcIndicator(char character) => character == '_';

    private ReadOnlySpan<char> CurrentRun => _currentString.Span.Slice(_runOffset, _runSize);

    private VTParameters CsiParameters
        => new(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_parameters),
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_subParameters),
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_subParameterRanges));

    private VTParameters PlainParameters => new(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_parameters));

    private void ActionExecute(char character) => SafeExecute(_engine.ActionExecute(character));

    private void ActionExecuteFromEscape(char character) => SafeExecute(_engine.ActionExecuteFromEscape(character));

    private void ActionPrint(char character) => SafeExecute(_engine.ActionPrint(character));

    private void ActionPrintString(ReadOnlySpan<char> text) => SafeExecute(_engine.ActionPrintString(text));

    private void ActionEscDispatch(char character) => SafeExecute(_engine.ActionEscDispatch(_identifier.Finalize(character)));

    private void ActionVt52EscDispatch(char character) => SafeExecute(_engine.ActionVt52EscDispatch(_identifier.Finalize(character), PlainParameters));

    private void ActionCsiDispatch(char character) => SafeExecute(_engine.ActionCsiDispatch(_identifier.Finalize(character), CsiParameters));

    private void ActionCollect(char character) => _identifier.AddIntermediate(character);

    private void ActionParam(char character)
    {
        if (_parameterLimitOverflowed)
        {
            return;
        }

        // The first parameter or delimiter begins the first parameter.
        if (_parameters.Count == 0)
        {
            _parameters.Add(VTParameter.Omitted);
            byte rangeStart = (byte)_subParameters.Count;
            _subParameterRanges.Add((rangeStart, rangeStart));
        }

        if (IsParameterDelimiter(character))
        {
            if (_parameters.Count >= MAX_PARAMETER_COUNT)
            {
                _parameterLimitOverflowed = true;
            }
            else
            {
                _parameters.Add(VTParameter.Omitted);
                _subParameterCounter = 0;
                _subParameterLimitOverflowed = false;
                byte rangeStart = (byte)_subParameters.Count;
                _subParameterRanges.Add((rangeStart, rangeStart));
            }
        }
        else
        {
            int current = _parameters[^1].ValueOr(0);
            AccumulateTo(character, ref current);
            _parameters[^1] = current;
        }
    }

    private void ActionSubParam(char character)
    {
        if (_subParameterLimitOverflowed)
        {
            return;
        }

        if (_parameters.Count == 0)
        {
            _parameters.Add(VTParameter.Omitted);
            byte rangeStart = (byte)_subParameters.Count;
            _subParameterRanges.Add((rangeStart, rangeStart));
        }

        if (IsSubParameterDelimiter(character))
        {
            if (_subParameterCounter >= MAX_SUBPARAMETER_COUNT)
            {
                _subParameterLimitOverflowed = true;
            }
            else
            {
                _subParameters.Add(VTParameter.Omitted);
                var range = _subParameterRanges[^1];
                _subParameterRanges[^1] = (range.Start, (byte)(range.End + 1));
                _subParameterCounter++;
            }
        }
        else
        {
            int current = _subParameters[^1].ValueOr(0);
            AccumulateTo(character, ref current);
            _subParameters[^1] = current;
        }
    }

    private void ActionClear()
    {
        _identifier.Clear();
        _parameters.Clear();
        _parameterLimitOverflowed = false;
        _subParameters.Clear();
        _subParameterRanges.Clear();
        _subParameterCounter = 0;
        _subParameterLimitOverflowed = false;
        _oscString.Clear();
        _oscParameter = 0;
        _dcsStringHandler = null;
    }

    private static void ActionIgnore()
    {
    }

    /// <summary>Ends a DCS string that an ESC or a cancel interrupts, telling its handler with an ESC.</summary>
    private void ActionInterrupt()
    {
        if (_state == VTStates.DcsPassThrough)
        {
            _dcsStringHandler?.Invoke(AsciiChars.ESC);
            _dcsStringHandler = null;
        }
    }

    private void ActionOscParam(char character) => AccumulateTo(character, ref _oscParameter);

    private void ActionOscPut(char character) => _oscString.Append(character);

    private void ActionOscDispatch()
    {
        string text = _oscString.ToString();
        SafeExecute(_engine.ActionOscDispatch(_oscParameter, text));
    }

    private void ActionSs3Dispatch(char character) => SafeExecute(_engine.ActionSs3Dispatch(character, PlainParameters));

    private void ActionDcsDispatch(char character)
    {
        _dcsStringHandler = _engine.ActionDcsDispatch(_identifier.Finalize(character), PlainParameters);
        if (_dcsStringHandler != null)
        {
            EnterDcsPassThrough();
        }
        else
        {
            EnterDcsIgnore();
        }
    }

    private void EnterGround()
    {
        _state = VTStates.Ground;

        // Ground means the pending sequence is complete.
        _cachedSequence = null;
    }

    private void EnterEscape()
    {
        _state = VTStates.Escape;
        ActionClear();
    }

    private void EnterEscapeIntermediate() => _state = VTStates.EscapeIntermediate;

    private void EnterCsiEntry()
    {
        _state = VTStates.CsiEntry;
        ActionClear();
    }

    private void EnterCsiParam() => _state = VTStates.CsiParam;

    private void EnterCsiSubParam() => _state = VTStates.CsiSubParam;

    private void EnterCsiIgnore() => _state = VTStates.CsiIgnore;

    private void EnterCsiIntermediate() => _state = VTStates.CsiIntermediate;

    private void EnterOscParam() => _state = VTStates.OscParam;

    private void EnterOscString() => _state = VTStates.OscString;

    private void EnterOscTermination() => _state = VTStates.OscTermination;

    private void EnterSs3Entry()
    {
        _state = VTStates.Ss3Entry;
        ActionClear();
    }

    private void EnterSs3Param() => _state = VTStates.Ss3Param;

    private void EnterVt52Param() => _state = VTStates.Vt52Param;

    private void EnterDcsEntry()
    {
        _state = VTStates.DcsEntry;
        ActionClear();
    }

    private void EnterDcsParam() => _state = VTStates.DcsParam;

    private void EnterDcsIgnore()
    {
        _state = VTStates.DcsIgnore;
        _cachedSequence = null;
    }

    private void EnterDcsIntermediate() => _state = VTStates.DcsIntermediate;

    private void EnterDcsPassThrough()
    {
        _state = VTStates.DcsPassThrough;
        _cachedSequence = null;
    }

    private void EnterSosPmApcString()
    {
        _state = VTStates.SosPmApcString;
        _cachedSequence = null;
        _engine.UnknownSequence();
    }

    private void EventGround(char character)
    {
        if (IsC0Code(character) || IsDelete(character))
        {
            ActionExecute(character);
        }
        else
        {
            ActionPrint(character);
        }
    }

    private void EventEscape(char character)
    {
        if (IsC0Code(character))
        {
            if (_isEngineForInput)
            {
                ActionExecuteFromEscape(character);
                EnterGround();
            }
            else
            {
                ActionExecute(character);
            }
        }
        else if (IsDelete(character))
        {
            ActionIgnore();
        }
        else if (IsIntermediate(character))
        {
            if (_isEngineForInput)
            {
                ActionEscDispatch(character);
                EnterGround();
            }
            else
            {
                ActionCollect(character);
                EnterEscapeIntermediate();
            }
        }
        else if ((_parserMode & Mode.Ansi) != 0)
        {
            if (IsCsiIndicator(character))
            {
                EnterCsiEntry();
            }
            else if (IsOscIndicator(character))
            {
                EnterOscParam();
            }
            else if (IsSs3Indicator(character) && _isEngineForInput)
            {
                EnterSs3Entry();
            }
            else if (IsDcsIndicator(character))
            {
                EnterDcsEntry();
            }
            else if (IsSosIndicator(character) || IsPmIndicator(character) || IsApcIndicator(character))
            {
                EnterSosPmApcString();
            }
            else
            {
                ActionEscDispatch(character);
                EnterGround();
            }
        }
        else if (IsVt52CursorAddress(character))
        {
            EnterVt52Param();
        }
        else
        {
            ActionVt52EscDispatch(character);
            EnterGround();
        }
    }

    private void EventEscapeIntermediate(char character)
    {
        if (IsC0Code(character))
        {
            ActionExecute(character);
        }
        else if (IsIntermediate(character))
        {
            ActionCollect(character);
        }
        else if (IsDelete(character))
        {
            ActionIgnore();
        }
        else if ((_parserMode & Mode.Ansi) != 0)
        {
            ActionEscDispatch(character);
            EnterGround();
        }
        else if (IsVt52CursorAddress(character))
        {
            EnterVt52Param();
        }
        else
        {
            ActionVt52EscDispatch(character);
            EnterGround();
        }
    }

    private void EventCsiEntry(char character)
    {
        if (IsC0Code(character))
        {
            ActionExecute(character);
        }
        else if (IsDelete(character))
        {
            ActionIgnore();
        }
        else if (IsIntermediate(character))
        {
            ActionCollect(character);
            EnterCsiIntermediate();
        }
        else if (IsNumericParamValue(character) || IsParameterDelimiter(character))
        {
            ActionParam(character);
            EnterCsiParam();
        }
        else if (IsSubParameterDelimiter(character))
        {
            ActionSubParam(character);
            EnterCsiSubParam();
        }
        else if (IsCsiPrivateMarker(character))
        {
            ActionCollect(character);
            EnterCsiParam();
        }
        else
        {
            ActionCsiDispatch(character);
            EnterGround();
            ExecuteCsiCompleteCallback();
        }
    }

    private void EventCsiIntermediate(char character)
    {
        if (IsC0Code(character))
        {
            ActionExecute(character);
        }
        else if (IsIntermediate(character))
        {
            ActionCollect(character);
        }
        else if (IsDelete(character))
        {
            ActionIgnore();
        }
        else if (IsIntermediateInvalid(character))
        {
            EnterCsiIgnore();
        }
        else
        {
            ActionCsiDispatch(character);
            EnterGround();
            ExecuteCsiCompleteCallback();
        }
    }

    private void EventCsiIgnore(char character)
    {
        if (IsC0Code(character))
        {
            ActionExecute(character);
        }
        else if (IsDelete(character) || IsIntermediate(character) || IsIntermediateInvalid(character))
        {
            ActionIgnore();
        }
        else
        {
            EnterGround();
        }
    }

    private void EventCsiParam(char character)
    {
        if (IsC0Code(character))
        {
            ActionExecute(character);
        }
        else if (IsDelete(character))
        {
            ActionIgnore();
        }
        else if (IsNumericParamValue(character) || IsParameterDelimiter(character))
        {
            ActionParam(character);
        }
        else if (IsSubParameterDelimiter(character))
        {
            ActionSubParam(character);
            EnterCsiSubParam();
        }
        else if (IsIntermediate(character))
        {
            ActionCollect(character);
            EnterCsiIntermediate();
        }
        else if (IsParameterInvalid(character))
        {
            EnterCsiIgnore();
        }
        else
        {
            ActionCsiDispatch(character);
            EnterGround();
            ExecuteCsiCompleteCallback();
        }
    }

    private void EventCsiSubParam(char character)
    {
        if (IsC0Code(character))
        {
            ActionExecute(character);
        }
        else if (IsDelete(character))
        {
            ActionIgnore();
        }
        else if (IsNumericParamValue(character) || IsSubParameterDelimiter(character))
        {
            ActionSubParam(character);
        }
        else if (IsParameterDelimiter(character))
        {
            ActionParam(character);
            EnterCsiParam();
        }
        else if (IsIntermediate(character))
        {
            ActionCollect(character);
            EnterCsiIntermediate();
        }
        else if (IsParameterInvalid(character))
        {
            EnterCsiIgnore();
        }
        else
        {
            ActionCsiDispatch(character);
            EnterGround();
            ExecuteCsiCompleteCallback();
        }
    }

    private void EventOscParam(char character)
    {
        if (IsOscTerminator(character))
        {
            ActionOscDispatch();
            EnterGround();
        }
        else if (IsEscape(character))
        {
            EnterOscTermination();
        }
        else if (IsNumericParamValue(character))
        {
            ActionOscParam(character);
        }
        else if (IsOscDelimiter(character))
        {
            EnterOscString();
        }
        else
        {
            ActionIgnore();
        }
    }

    private void EventOscString(char character)
    {
        if (IsOscTerminator(character))
        {
            ActionOscDispatch();
            EnterGround();
        }
        else if (IsEscape(character))
        {
            EnterOscTermination();
        }
        else if (IsOscInvalid(character))
        {
            ActionIgnore();
        }
        else
        {
            ActionOscPut(character);
        }
    }

    private void EventOscTermination(char character)
    {
        if (IsStringTerminatorIndicator(character))
        {
            ActionOscDispatch();
            EnterGround();
        }
        else
        {
            EnterEscape();
            EventEscape(character);
        }
    }

    private void EventSs3Entry(char character)
    {
        if (IsC0Code(character))
        {
            ActionExecute(character);
        }
        else if (IsDelete(character))
        {
            ActionIgnore();
        }
        else if (IsSubParameterDelimiter(character))
        {
            EnterCsiIgnore();
        }
        else if (IsNumericParamValue(character) || IsParameterDelimiter(character))
        {
            ActionParam(character);
            EnterSs3Param();
        }
        else
        {
            ActionSs3Dispatch(character);
            EnterGround();
        }
    }

    private void EventSs3Param(char character)
    {
        if (IsC0Code(character))
        {
            ActionExecute(character);
        }
        else if (IsDelete(character))
        {
            ActionIgnore();
        }
        else if (IsNumericParamValue(character) || IsParameterDelimiter(character))
        {
            ActionParam(character);
        }
        else if (IsParameterInvalid(character) || IsSubParameterDelimiter(character))
        {
            EnterCsiIgnore();
        }
        else
        {
            ActionSs3Dispatch(character);
            EnterGround();
        }
    }

    private void EventVt52Param(char character)
    {
        if (IsC0Code(character))
        {
            ActionExecute(character);
        }
        else if (IsDelete(character))
        {
            ActionIgnore();
        }
        else
        {
            _parameters.Add(character);
            if (_parameters.Count == 2)
            {
                ActionVt52EscDispatch('Y');
                EnterGround();
            }
        }
    }

    private void EventDcsEntry(char character)
    {
        if (IsC0Code(character) || IsDelete(character))
        {
            ActionIgnore();
        }
        else if (IsSubParameterDelimiter(character))
        {
            EnterDcsIgnore();
        }
        else if (IsNumericParamValue(character) || IsParameterDelimiter(character))
        {
            ActionParam(character);
            EnterDcsParam();
        }
        else if (IsIntermediate(character))
        {
            ActionCollect(character);
            EnterDcsIntermediate();
        }
        else
        {
            ActionDcsDispatch(character);
        }
    }

    private void EventDcsIntermediate(char character)
    {
        if (IsC0Code(character) || IsDelete(character))
        {
            ActionIgnore();
        }
        else if (IsIntermediate(character))
        {
            ActionCollect(character);
        }
        else if (IsIntermediateInvalid(character))
        {
            EnterDcsIgnore();
        }
        else
        {
            ActionDcsDispatch(character);
        }
    }

    private void EventDcsParam(char character)
    {
        // As in the original, a C0 or DEL is ignored and then still goes through the checks below.
        if (IsC0Code(character) || IsDelete(character))
        {
            ActionIgnore();
        }

        if (IsNumericParamValue(character) || IsParameterDelimiter(character))
        {
            ActionParam(character);
        }
        else if (IsIntermediate(character))
        {
            ActionCollect(character);
            EnterDcsIntermediate();
        }
        else if (IsParameterInvalid(character) || IsSubParameterDelimiter(character))
        {
            EnterDcsIgnore();
        }
        else
        {
            ActionDcsDispatch(character);
        }
    }

    private void EventDcsPassThrough(char character)
    {
        if (IsC0Code(character) || IsDcsPassThroughValid(character))
        {
            if (!_dcsStringHandler!(character))
            {
                EnterDcsIgnore();
            }
        }
        else
        {
            ActionIgnore();
        }
    }

    /// <summary>Feeds one character through the state machine.</summary>
    public void ProcessCharacter(char character)
    {
        // CAN and SUB cancel a sequence from any state; the input engine keeps ESC CAN and ESC SUB as Ctrl+Alt keys.
        bool isFromAnywhereChar = character == AsciiChars.CAN || character == AsciiChars.SUB;
        if (isFromAnywhereChar && !(_state == VTStates.Escape && _isEngineForInput))
        {
            ActionInterrupt();
            ActionExecute(character);
            EnterGround();
        }
        else if (IsC1ControlCharacter(character))
        {
            // C1 controls act as ESC + their 7-bit form only when asked for, since some code pages map ordinary
            // characters there.
            if ((_parserMode & Mode.AcceptC1) != 0)
            {
                ProcessCharacter(AsciiChars.ESC);
                ProcessCharacter(C1To7Bit(character));
            }
        }
        else if (IsEscape(character) && _state != VTStates.OscString && _state != VTStates.OscParam)
        {
            // ESC in an OSC string is the start of its terminator, not a new sequence.
            ActionInterrupt();
            EnterEscape();
        }
        else
        {
            switch (_state)
            {
                case VTStates.Ground:
                    EventGround(character);
                    break;
                case VTStates.Escape:
                    EventEscape(character);
                    break;
                case VTStates.EscapeIntermediate:
                    EventEscapeIntermediate(character);
                    break;
                case VTStates.CsiEntry:
                    EventCsiEntry(character);
                    break;
                case VTStates.CsiIntermediate:
                    EventCsiIntermediate(character);
                    break;
                case VTStates.CsiIgnore:
                    EventCsiIgnore(character);
                    break;
                case VTStates.CsiParam:
                    EventCsiParam(character);
                    break;
                case VTStates.CsiSubParam:
                    EventCsiSubParam(character);
                    break;
                case VTStates.OscParam:
                    EventOscParam(character);
                    break;
                case VTStates.OscString:
                    EventOscString(character);
                    break;
                case VTStates.OscTermination:
                    EventOscTermination(character);
                    break;
                case VTStates.Ss3Entry:
                    EventSs3Entry(character);
                    break;
                case VTStates.Ss3Param:
                    EventSs3Param(character);
                    break;
                case VTStates.Vt52Param:
                    EventVt52Param(character);
                    break;
                case VTStates.DcsEntry:
                    EventDcsEntry(character);
                    break;
                case VTStates.DcsIgnore:
                    ActionIgnore();
                    break;
                case VTStates.DcsIntermediate:
                    EventDcsIntermediate(character);
                    break;
                case VTStates.DcsParam:
                    EventDcsParam(character);
                    break;
                case VTStates.DcsPassThrough:
                    EventDcsPassThrough(character);
                    break;
                case VTStates.SosPmApcString:
                    ActionIgnore();
                    break;
            }
        }
    }

    /// <summary>
    /// Passes the sequence being processed, including what was cached of it from earlier input, to the engine as
    /// raw text, for an engine that cannot handle it itself.
    /// </summary>
    public bool FlushToTerminal()
    {
        bool success = true;
        if (_cachedSequence != null)
        {
            success = _engine.ActionPassThroughString(_cachedSequence.ToString());
            _cachedSequence = null;
        }

        if (success)
        {
            success = _engine.ActionPassThroughString(CurrentRun);
        }

        return success;
    }

    public void ProcessString(string text) => ProcessString(text.AsMemory());

    /// <summary>
    /// Prints runs of plain text in one call each and feeds everything from a control character on through the state
    /// machine until it is back in the ground state.
    /// </summary>
    public void ProcessString(ReadOnlyMemory<char> text)
    {
        var span = text.Span;
        int index = 0;
        _currentString = text;
        _runOffset = 0;
        _runSize = 0;

        bool startInSequence = _state != VTStates.Ground;
        while (index < span.Length || startInSequence)
        {
            if (!startInSequence)
            {
                _runOffset = index;
                _runSize = VTUtils.FindActionableControlCharacter(span[index..]);
                if (_runSize != 0)
                {
                    ActionPrintString(CurrentRun);
                    index += _runSize;
                    _runOffset = index;
                    _runSize = 0;
                }
            }

            startInSequence = false;
            if (index >= span.Length)
            {
                break;
            }

            do
            {
                _runSize++;
                _processingLastCharacter = index + 1 >= span.Length;
                ProcessCharacter(span[index]);
                index++;
            }
            while (index < span.Length && _state != VTStates.Ground);
        }

        // An unfinished sequence is kept, in case it has to be passed through whole later.
        if (_state != VTStates.Ground)
        {
            var run = CurrentRun;
            bool cacheUnusedRun = true;

            if (_isEngineForInput)
            {
                // A lone ESC or Alt+key at the end of input is far more likely a key than the start of a sequence.
                bool win32 = _engine.EncounteredWin32InputModeSequence();
                if (!win32 && run.Length <= 2 && run.Length > 0 && run[0] == '\u001b')
                {
                    EnterGround();
                    if (run.Length == 1)
                    {
                        ActionExecute('\u001b');
                    }
                    else
                    {
                        EnterEscape();
                        ActionEscDispatch(run[^1]);
                    }

                    EnterGround();
                    cacheUnusedRun = false;
                }
            }
            else if (_state == VTStates.SosPmApcString || _state == VTStates.DcsPassThrough || _state == VTStates.DcsIgnore)
            {
                // String states handle their data as it arrives.
                cacheUnusedRun = false;
            }

            if (cacheUnusedRun)
            {
                (_cachedSequence ??= new System.Text.StringBuilder()).Append(run);
            }
        }
    }

    private static void AccumulateTo(char character, ref int value)
    {
        int digit = character - '0';
        value = value * 10 + digit;

        // Larger values map to the largest supported one.
        if (value > MAX_PARAMETER_VALUE)
        {
            value = MAX_PARAMETER_VALUE;
        }
    }

    /// <summary>Passes the sequence through when the engine could not handle it.</summary>
    private void SafeExecute(bool ok)
    {
        if (!ok)
        {
            FlushToTerminal();
        }
    }

    private void ExecuteCsiCompleteCallback()
    {
        var callback = _onCsiCompleteCallback;
        if (callback == null)
        {
            return;
        }

        // The callback may feed another string, so the current one is saved and restored around it, and the
        // callback is taken first so it runs only once.
        var savedCurrentString = _currentString;
        int savedRunOffset = _runOffset;
        int savedRunSize = _runSize;
        _onCsiCompleteCallback = null;
        callback();
        _currentString = savedCurrentString;
        _runOffset = savedRunOffset;
        _runSize = savedRunSize;
    }
}
