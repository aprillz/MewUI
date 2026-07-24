// Injected into app compilations only during editor preview sessions (see Aprillz.MewUI.targets).
// Runs before Main, so PreviewBootstrap can intercept the platform host resolution.
namespace Aprillz.MewUI.Preview.Generated;

internal static class MewUIPreviewInit
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize() => PreviewBootstrap.TryRegister();
}
