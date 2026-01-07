using System.Runtime.InteropServices;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Core;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Platform.Win32;

public sealed class Win32PlatformHost : IPlatformHost
{
    internal const string WindowClassName = "AprillzMewUIWindow";

    private readonly Dictionary<nint, Win32WindowBackend> _windows = new();
    private readonly IMessageBoxService _messageBox = new Win32MessageBoxService();
    private WndProc? _wndProcDelegate;
    private bool _running;
    private ushort _classAtom;
    private nint _classNamePtr;
    private nint _moduleHandle;
    private SynchronizationContext? _previousSynchronizationContext;

    public IMessageBoxService MessageBox => _messageBox;

    public IWindowBackend CreateWindowBackend(Window window) => new Win32WindowBackend(this, window);

    public IUiDispatcher CreateDispatcher(nint windowHandle) => new Win32UiDispatcher(windowHandle);

    internal void RegisterWindow(nint hwnd, Win32WindowBackend backend) => _windows[hwnd] = backend;

    internal void UnregisterWindow(nint hwnd)
    {
        _windows.Remove(hwnd);
        if (_windows.Count == 0)
        {
            _running = false;
            User32.PostQuitMessage(0);
        }
    }

    public void Run(Application app, Window mainWindow)
    {
        try
        {
            DpiHelper.EnablePerMonitorDpiAwareness();
            RegisterWindowClass();

            _running = true;

            mainWindow.Show();

            _previousSynchronizationContext = SynchronizationContext.Current;
            var dispatcher = CreateDispatcher(mainWindow.Handle);
            app.Dispatcher = dispatcher;
            SynchronizationContext.SetSynchronizationContext(dispatcher as SynchronizationContext);

            MSG msg;
            while (_running && User32.GetMessage(out msg, 0, 0, 0) > 0)
            {
                try
                {
                    User32.TranslateMessage(ref msg);
                    User32.DispatchMessage(ref msg);
                }
                catch (Exception ex)
                {
                    if (Application.TryHandleUiException(ex))
                        continue;

                    Application.NotifyFatalUiException(ex);
                    _running = false;
                    User32.PostQuitMessage(0);
                    break;
                }
            }
        }
        finally
        {
            Shutdown(app);
        }
    }

    public void Quit(Application app)
    {
        _running = false;
        User32.PostQuitMessage(0);
    }

    public void DoEvents()
    {
        MSG msg;
        while (User32.PeekMessage(out msg, 0, 0, 0, 1)) // PM_REMOVE = 1
        {
            try
            {
                User32.TranslateMessage(ref msg);
                User32.DispatchMessage(ref msg);
            }
            catch (Exception ex)
            {
                if (Application.TryHandleUiException(ex))
                    continue;

                Application.NotifyFatalUiException(ex);
                _running = false;
                User32.PostQuitMessage(0);
                break;
            }
        }
    }

    private void RegisterWindowClass()
    {
        _wndProcDelegate = WndProc;
        var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

        _moduleHandle = Kernel32.GetModuleHandle(null);
        _classNamePtr = Marshal.StringToHGlobalUni(WindowClassName);

        var wndClass = WNDCLASSEX.Create();
        // CS_OWNDC is important for stable OpenGL (WGL) contexts; it is harmless for other backends.
        wndClass.style = ClassStyles.CS_HREDRAW | ClassStyles.CS_VREDRAW | ClassStyles.CS_DBLCLKS | ClassStyles.CS_OWNDC;
        wndClass.lpfnWndProc = wndProcPtr;
        wndClass.cbClsExtra = 0;
        wndClass.cbWndExtra = 0;
        wndClass.hInstance = _moduleHandle;
        wndClass.hIcon = 0;
        wndClass.hCursor = User32.LoadCursor(0, SystemCursors.IDC_ARROW);
        wndClass.hbrBackground = 0;
        wndClass.lpszMenuName = 0;
        wndClass.lpszClassName = _classNamePtr;
        wndClass.hIconSm = 0;

        _classAtom = User32.RegisterClassEx(ref wndClass);
        if (_classAtom == 0)
        {
            Marshal.FreeHGlobal(_classNamePtr);
            _classNamePtr = 0;
            throw new InvalidOperationException($"Failed to register window class. Error: {Marshal.GetLastWin32Error()}");
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (_windows.TryGetValue(hWnd, out var backend))
        {
            try
            {
                return backend.ProcessMessage(msg, wParam, lParam);
            }
            catch (Exception ex)
            {
                if (Application.TryHandleUiException(ex))
                    return 0;

                Application.NotifyFatalUiException(ex);
                _running = false;
                User32.PostQuitMessage(0);
                return 0;
            }
        }

        return User32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void Shutdown(Application app)
    {
        SynchronizationContext.SetSynchronizationContext(_previousSynchronizationContext);
        app.Dispatcher = null;

        foreach (var backend in _windows.Values.ToArray())
        {
            backend.Window.Close();
        }
        _windows.Clear();

        if (_classAtom != 0)
        {
            User32.UnregisterClass(WindowClassName, _moduleHandle);
            _classAtom = 0;
        }

        if (_classNamePtr != 0)
        {
            Marshal.FreeHGlobal(_classNamePtr);
            _classNamePtr = 0;
        }
    }

    public void Dispose() { }
}
