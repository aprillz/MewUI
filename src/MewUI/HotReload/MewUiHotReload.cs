namespace Aprillz.MewUI.HotReload;

/// <summary>
/// Minimal opt-in Hot Reload bridge for apps using C# markup.
/// Apps register a rebuild callback, and a MetadataUpdateHandler can request a UI reload.
/// </summary>
public static class MewUiHotReload
{
    private static readonly DispatcherMergeKey mergeKey = new(DispatcherPriority.Background);
    private static bool reloading;

    public static bool IsEnabled
    {
        get
        {
            return Application.IsRunning && Application.Current.Dispatcher != null;
        }
    }

    /// <summary>
    /// Requests a rebuild of the registered window's content, if enabled.
    /// </summary>
    public static void RequestReload()
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher == null)
        {
            return;
        }

        (dispatcher as IDispatcherCore)?.PostMerged(mergeKey, ReloadOnUiThread, DispatcherPriority.Input);
    }

    private static void ReloadOnUiThread()
    {
        if (reloading)
        {
            return;
        }

        if (!Application.IsRunning)
        {
            return;
        }

        reloading = true;
        try
        {
            var windows = Application.Current.AllWindows;
            for (int i = 0; i < windows.Count; i++)
            {
                ReloadWindowIfEnabled(windows[i]);
            }
        }
        finally
        {
            reloading = false;
        }
    }

    private static void ReloadWindowIfEnabled(Window window)
    {
        var build = window.BuildCallback;
        if (build == null)
        {
            return;
        }

        build(window); 
    }
}

/// <summary>
/// Runtime Hot Reload callback entrypoint. Use in the app assembly:
/// <code>
/// #if DEBUG
/// [assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
/// #endif
/// </code>
/// </summary>
public static class MewUiMetadataUpdateHandler
{
    public static void ClearCache(Type[]? updatedTypes)
    {
        // No-op for now. Apps can rebuild via UpdateApplication.
    }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        // Hot Reload is not supported on NativeAOT, but in normal debug sessions this will be invoked.
        MewUiHotReload.RequestReload();
    }
}
