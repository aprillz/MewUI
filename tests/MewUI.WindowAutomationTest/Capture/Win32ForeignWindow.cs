using System.Runtime.InteropServices;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A plain top-level Win32 window on its own thread with its own message loop. Win32 mouse capture belongs to
/// a thread, so to the application's UI thread this window takes input the way another application's does.
/// It is topmost and a click does not activate it, so the scenario window stays active.
/// </summary>
internal sealed class Win32ForeignWindow
{
    private const string CLASS_NAME = "MewUIWindowAutomationForeignWindow";
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_BORDER = 0x00800000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_SHOWNOACTIVATE = 4;
    private const nint COLOR_WINDOW_BRUSH = 6;

    private readonly ManualResetEventSlim _created = new();

    public Win32ForeignWindow(int x, int y, int width, int height)
    {
        var thread = new Thread(() => RunMessageLoop(x, y, width, height))
        {
            IsBackground = true,
            Name = "WindowAutomationTest foreign window",
        };
        thread.Start();

        if (!_created.Wait(TimeSpan.FromSeconds(5)) || Handle == 0)
        {
            throw new InvalidOperationException("The stand-in for another application's window was not created.");
        }
    }

    public nint Handle { get; private set; }

    private void RunMessageLoop(int x, int y, int width, int height)
    {
        try
        {
            nint instance = GetModuleHandle(null);
            var windowClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = NativeLibrary.GetExport(NativeLibrary.Load("user32.dll"), "DefWindowProcW"),
                hInstance = instance,
                hbrBackground = COLOR_WINDOW_BRUSH,
                lpszClassName = CLASS_NAME,
            };
            RegisterClassEx(ref windowClass);

            Handle = CreateWindowEx(
                WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, CLASS_NAME, "WindowAutomationTest foreign window",
                WS_POPUP | WS_BORDER, x, y, width, height, 0, 0, instance, 0);
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
        }
        finally
        {
            _created.Set();
        }

        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out MSG message, nint hwnd, uint filterMin, uint filterMax);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref MSG message);
}
