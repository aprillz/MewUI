using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Diagnostics;

/// <summary>
/// Single gate for the development tools and the profiler. False unless the app opted in, and
/// always false in trimmed or NativeAOT publishes.
/// </summary>
internal static class DevToolsGate
{
    // Apps opt in via <MewUIDevTools>true</MewUIDevTools>, which emits this AppContext switch.
    private const string ENABLED_SWITCH = "Aprillz.MewUI.DevTools.Enabled";

    // Read once so the JIT folds the checks away; ILLink stubs the property instead (see
    // ILLink.Substitutions.xml), which drops the whole DevTools graph from trimmed output.
    private static readonly bool _isSupported = ReadSwitch();

    internal static bool IsSupported => _isSupported;

    private static bool ReadSwitch()
    {
        // The property panel enumerates CLR members by reflection, so a trimmed member list would
        // silently lie about what an element carries.
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            return false;
        }

        return AppContext.TryGetSwitch(ENABLED_SWITCH, out bool enabled) && enabled;
    }
}
