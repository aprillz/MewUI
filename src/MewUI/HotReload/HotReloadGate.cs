using System.Reflection.Metadata;

namespace Aprillz.MewUI.HotReload;

/// <summary>
/// Single gate for the Hot Reload path: active only when the runtime supports metadata updates
/// (JIT/debug) and the app has not opted out. False (and trim-constant) in release/AOT.
/// </summary>
internal static class HotReloadGate
{
    // Apps disable via <MewUIHotReload>false</MewUIHotReload>, which emits this AppContext switch
    // (the assembly attribute is baked into MewUI.dll and cannot be removed by MSBuild).
    private const string ENABLED_SWITCH = "Aprillz.MewUI.HotReload.Enabled";

    public static bool IsActive
    {
        get
        {
            if (!MetadataUpdater.IsSupported)
            {
                return false;
            }

            return !(AppContext.TryGetSwitch(ENABLED_SWITCH, out bool enabled) && !enabled);
        }
    }
}
