using System.Runtime.InteropServices;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Core;
using Aprillz.MewUI.Elements;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Primitives;

namespace Aprillz.MewUI.Platform.Win32;

internal sealed class Win32WindowBackend : IWindowBackend
{
    private readonly Win32PlatformHost _host;
    internal Window Window { get; }

    private UIElement? _mouseOverElement;
    private UIElement? _capturedElement;

    public nint Handle { get; private set; }

    internal Win32WindowBackend(Win32PlatformHost host, Window window)
    {
        _host = host;
        Window = window;
    }

    public void SetResizable(bool resizable)
    {
        if (Handle == 0)
            return;

        ApplyResizeMode();
    }

    public void Show()
    {
        if (Handle != 0)
        {
            User32.ShowWindow(Handle, ShowWindowCommands.SW_SHOW);
            return;
        }

        CreateWindow();
        User32.ShowWindow(Handle, ShowWindowCommands.SW_SHOW);
        User32.UpdateWindow(Handle);
    }

    public void Hide()
    {
        if (Handle != 0)
            User32.ShowWindow(Handle, ShowWindowCommands.SW_HIDE);
    }

    public void Close()
    {
        if (Handle != 0)
            User32.DestroyWindow(Handle);
    }

    public void Invalidate(bool erase)
    {
        if (Handle != 0)
            User32.InvalidateRect(Handle, 0, erase);
    }

    public void SetTitle(string title)
    {
        if (Handle != 0)
            User32.SetWindowText(Handle, title);
    }

    public void SetClientSize(double widthDip, double heightDip)
    {
        if (Handle == 0)
            return;

        uint dpi = Window.Dpi == 0 ? User32.GetDpiForWindow(Handle) : Window.Dpi;
        double dpiScale = dpi / 96.0;

        var rect = new RECT(0, 0, (int)Math.Round(widthDip * dpiScale), (int)Math.Round(heightDip * dpiScale));
        User32.AdjustWindowRectEx(ref rect, GetWindowStyle(), false, 0);
        User32.SetWindowPos(Handle, 0, 0, 0, rect.Width, rect.Height, 0x0002 | 0x0004); // SWP_NOMOVE | SWP_NOZORDER
    }

    public void CaptureMouse(UIElement element)
    {
        if (Handle == 0)
            return;

        User32.SetCapture(Handle);
        _capturedElement = element;
        element.SetMouseCaptured(true);
    }

    public void ReleaseMouseCapture()
    {
        User32.ReleaseCapture();
        if (_capturedElement != null)
        {
            _capturedElement.SetMouseCaptured(false);
            _capturedElement = null;
        }
    }

