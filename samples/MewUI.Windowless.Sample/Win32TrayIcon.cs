using System.ComponentModel;
using System.Runtime.InteropServices;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Windowless.Sample;

/// <summary>
/// A notification-area icon through <c>Shell_NotifyIconW</c>. Its hidden window is created on the UI thread, so MewUI's
/// message loop delivers the icon's messages and the callbacks run on the UI thread. The right-click menu is a Win32
/// popup menu built from the owner's <see cref="TrayIcon.ContextMenu"/> each time it opens.
/// </summary>
internal sealed unsafe class Win32TrayIcon : ITrayIconBackend
{
    private const string WINDOW_CLASS = "MewUIWindowlessTray";
    private const uint ICON_ID = 1;
    private const uint WM_NULL = 0x0000;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_TRAY = 0x8002;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;
    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint MF_STRING = 0x0000;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_POPUP = 0x0010;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;
    private const int SM_CXSMICON = 49;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const int TIP_CAPACITY = 128;

    // The window procedure is a static entry point; the sample has one tray icon at a time.
    private static Win32TrayIcon? _current;

    private nint _instance;
    private nint _window;
    private nint _icon;
    private uint _taskbarCreatedMessage;
    private TrayIcon? _owner;

    public string Name => "Win32 Shell_NotifyIcon";

    public void Start(TrayIcon owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_window != 0)
        {
            throw new InvalidOperationException("The tray icon is already shown.");
        }

        _owner = owner;
        _current = this;
        _instance = GetModuleHandleW(0);
        try
        {
            fixed (char* className = WINDOW_CLASS)
            {
                var windowClass = new WNDCLASSEXW
                {
                    Size = (uint)sizeof(WNDCLASSEXW),
                    WindowProc = &WindowProc,
                    Instance = _instance,
                    ClassName = className,
                };
                if (RegisterClassExW(&windowClass) == 0 && Marshal.GetLastPInvokeError() != ERROR_CLASS_ALREADY_EXISTS)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                // A hidden top-level window rather than a message-only one: only top-level windows receive the
                // TaskbarCreated broadcast and can take the foreground that dismisses the menu correctly.
                _window = CreateWindowExW(0, className, className, 0, 0, 0, 0, 0, 0, 0, _instance, 0);
            }
            if (_window == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            int iconSize = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
            _icon = CreateIcon(owner.IconFile, iconSize);
            _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
            if (!AddIcon())
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Shell_NotifyIconW(NIM_ADD) failed.");
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        // The shell reports where it placed the icon, which confirms it is really in the notification area.
        var identifier = new NOTIFYICONIDENTIFIER { Size = (uint)sizeof(NOTIFYICONIDENTIFIER), Window = _window, Id = ICON_ID };
        RECT bounds;
        int result = Shell_NotifyIconGetRect(&identifier, &bounds);
        Console.Error.WriteLine(result == 0
            ? $"[windowless] tray icon placed at {bounds.Left},{bounds.Top} {bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top}"
            : $"[windowless] tray icon position unknown (0x{result:X8})");
    }

    private bool AddIcon()
    {
        var data = CreateIconData();
        data.Flags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.CallbackMessage = WM_TRAY;
        data.Icon = _icon;
        string toolTip = _owner?.ToolTip ?? string.Empty;
        var tip = new Span<char>(data.Tip, TIP_CAPACITY);
        toolTip.AsSpan(0, Math.Min(toolTip.Length, TIP_CAPACITY - 1)).CopyTo(tip);
        if (!Shell_NotifyIconW(NIM_ADD, &data))
        {
            return false;
        }

        // Version 4 reports a click as NIN_SELECT and a right click or Shift+F10 as WM_CONTEXTMENU.
        data.Version = NOTIFYICON_VERSION_4;
        return Shell_NotifyIconW(NIM_SETVERSION, &data);
    }

    private NOTIFYICONDATAW CreateIconData() => new()
    {
        Size = (uint)sizeof(NOTIFYICONDATAW),
        Window = _window,
        Id = ICON_ID,
    };

    private bool HandleMessage(uint message, nint wParam, nint lParam)
    {
        if (message == WM_TRAY)
        {
            uint notification = (uint)(lParam & 0xFFFF);
            if (notification == NIN_SELECT || notification == NIN_KEYSELECT)
            {
                _owner?.RaiseClick();
                return true;
            }

            if (notification == WM_CONTEXTMENU)
            {
                // Version 4 passes the anchor point in wParam, so a keyboard-opened menu appears at the icon.
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                return true;
            }

            return false;
        }

        if (message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            // Explorer restarted and dropped every notification icon.
            AddIcon();
            return true;
        }

        return false;
    }

    private void ShowMenu(int x, int y)
    {
        var model = _owner?.ContextMenu;
        if (_owner == null || model == null)
        {
            return;
        }

        nint menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        var items = new List<MenuItem>();
        int chosen;
        try
        {
            AppendEntries(menu, model.Items, _owner, items);

            // Without the foreground the menu stays open after a click elsewhere; the WM_NULL lets it close.
            SetForegroundWindow(_window);
            chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, x, y, _window, 0);
            PostMessageW(_window, WM_NULL, 0, 0);
        }
        finally
        {
            // Destroying the root menu destroys the submenus attached to it.
            DestroyMenu(menu);
        }

        if (chosen > 0 && chosen <= items.Count)
        {
            _owner.Invoke(items[chosen - 1]);
        }
    }

