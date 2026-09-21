namespace Aprillz.MewUI.Rendering.Win32;

/// <summary>
/// Single gate for the DirectWrite text path on Win32. False unless the app selected it with
/// <c>&lt;MewUIWin32TextEngine&gt;DirectWrite&lt;/MewUIWin32TextEngine&gt;</c>.
/// </summary>
internal static class DirectWriteTextGate
{
    private const string ENABLED_SWITCH = "Aprillz.MewUI.Win32.DirectWriteText.Enabled";

    // Read once so the JIT folds the checks away; ILLink stubs the property instead (see each
    // backend's ILLink.Substitutions.xml), which drops whichever text path the app did not select.
    private static readonly bool _isSupported =
        AppContext.TryGetSwitch(ENABLED_SWITCH, out bool enabled) && enabled;

    internal static bool IsSupported => _isSupported;
}
