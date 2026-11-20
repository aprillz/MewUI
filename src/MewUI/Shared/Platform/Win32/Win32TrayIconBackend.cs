using System.ComponentModel;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Platform.Win32;

/// <summary>
/// A notification-area icon. Its callback window is a hidden top-level window created on the UI thread, so the
/// message loop delivers the icon's notifications there. The menu opens as the icon's <see cref="ContextMenu"/>,
/// owned by a menu host window that takes the foreground while the menu is open.
/// </summary>
internal sealed unsafe partial class Win32TrayIconBackend : ITrayIconBackend
{
    private const string WINDOW_CLASS = "AprillzMewUITrayIcon";
    private const uint ICON_ID = 1;
    private const uint WM_TRAY_CALLBACK = 0x8000 + 0x0101;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_DPICHANGED = 0x02E0;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const int SM_CXSMICON = 49;
    private const int GWLP_USERDATA = -21;
    private const int TIP_CAPACITY = 128;
    private const uint MSGFLT_ALLOW = 1;
    private const int IDI_APPLICATION = 32512;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;

    // Registered once per process; tray icons of later runs reuse it.
    private static bool _windowClassRegistered;

    private readonly TrayIcon _trayIcon;
    private readonly uint _taskbarCreatedMessage;
    private GCHandle _self;
    private nint _window;
    private nint _icon;
    private bool _ownsIcon;
    private Window? _menuHost;
    private TrayMenuAnchor? _menuAnchor;
    private bool _disposed;

    public Win32TrayIconBackend(TrayIcon trayIcon)
    {
        _trayIcon = trayIcon;
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

        nint instance = Kernel32.GetModuleHandle(null);
        EnsureWindowClass(instance);

        // A hidden top-level window rather than a message-only one: only top-level windows receive the
        // TaskbarCreated broadcast that follows an Explorer restart.
        _window = User32.CreateWindowEx(0, WINDOW_CLASS, WINDOW_CLASS, 0, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_window == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _self = GCHandle.Alloc(this);
        User32.SetWindowLongPtr(_window, GWLP_USERDATA, GCHandle.ToIntPtr(_self));
        if (_taskbarCreatedMessage != 0)
        {
            // An elevated process would otherwise filter the broadcast out.
            _ = ChangeWindowMessageFilterEx(_window, _taskbarCreatedMessage, MSGFLT_ALLOW, 0);
        }

        try
        {
            RefreshIcon();
            if (!AddIcon())
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The notification area did not accept the icon.");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        RefreshIcon();
        var data = CreateIconData(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _ = Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_menuHost != null)
        {
            // Closing popups first: closing the host with a menu open would dispose the application's menu.
            _menuHost.CloseAllPopups();
            _menuHost.Close();
            _menuHost = null;
            _menuAnchor = null;
        }

        if (_window != 0)
        {
            var data = CreateIconData(0);
            _ = Shell_NotifyIconW(NIM_DELETE, &data);
            User32.SetWindowLongPtr(_window, GWLP_USERDATA, 0);
            _ = User32.DestroyWindow(_window);
            _window = 0;
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }

        ReleaseIcon();
    }

    private bool AddIcon()
    {
        var data = CreateIconData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (!Shell_NotifyIconW(NIM_ADD, &data))
        {
            return false;
        }

        // Version 4 reports a click as NIN_SELECT, a right click or Shift+F10 as WM_CONTEXTMENU, with the anchor point.
        data.Version = NOTIFYICON_VERSION_4;
        return Shell_NotifyIconW(NIM_SETVERSION, &data);
    }

    private NOTIFYICONDATAW CreateIconData(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            Size = (uint)sizeof(NOTIFYICONDATAW),
            Window = _window,
            Id = ICON_ID,
            Flags = flags,
            CallbackMessage = WM_TRAY_CALLBACK,
            Icon = _icon,
        };

        string toolTip = _trayIcon.ToolTipText;
        var tip = new Span<char>(data.Tip, TIP_CAPACITY);
        toolTip.AsSpan(0, Math.Min(toolTip.Length, TIP_CAPACITY - 1)).CopyTo(tip);
        return data;
    }

    private void RefreshIcon()
    {
        ReleaseIcon();

        uint dpi = User32.GetDpiForWindow(_window);
        int size = User32.GetSystemMetricsForDpi(SM_CXSMICON, dpi == 0 ? 96 : dpi);
        if (_trayIcon.Icon is IconSource source)
        {
            _icon = Win32WindowBackend.TryCreateIcon(source, size);
            _ownsIcon = _icon != 0;
        }

        if (_icon == 0)
        {
            nint extracted = 0;
            string? processPath = Environment.ProcessPath;
            if (processPath != null && SHDefExtractIconW(processPath, 0, 0, &extracted, null, (uint)size) == 0 && extracted != 0)
            {
                _icon = extracted;
                _ownsIcon = true;
            }
            else
            {
                _icon = User32.LoadIcon(0, IDI_APPLICATION);
                _ownsIcon = false;
            }
        }
    }

    private void ReleaseIcon()
    {
        if (_icon != 0 && _ownsIcon)
        {
            _ = User32.DestroyIcon(_icon);
        }

        _icon = 0;
        _ownsIcon = false;
    }

    private bool HandleMessage(uint message, nint wParam, nint lParam)
    {
        if (message == WM_TRAY_CALLBACK)
        {
            // Version 4 passes the anchor point in screen pixels in wParam.
            int anchorX = (short)(wParam & 0xFFFF);
            int anchorY = (short)((wParam >> 16) & 0xFFFF);
            var anchor = new Point(anchorX, anchorY);
            switch ((uint)(lParam & 0xFFFF))
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    _trayIcon.RaiseClick(anchor, MouseButton.Left);
                    return true;

                case WM_LBUTTONDBLCLK:
                    _trayIcon.RaiseDoubleClick(anchor, MouseButton.Left);
                    return true;

                case WM_CONTEXTMENU:
                    OpenMenu(anchorX, anchorY);
                    return true;

                default:
                    return false;
            }
        }

        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            // Explorer restarted and dropped every notification-area icon.
            RefreshIcon();
            _ = AddIcon();
            return true;
        }

