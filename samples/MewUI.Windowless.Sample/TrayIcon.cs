using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Windowless.Sample;

/// <summary>
/// An icon in the notification area (Windows) or the menu bar (macOS), with a MewUI <see cref="ContextMenu"/> as its
/// menu. Each platform shows the menu as its own native menu, read from <see cref="ContextMenu"/> each time it opens.
/// The items' commands run through <see cref="Application.CommandRouter"/>, so their handlers are registered in
/// <see cref="Application.Commands"/> and work while no window is open.
/// </summary>
internal sealed class TrayIcon(Action<Action> post) : IDisposable
{
    private ITrayIconBackend? _backend;

    /// <summary>The icon, as the bytes of an .ico file.</summary>
    public byte[] IconFile { get; set; } = [];

    public string ToolTip { get; set; } = string.Empty;

    public ContextMenu? ContextMenu { get; set; }

    /// <summary>Raised on a click on the icon where the platform reports one (Windows); macOS opens the menu instead.</summary>
    public event Action? Click;

    /// <summary>The platform implementation in use, or null before <see cref="Show"/> or where there is none.</summary>
    public string? BackendName => _backend?.Name;

    /// <summary>Adds the icon; false where the platform has no tray icon (Linux). Call it on the UI thread.</summary>
    public bool Show()
    {
        if (_backend != null)
        {
            return true;
        }

        var backend = TrayIconFactory.Create();
        if (backend == null)
        {
            return false;
        }

        try
        {
            backend.Start(this);
        }
        catch
        {
            backend.Dispose();
            throw;
        }

        _backend = backend;
        return true;
    }

    /// <summary>
    /// Whether a menu item can be chosen now, the way the MewUI context menu decides it: the item is enabled, its
    /// <see cref="MenuItem.CanClick"/> allows it, and its command can execute with the item's data.
    /// </summary>
    internal bool CanInvoke(MenuItem item)
    {
        if (!item.IsEnabled || !(item.CanClick?.Invoke() ?? true))
        {
            return false;
        }

        return item.Command == null || Application.Current.CommandRouter.CanExecute(item.Command, default, item.CommandData);
    }

    /// <summary>Runs the item's command on the next dispatcher turn, after the native menu has returned.</summary>
    internal void Invoke(MenuItem item)
    {
        var command = item.Command;
        if (command != null && CanInvoke(item))
        {
            object? data = item.CommandData;
            // async void: a faulted handler surfaces through the dispatcher's exception policy.
            post(async () => await Application.Current.CommandRouter.ExecuteAsync(command, default, data, source: null));
        }
    }

    /// <summary>Called by the platform implementation; runs on the next dispatcher turn, like the menu commands.</summary>
    internal void RaiseClick()
    {
        var click = Click;
        if (click != null)
        {
            post(click);
        }
    }

    public void Dispose()
    {
        _backend?.Dispose();
        _backend = null;
    }
}

internal static class TrayIconExtensions
{
    public static TrayIcon Icon(this TrayIcon trayIcon, byte[] iconFile)
    {
        trayIcon.IconFile = iconFile;
        return trayIcon;
    }

    public static TrayIcon ToolTip(this TrayIcon trayIcon, string toolTip)
    {
        trayIcon.ToolTip = toolTip;
        return trayIcon;
    }

    /// <summary>Sets the menu, the same way <c>ContextMenu(...)</c> sets an element's context menu.</summary>
    public static TrayIcon ContextMenu(this TrayIcon trayIcon, ContextMenu? menu)
    {
        trayIcon.ContextMenu = menu;
        return trayIcon;
    }

    public static TrayIcon OnClick(this TrayIcon trayIcon, Action handler)
    {
        trayIcon.Click += handler;
        return trayIcon;
    }
}
