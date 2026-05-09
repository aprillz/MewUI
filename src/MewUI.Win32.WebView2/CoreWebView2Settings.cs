namespace Aprillz.MewUI.Controls;

/// <summary>
/// Defines the settings for a CoreWebView2.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CoreWebView2Settings : IDisposable
{
    private ComObject<ICoreWebView2Settings>? _settingsObj;
    private bool _disposed;

    internal CoreWebView2Settings(ICoreWebView2Settings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        _settingsObj = new ComObject<ICoreWebView2Settings>(settings);
    }

    private ICoreWebView2Settings Settings
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed || _settingsObj == null, this);
            return _settingsObj.Object;
        }
    }

    /// <summary>
    /// Releases all resources used by the CoreWebView2Settings.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _settingsObj?.Dispose();
        _settingsObj = null;
    }

    /// <summary>
    /// Gets or sets whether JavaScript execution is enabled.
    /// </summary>
    public bool IsScriptEnabled
    {
        get
        {
            BOOL value = default;
            Settings.get_IsScriptEnabled(ref value);
            return value != 0;
        }
        set => Settings.put_IsScriptEnabled(value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets whether web messaging is enabled.
    /// </summary>
    public bool IsWebMessageEnabled
    {
        get
        {
            BOOL value = default;
            Settings.get_IsWebMessageEnabled(ref value);
            return value != 0;
        }
        set => Settings.put_IsWebMessageEnabled(value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets whether default script dialogs (alert, confirm, prompt) are enabled.
    /// </summary>
    public bool AreDefaultScriptDialogsEnabled
    {
        get
        {
            BOOL value = default;
            Settings.get_AreDefaultScriptDialogsEnabled(ref value);
            return value != 0;
        }
        set => Settings.put_AreDefaultScriptDialogsEnabled(value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets whether the status bar is enabled.
    /// </summary>
    public bool IsStatusBarEnabled
    {
        get
        {
            BOOL value = default;
            Settings.get_IsStatusBarEnabled(ref value);
            return value != 0;
        }
        set => Settings.put_IsStatusBarEnabled(value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets whether DevTools are enabled.
    /// </summary>
    public bool AreDevToolsEnabled
    {
        get
        {
            BOOL value = default;
            Settings.get_AreDevToolsEnabled(ref value);
            return value != 0;
        }
        set => Settings.put_AreDevToolsEnabled(value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets whether the default context menus are enabled.
    /// </summary>
    public bool AreDefaultContextMenusEnabled
    {
        get
        {
            BOOL value = default;
            Settings.get_AreDefaultContextMenusEnabled(ref value);
            return value != 0;
        }
        set => Settings.put_AreDefaultContextMenusEnabled(value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets whether host objects are allowed.
    /// </summary>
    public bool AreHostObjectsAllowed
    {
        get
        {
            BOOL value = default;
            Settings.get_AreHostObjectsAllowed(ref value);
            return value != 0;
        }
        set => Settings.put_AreHostObjectsAllowed(value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets whether zoom control is enabled.
    /// </summary>
    public bool IsZoomControlEnabled
    {
        get
        {
            BOOL value = default;
            Settings.get_IsZoomControlEnabled(ref value);
            return value != 0;
        }
        set => Settings.put_IsZoomControlEnabled(value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets whether the built-in error page is enabled.
    /// </summary>
    public bool IsBuiltInErrorPageEnabled
    {
        get
        {
            BOOL value = default;
            Settings.get_IsBuiltInErrorPageEnabled(ref value);
            return value != 0;
        }
        set => Settings.put_IsBuiltInErrorPageEnabled(value ? 1 : 0);
    }

    /// <summary>
    /// Gets or sets the user agent string.
    /// </summary>
    public string? UserAgent
    {
        get
        {
            if (Settings is not ICoreWebView2Settings2 settings2)
                return null;

            settings2.get_UserAgent(out var userAgent);
            try
            {
                return userAgent.ToString();
            }
            finally
            {
                Marshal.FreeCoTaskMem(userAgent.Value);
            }
        }
        set
        {
            if (Settings is not ICoreWebView2Settings2 settings2)
                return;

            settings2.put_UserAgent(PWSTR.From(value));
        }
    }

    /// <summary>
    /// Gets or sets whether browser accelerator keys are enabled.
    /// </summary>
    public bool AreBrowserAcceleratorKeysEnabled
    {
        get
        {
            if (Settings is not ICoreWebView2Settings3 settings3)
                return true;

            BOOL value = default;
            settings3.get_AreBrowserAcceleratorKeysEnabled(ref value);
            return value != 0;
        }
        set
        {
            if (Settings is not ICoreWebView2Settings3 settings3)
                return;

            settings3.put_AreBrowserAcceleratorKeysEnabled(value ? 1 : 0);
        }
    }
}
