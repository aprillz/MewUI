namespace Aprillz.MewUI.Preview;

/// <summary>
/// Composition entry point for preview sessions. Called before <c>Application.Run</c> by the
/// build-injected module initializer (the MewUI package targets inject it only when the IDE
/// extension sets the preview environment); a no-op outside preview sessions.
/// </summary>
public static class PreviewBootstrap
{
    private static int _registered;

    public static void TryRegister()
    {
        if (!PreviewEnvironment.IsActive || Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        Design.IsPreviewMode = true;
        Application.PlatformHostInterceptor = host => new PreviewPlatformHost(host);
    }
}
