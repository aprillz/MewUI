namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies how WebView2 host resources are exposed to virtual host name mappings.
/// </summary>
public enum CoreWebView2HostResourceAccessKind
{
    /// <summary>
    /// Denies access to the mapped folder.
    /// </summary>
    Deny = 0,

    /// <summary>
    /// Allows access to the mapped folder.
    /// </summary>
    Allow = 1,

    /// <summary>
    /// Allows access to the mapped folder but blocks cross-origin access.
    /// </summary>
    DenyCors = 2,
}

