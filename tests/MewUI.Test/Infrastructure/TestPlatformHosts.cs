using Aprillz.MewUI;
using Aprillz.MewUI.Platform;

namespace MewUI.Test.Infrastructure;

/// <summary>
/// Process-wide platform host registration shared by tests that drive Application.Run.
/// Application allows a single host registration per process, so every such test enqueues
/// its host here instead of registering its own factory.
/// </summary>
internal static class TestPlatformHosts
{
    public static readonly Queue<IPlatformHost> Queue = new();

    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        Application.RegisterPlatformHost(static () => Queue.Dequeue(), PlatformSurfaceKind.Win32, "Test",
            "Arial");
        _registered = true;
    }
}
