using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Core;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Platform.Linux;

/// <summary>
/// Experimental Linux platform host.
/// This currently provides the scaffolding required for future X11/Wayland backends.
/// </summary>
public sealed class LinuxPlatformHost : IPlatformHost
{
    private readonly IMessageBoxService _messageBox = new LinuxMessageBoxService();

    public IMessageBoxService MessageBox => _messageBox;

    public IWindowBackend CreateWindowBackend(Window window) => new LinuxWindowBackend(window);

    public IUiDispatcher CreateDispatcher(nint windowHandle) => new LinuxUiDispatcher();

    public void Run(Application app, Window mainWindow)
        => throw new PlatformNotSupportedException("Linux platform host is not implemented yet. (X11/Wayland + rendering backend work pending)");

    public void Quit(Application app) { }

    public void DoEvents() { }

    public void Dispose() { }
}

