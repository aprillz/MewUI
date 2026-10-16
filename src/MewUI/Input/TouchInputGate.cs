namespace Aprillz.MewUI.Input;

/// <summary>
/// Single gate for the touch gesture layer. True unless the app turned touch off, in which case
/// touch and pen arrive the way the platform promotes them to the mouse.
/// </summary>
internal static class TouchInputGate
{
    // Apps opt out via <MewUITouch>false</MewUITouch>, which emits this AppContext switch.
    private const string ENABLED_SWITCH = "Aprillz.MewUI.Touch.Enabled";

    // ILLink stubs the property when the switch is false (see ILLink.Substitutions.xml), which drops
    // the recognizer and the coast from trimmed output.
    private static readonly bool _isSupported =
        !AppContext.TryGetSwitch(ENABLED_SWITCH, out bool enabled) || enabled;

    internal static bool IsSupported => _isSupported;
}
