namespace Aprillz.MewUI.Automation;

/// <summary>Single gate for the automation peer tree and everything built on it. False unless the app turned automation on.</summary>
internal static class AutomationGate
{
    // Apps opt in via <MewUIAutomation>true</MewUIAutomation>, which emits this AppContext switch.
    private const string ENABLED_SWITCH = "Aprillz.MewUI.Automation.Enabled";

    // ILLink stubs the property when the switch is false (see ILLink.Substitutions.xml), which drops the peers from
    // trimmed output.
    private static readonly bool _isSupported =
        AppContext.TryGetSwitch(ENABLED_SWITCH, out bool enabled) && enabled;

    internal static bool IsSupported => _isSupported;
}