    public nint ProcessMessage(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WindowMessages.WM_NCCREATE:
                return User32.DefWindowProc(Handle, msg, wParam, lParam);

            case WindowMessages.WM_CREATE:
                return 0;

            case WindowMessages.WM_DESTROY:
                HandleDestroy();
                return 0;

            case WindowMessages.WM_CLOSE:
                Window.RaiseClosed();
                User32.DestroyWindow(Handle);
                return 0;

            case WindowMessages.WM_PAINT:
                return HandlePaint();

            case WindowMessages.WM_ERASEBKGND:
                return 1;

            case WindowMessages.WM_SIZE:
                return HandleSize(lParam);

            case WindowMessages.WM_DPICHANGED:
                return HandleDpiChanged(wParam, lParam);

            case WindowMessages.WM_ACTIVATE:
                return HandleActivate(wParam);

            case WindowMessages.WM_LBUTTONDOWN:
                return HandleMouseButton(lParam, MouseButton.Left, isDown: true);
            case WindowMessages.WM_LBUTTONUP:
                return HandleMouseButton(lParam, MouseButton.Left, isDown: false);
            case WindowMessages.WM_RBUTTONDOWN:
                return HandleMouseButton(lParam, MouseButton.Right, isDown: true);
            case WindowMessages.WM_RBUTTONUP:
                return HandleMouseButton(lParam, MouseButton.Right, isDown: false);
            case WindowMessages.WM_MBUTTONDOWN:
                return HandleMouseButton(lParam, MouseButton.Middle, isDown: true);
            case WindowMessages.WM_MBUTTONUP:
                return HandleMouseButton(lParam, MouseButton.Middle, isDown: false);

            case WindowMessages.WM_MOUSEMOVE:
                return HandleMouseMove(lParam);
            case WindowMessages.WM_MOUSELEAVE:
                return HandleMouseLeave();

            case WindowMessages.WM_MOUSEWHEEL:
                return HandleMouseWheel(wParam, lParam, isHorizontal: false);
            case WindowMessages.WM_MOUSEHWHEEL:
                return HandleMouseWheel(wParam, lParam, isHorizontal: true);

            case WindowMessages.WM_KEYDOWN:
            case WindowMessages.WM_SYSKEYDOWN:
                return HandleKeyDown(msg, wParam, lParam);
            case WindowMessages.WM_KEYUP:
            case WindowMessages.WM_SYSKEYUP:
                return HandleKeyUp(msg, wParam, lParam);

            case WindowMessages.WM_CHAR:
                return HandleChar(wParam);

            case WindowMessages.WM_SETFOCUS:
                User32.CreateCaret(Handle, 0, 1, 20);
                return 0;

            case WindowMessages.WM_KILLFOCUS:
                User32.DestroyCaret();
                return 0;

            case Win32UiDispatcher.WM_INVOKE:
                (Window.ApplicationDispatcher as Win32UiDispatcher)?.ProcessWorkItems();
                return 0;

            default:
                return User32.DefWindowProc(Handle, msg, wParam, lParam);
        }
    }

    private void CreateWindow()
    {
        uint initialDpi = User32.GetDpiForSystem();
        Window.SetDpi(initialDpi);
        double dpiScale = Window.DpiScale;

        var rect = new RECT(0, 0, (int)(Window.Width * dpiScale), (int)(Window.Height * dpiScale));
        uint style = GetWindowStyle();
        User32.AdjustWindowRectEx(ref rect, style, false, 0);

        Handle = User32.CreateWindowEx(
            0,
            Win32PlatformHost.WindowClassName,
            Window.Title,
            style,
            100,
            100,
            rect.Width,
            rect.Height,
            0,
            0,
            Kernel32.GetModuleHandle(null),
            0);

        if (Handle == 0)
            throw new InvalidOperationException($"Failed to create window. Error: {Marshal.GetLastWin32Error()}");

        _host.RegisterWindow(Handle, this);
        Window.AttachBackend(this);
        ApplyResizeMode();

        uint actualDpi = User32.GetDpiForWindow(Handle);
        if (actualDpi != initialDpi)
        {
            var oldDpi = initialDpi;
            Window.SetDpi(actualDpi);
            Window.RaiseDpiChanged(oldDpi, actualDpi);
            SetClientSize(Window.Width, Window.Height);
        }

        User32.GetClientRect(Handle, out var clientRect);
        Window.SetClientSizeDip(clientRect.Width / Window.DpiScale, clientRect.Height / Window.DpiScale);

        if (Window.Background.A == 0)
            Window.Background = Window.Theme.WindowBackground;

        Window.RaiseLoaded();
    }

    private uint GetWindowStyle()
    {
        uint style = WindowStyles.WS_OVERLAPPEDWINDOW;
        if (!Window.WindowSize.IsResizable)
            style &= ~(WindowStyles.WS_THICKFRAME | WindowStyles.WS_MAXIMIZEBOX);
        return style;
    }

    private void ApplyResizeMode()
    {
        const int GWL_STYLE = -16;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_FRAMECHANGED = 0x0020;

        uint style = GetWindowStyle();
        User32.SetWindowLongPtr(Handle, GWL_STYLE, (nint)style);
        User32.SetWindowPos(Handle, 0, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    private void HandleDestroy()
    {
        if (Window.GraphicsFactory is Aprillz.MewUI.Rendering.IWindowResourceReleaser releaser)
            releaser.ReleaseWindowResources(Handle);

        _host.UnregisterWindow(Handle);
        Window.DisposeVisualTree();
        Handle = 0;
    }

    private nint HandlePaint()
    {
        Window.PerformLayout();

        var ps = new PAINTSTRUCT();
        nint hdc = User32.BeginPaint(Handle, out ps);

        try
        {
            Window.RenderFrame(hdc);
        }
        finally
        {
            User32.EndPaint(Handle, ref ps);
        }

        return 0;
    }

    private nint HandleSize(nint lParam)
    {
        int widthPx = (short)(lParam.ToInt64() & 0xFFFF);
        int heightPx = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        Window.SetClientSizeDip(widthPx / Window.DpiScale, heightPx / Window.DpiScale);
        Window.PerformLayout();
        Window.Invalidate();

        Window.RaiseSizeChanged(widthPx / Window.DpiScale, heightPx / Window.DpiScale);
        return 0;
    }

    private nint HandleDpiChanged(nint wParam, nint lParam)
    {
        uint newDpi = (uint)(wParam.ToInt64() & 0xFFFF);
        uint oldDpi = Window.Dpi;
        Window.SetDpi(newDpi);

        var suggestedRect = Marshal.PtrToStructure<RECT>(lParam);
        User32.SetWindowPos(Handle, 0,
            suggestedRect.left, suggestedRect.top,
            suggestedRect.Width, suggestedRect.Height,
            0x0004 | 0x0010); // SWP_NOZORDER | SWP_NOACTIVATE

        Window.RaiseDpiChanged(oldDpi, newDpi);
        Window.PerformLayout();
        Window.Invalidate();

        return 0;
    }

    private nint HandleActivate(nint wParam)
    {
        bool active = (wParam.ToInt64() & 0xFFFF) != 0;
        Window.SetIsActive(active);
        if (active)
            Window.Activated?.Invoke();
        else
            Window.Deactivated?.Invoke();
        return 0;
    }

    private nint HandleMouseMove(nint lParam)
    {
        var pos = GetMousePosition(lParam);
        var screenPos = ClientToScreen(pos);

        var element = _capturedElement ?? Window.HitTest(pos);

        if (element != _mouseOverElement)
        {
            _mouseOverElement?.SetMouseOver(false);
            element?.SetMouseOver(true);
            _mouseOverElement = element;
        }

        bool leftDown = (User32.GetKeyState(VirtualKeys.VK_LBUTTON) & 0x8000) != 0;
        bool rightDown = (User32.GetKeyState(VirtualKeys.VK_RBUTTON) & 0x8000) != 0;
        bool middleDown = (User32.GetKeyState(VirtualKeys.VK_MBUTTON) & 0x8000) != 0;
        var args = new MouseEventArgs(pos, screenPos, MouseButton.Left, leftDown, rightDown, middleDown);
        element?.RaiseMouseMove(args);

        return 0;
    }

    private nint HandleMouseButton(nint lParam, MouseButton button, bool isDown)
    {
        var pos = GetMousePosition(lParam);
        var screenPos = ClientToScreen(pos);

        Window.ClosePopupsIfClickOutside(pos);

        var element = _capturedElement ?? Window.HitTest(pos);

        var args = new MouseEventArgs(pos, screenPos, button,
            button == MouseButton.Left && isDown,
            button == MouseButton.Right && isDown,
            button == MouseButton.Middle && isDown);

        if (isDown)
        {
            if (element?.Focusable == true)
                Window.FocusManager.SetFocus(element);

            element?.RaiseMouseDown(args);
        }
        else
        {
            element?.RaiseMouseUp(args);
        }

        return 0;
    }

    private nint HandleMouseWheel(nint wParam, nint lParam, bool isHorizontal)
    {
        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

        var screenX = (short)(lParam.ToInt64() & 0xFFFF);
        var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var pt = new POINT(screenX, screenY);
        User32.ScreenToClient(Handle, ref pt);
        var pos = new Point(pt.x / Window.DpiScale, pt.y / Window.DpiScale);

        var element = Window.HitTest(pos);
        var args = new MouseWheelEventArgs(pos, new Point(screenX, screenY), delta, isHorizontal);

        // Bubble to parents until handled (ScrollViewer etc.).
        for (var current = element; current != null && !args.Handled; current = current.Parent as UIElement)
            current.RaiseMouseWheel(args);

        return 0;
    }

    private nint HandleMouseLeave()
    {
        if (_mouseOverElement != null)
        {
            _mouseOverElement.SetMouseOver(false);
            _mouseOverElement = null;
        }
        return 0;
    }

    private ModifierKeys GetModifierKeys()
    {
        var modifiers = ModifierKeys.None;

        if ((User32.GetKeyState(VirtualKeys.VK_CONTROL) & 0x8000) != 0)
            modifiers |= ModifierKeys.Control;
        if ((User32.GetKeyState(VirtualKeys.VK_SHIFT) & 0x8000) != 0)
            modifiers |= ModifierKeys.Shift;
        if ((User32.GetKeyState(VirtualKeys.VK_MENU) & 0x8000) != 0)
            modifiers |= ModifierKeys.Alt;

        return modifiers;
    }

    private nint HandleKeyDown(uint msg, nint wParam, nint lParam)
    {
        int platformKey = (int)wParam.ToInt64();
        bool isRepeat = ((lParam.ToInt64() >> 30) & 1) != 0;
        var modifiers = GetModifierKeys();

        // Let the OS handle Alt+F4 so it translates to WM_SYSCOMMAND/SC_CLOSE and our WM_CLOSE path runs.
        if (msg == WindowMessages.WM_SYSKEYDOWN &&
            modifiers.HasFlag(ModifierKeys.Alt) &&
            platformKey == VirtualKeys.VK_F4)
        {
            return User32.DefWindowProc(Handle, msg, wParam, lParam);
        }

        var args = new KeyEventArgs(MapKey(platformKey), platformKey, modifiers, isRepeat);
        Window.RaisePreviewKeyDown(args);
        if (args.Handled)
            return 0;

        if (args.Key == Key.Tab)
        {
            if (modifiers.HasFlag(ModifierKeys.Shift))
                Window.FocusManager.MoveFocusPrevious();
            else
                Window.FocusManager.MoveFocusNext();
            return 0;
        }

        Window.FocusManager.FocusedElement?.RaiseKeyDown(args);

        return args.Handled ? 0 : User32.DefWindowProc(Handle, msg, wParam, lParam);
    }

    private nint HandleKeyUp(uint msg, nint wParam, nint lParam)
    {
        int platformKey = (int)wParam.ToInt64();
        var modifiers = GetModifierKeys();

        var args = new KeyEventArgs(MapKey(platformKey), platformKey, modifiers);
        Window.RaisePreviewKeyUp(args);
        if (!args.Handled)
        Window.FocusManager.FocusedElement?.RaiseKeyUp(args);

        return args.Handled ? 0 : User32.DefWindowProc(Handle, msg, wParam, lParam);
    }

    private nint HandleChar(nint wParam)
    {
        char c = (char)wParam.ToInt64();
        if (c == '\b')
            return 0;

        if (char.IsControl(c) && c != '\r' && c != '\t')
            return 0;

        var args = new TextInputEventArgs(c.ToString());
        Window.RaisePreviewTextInput(args);
        if (!args.Handled)
            Window.FocusManager.FocusedElement?.RaiseTextInput(args);

        return 0;
    }

    private static Input.Key MapKey(int vk)
    {
        // Digits (top row)
        if (vk is >= 0x30 and <= 0x39)
            return (Input.Key)((int)Key.D0 + (vk - 0x30));

        // Letters
        if (vk is >= 0x41 and <= 0x5A)
            return (Input.Key)((int)Key.A + (vk - 0x41));

        // Numpad digits
        if (vk is >= 0x60 and <= 0x69)
            return (Input.Key)((int)Key.NumPad0 + (vk - 0x60));

        return vk switch
        {
            VirtualKeys.VK_BACK => Key.Backspace,
            VirtualKeys.VK_TAB => Key.Tab,
            VirtualKeys.VK_RETURN => Key.Enter,
            VirtualKeys.VK_ESCAPE => Key.Escape,
            VirtualKeys.VK_SPACE => Key.Space,

            VirtualKeys.VK_LEFT => Key.Left,
            VirtualKeys.VK_UP => Key.Up,
            VirtualKeys.VK_RIGHT => Key.Right,
            VirtualKeys.VK_DOWN => Key.Down,

            VirtualKeys.VK_INSERT => Key.Insert,
            VirtualKeys.VK_DELETE => Key.Delete,
            VirtualKeys.VK_HOME => Key.Home,
            VirtualKeys.VK_END => Key.End,
            VirtualKeys.VK_PRIOR => Key.PageUp,
            VirtualKeys.VK_NEXT => Key.PageDown,

            VirtualKeys.VK_ADD => Key.Add,
            VirtualKeys.VK_SUBTRACT => Key.Subtract,
            VirtualKeys.VK_MULTIPLY => Key.Multiply,
            VirtualKeys.VK_DIVIDE => Key.Divide,
            VirtualKeys.VK_DECIMAL => Key.Decimal,

            _ => Key.None
        };
    }

    private Point GetMousePosition(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        // lParam is in device pixels; convert to DIPs.
        return new Point(x / Window.DpiScale, y / Window.DpiScale);
    }

    private Point ClientToScreen(Point clientPoint)
    {
        var pt = new POINT((int)(clientPoint.X * Window.DpiScale), (int)(clientPoint.Y * Window.DpiScale));
        User32.ClientToScreen(Handle, ref pt);
        return new Point(pt.x, pt.y);
    }

    public void Dispose()
    {
        if (Handle != 0)
            Close();
    }
}