        if (message is WM_DISPLAYCHANGE or WM_DPICHANGED)
        {
            Update();
        }

        return false;
    }

    private void OpenMenu(int anchorX, int anchorY)
    {
        var menu = _trayIcon.PrepareMenu();
        if (_disposed || menu == null || menu.Items.Count == 0)
        {
            return;
        }

        var host = EnsureMenuHost();
        host.CloseAllPopups();
        host.Show();
        host.MoveToScreenPixels(anchorX, anchorY);

        // Without the foreground the menu would stay open after a click outside it.
        host.Activate();

        menu.SetCommandTarget(_trayIcon.CommandTarget);
        menu.SetCommandRouter(Application.Current.CommandRouter);
        menu.Show(_menuAnchor!, new Point(0, 0));
    }

    private Window EnsureMenuHost()
    {
        if (_menuHost == null)
        {
            _menuAnchor = new TrayMenuAnchor(OnMenuClosed);
            _menuHost = new Window
            {
                Kind = WindowKind.MenuHost,
                Title = WINDOW_CLASS,
                ShowInTaskbar = false,
                Borderless = true,
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(1, 1),
                Content = _menuAnchor,
            };
        }

        return _menuHost;
    }

    private void OnMenuClosed()
    {
        // Hidden on a later turn: the close sweep that reported it is still running, and a click on the icon may
        // reopen a menu before it ends.
        Application.Current.Dispatcher?.BeginInvoke(() =>
        {
            if (!_disposed && _menuHost != null && _menuHost.OpenPopupCount == 0)
            {
                _menuHost.Hide();
            }
        });
    }

    private static void EnsureWindowClass(nint instance)
    {
        if (_windowClassRegistered)
        {
            return;
        }

        fixed (char* className = WINDOW_CLASS)
        {
            var windowClass = WNDCLASSEX.Create();
            windowClass.lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WindowProc;
            windowClass.hInstance = instance;
            windowClass.lpszClassName = (nint)className;
            if (User32.RegisterClassEx(ref windowClass) == 0 && Marshal.GetLastPInvokeError() != ERROR_CLASS_ALREADY_EXISTS)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        _windowClassRegistered = true;
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        nint self = User32.GetWindowLongPtr(window, GWLP_USERDATA);
        if (self != 0 && GCHandle.FromIntPtr(self).Target is Win32TrayIconBackend backend)
        {
            try
            {
                if (backend.HandleMessage(message, wParam, lParam))
                {
                    return 0;
                }
            }
            catch (Exception ex)
            {
                // An exception must not unwind into the window manager's frames.
                Application.RouteLifecycleException(ex);
                return 0;
            }
        }

        return User32.DefWindowProc(window, message, wParam, lParam);
    }

    /// <summary>The menu's placement target in the menu host; it learns when the menu closes.</summary>
    private sealed class TrayMenuAnchor(Action menuClosed) : FrameworkElement, IPopupOwner
    {
        public void OnPopupClosed(UIElement popup, PopupCloseKind kind) => menuClosed();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATAW
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[TIP_CAPACITY];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint Version;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHDefExtractIconW(string iconFile, int index, uint flags, nint* largeIcon, nint* smallIcon, uint iconSize);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeWindowMessageFilterEx(nint window, uint message, uint action, nint changeFilterStruct);
}
