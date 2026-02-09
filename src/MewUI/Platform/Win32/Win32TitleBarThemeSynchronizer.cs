namespace Aprillz.MewUI.Platform.Win32;

/// <summary>
/// Manages synchronization of the Win32 window title bar appearance with application themes.
/// </summary>
internal sealed class Win32TitleBarThemeSynchronizer : IDisposable
{
    private nint _hWnd;
    private bool _isDark;

    /// <summary>
    /// Initializes the synchronizer and begins tracking theme changes for the specified window.
    /// </summary>
    /// <param name="hWnd">The window handle to synchronize.</param>
    public void Initialize(nint hWnd)
    {
        if (hWnd == 0)
        {
            throw new ArgumentException("Invalid window handle.", nameof(hWnd));
        }

        if (!Win32ImmersiveThemeManager.IsImmersiveThemeSupportedByVersion())
        {
            return;
        }

        _hWnd = hWnd;
        ApplyTheme(_isDark);
    }


    /// <summary>
    /// Synchronizes the window's title bar appearance with the specified theme mode.
    /// </summary>
    /// <param name="isDark"><see langword="true"/> to use the dark title bar theme; otherwise, <see langword="false"/>.</param>
    public void ApplyTheme(bool isDark)
    {
        _isDark = isDark;

        if (_hWnd == 0)
        {
            return;
        }

        Win32ImmersiveThemeManager.SetWindowImmersiveTheme(_hWnd, isDark);
    }

    public void Dispose()
    {
        _hWnd = 0;
    }
}