    /// <summary>
    /// Appends the entries to a Win32 menu. A chosen item comes back as its position in <paramref name="items"/>
    /// plus one, since zero means nothing was chosen.
    /// </summary>
    private static void AppendEntries(nint menu, IList<MenuEntry> entries, TrayIcon owner, List<MenuItem> items)
    {
        foreach (var entry in entries)
        {
            if (entry is not MenuItem item)
            {
                AppendMenuW(menu, MF_SEPARATOR, 0, null);
                continue;
            }

            string text = TrayMenuText.ForWin32(item);
            if (item.SubMenu != null)
            {
                nint subMenu = CreatePopupMenu();
                AppendEntries(subMenu, item.SubMenu.Items, owner, items);
                AppendMenuW(menu, MF_POPUP | (item.IsEnabled ? 0 : MF_GRAYED), (nuint)subMenu, text);
                continue;
            }

            items.Add(item);
            uint flags = MF_STRING;
            if (!owner.CanInvoke(item))
            {
                flags |= MF_GRAYED;
            }
            if (item.IsCheckable && item.IsChecked)
            {
                flags |= MF_CHECKED;
            }
            AppendMenuW(menu, flags, (nuint)items.Count, text);
        }
    }

    /// <summary>Creates an icon from the image in an .ico file that best fits <paramref name="size"/> pixels.</summary>
    private static nint CreateIcon(byte[] iconFile, int size)
    {
        int count = iconFile[4] | (iconFile[5] << 8);
        int bestEntry = -1;
        int bestScore = int.MaxValue;
        for (int entry = 0; entry < count; entry++)
        {
            int width = iconFile[6 + (entry * 16)];
            if (width == 0)
            {
                width = 256;
            }

            // Prefer the smallest image at least as large as asked for; scaling down looks better than up.
            int score = width >= size ? width - size : 1000 + (size - width);
            if (score < bestScore)
            {
                bestScore = score;
                bestEntry = entry;
            }
        }
        if (bestEntry < 0)
        {
            throw new InvalidDataException("The icon file has no images.");
        }

        int header = 6 + (bestEntry * 16);
        int length = BitConverter.ToInt32(iconFile, header + 8);
        int offset = BitConverter.ToInt32(iconFile, header + 12);
        fixed (byte* image = iconFile)
        {
            nint icon = CreateIconFromResourceEx(image + offset, (uint)length, true, 0x00030000, size, size, 0);
            if (icon == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return icon;
        }
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        var current = _current;
        if (current != null && current.HandleMessage(message, wParam, lParam))
        {
            return 0;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_window != 0)
        {
            var data = CreateIconData();
            Shell_NotifyIconW(NIM_DELETE, &data);
            DestroyWindow(_window);
            _window = 0;
        }
        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
        if (_instance != 0)
        {
            UnregisterClassW(WINDOW_CLASS, _instance);
            _instance = 0;
        }
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
        _owner = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid GuidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("kernel32")]
    private static extern nint GetModuleHandleW(nint moduleName);

    [DllImport("user32", SetLastError = true)]
    private static extern ushort RegisterClassExW(WNDCLASSEXW* windowClass);

    [DllImport("user32", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32", SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32")]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [DllImport("user32")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32", SetLastError = true)]
    private static extern nint CreateIconFromResourceEx(byte* data, uint length,
        [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);

    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("shell32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [DllImport("shell32")]
    private static extern int Shell_NotifyIconGetRect(NOTIFYICONIDENTIFIER* identifier, RECT* bounds);
}
