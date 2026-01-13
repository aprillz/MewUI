using Aprillz.MewUI.Core;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Platform;

public interface IPlatformHost : IDisposable
{
    IMessageBoxService MessageBox { get; }

    IFileDialogService FileDialog { get; }

    IClipboardService Clipboard { get; }

    IWindowBackend CreateWindowBackend(Window window);

    IUiDispatcher CreateDispatcher(nint windowHandle);

    uint GetSystemDpi();

    uint GetDpiForWindow(nint hwnd);

    bool EnablePerMonitorDpiAwareness();

    int GetSystemMetricsForDpi(int nIndex, uint dpi);

    void Run(Application app, Window mainWindow);

    void Quit(Application app);

    void DoEvents();
}
