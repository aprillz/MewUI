using System.Collections.Concurrent;

using WF = System.Windows.Forms;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Hosts a Windows Forms control inside a MewUI layout by placing it in a native child window.
/// </summary>
/// <remarks>
/// The hosted control is drawn by Windows on top of everything MewUI renders, so it always covers
/// MewUI content that overlaps it regardless of element order.
/// </remarks>
public sealed class WinFormsHost : FrameworkElement
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int BS_OWNERDRAW = 0x0000000B;
    private const int GWLP_WNDPROC = -4;
    private const int GWL_STYLE = -16;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint WM_PARENTNOTIFY = 0x0210;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const double DEFAULT_WIDTH = 320;
    private const double DEFAULT_HEIGHT = 240;

    private const int WH_GETMESSAGE = 3;
    private const int HC_ACTION = 0;
    private const nint PM_REMOVE = 1;
    private const uint WM_NULL = 0x0000;
    private const uint WM_KEYFIRST = 0x0100;
    private const uint WM_KEYLAST = 0x0109;
    private const uint WM_KEYDOWN = 0x0100;
    private const nint VK_TAB = 0x09;
    private const int VK_SHIFT = 0x10;

    private static readonly ConcurrentDictionary<nint, WinFormsHost> _hostMap = new();
    private static readonly WndProcDelegate _hostWndProc = HostWndProc;
    private static readonly nint _hostWndProcPtr = Marshal.GetFunctionPointerForDelegate(_hostWndProc);
    private static readonly HookProcDelegate _getMessageProc = GetMessageProc;
    private static readonly nint _getMessageProcPtr = Marshal.GetFunctionPointerForDelegate(_getMessageProc);

    private static nint _messageHook;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private delegate nint HookProcDelegate(int code, nint wParam, nint lParam);

    /// <summary>The Windows Forms control shown in this element's layout slot.</summary>
    public static readonly MewProperty<WF.Control?> ChildProperty =
        MewProperty<WF.Control?>.Register<WinFormsHost>(
            nameof(Child),
            null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnChildChanged(oldValue, newValue));

    /// <summary>
    /// When true the hosted window is clipped to the visible region of its MewUI ancestors.
    /// Default true; set false to match the unclipped behavior of WPF's HwndHost.
    /// </summary>
    public static readonly MewProperty<bool> ClipToAncestorsProperty =
        MewProperty<bool>.Register<WinFormsHost>(
            nameof(ClipToAncestors),
            true,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.UpdateHostBounds());

    private WF.ContainerControl? _container;
    private nint _hostHandle;
    private nint _ownerWindowHandle;
    private nint _prevWndProc;
    private Rect _arrangedBounds;
    private bool _disposed;

    static WinFormsHost()
    {
        FocusableProperty.OverrideDefaultValue<WinFormsHost>(true);
    }

    public WF.Control? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    public bool ClipToAncestors
    {
        get => GetValue(ClipToAncestorsProperty);
        set => SetValue(ClipToAncestorsProperty, value);
    }

    /// <summary>The native child window that owns the hosted control, or 0 before it is created.</summary>
    public nint HostHandle => _hostHandle;

    private nint ContainerHandle
    {
        get
        {
            var container = _container;
            if (container == null || container.IsDisposed || !container.IsHandleCreated)
            {
                return 0;
            }

            return container.Handle;
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var child = Child;
        if (child == null)
        {
            return new Size(DEFAULT_WIDTH, DEFAULT_HEIGHT);
        }

        var preferred = child.PreferredSize;
        if (preferred.Width <= 0 || preferred.Height <= 0)
        {
            return new Size(DEFAULT_WIDTH, DEFAULT_HEIGHT);
        }

        double scale = GetDpi() / 96.0;
        return new Size(preferred.Width / scale, preferred.Height / scale);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        _arrangedBounds = bounds;
        UpdateHostBounds();
    }

    protected override void OnParentChanged()
    {
        base.OnParentChanged();
        UpdateHostBounds();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        UpdateHostBounds();
    }

    protected override void OnVisibilityChanged()
    {
        base.OnVisibilityChanged();
        UpdateHostBounds();
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateHostBounds();
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();

        if (_hostHandle == 0)
        {
            return;
        }

        // Focus has to land on a guest control rather than the host window, or keyboard input has
        // no target and Tab cannot find a starting point.
        bool backward = (Interop.GetKeyState(VK_SHIFT) & 0x8000) != 0;
        nint guest = FindEdgeTabStop(backward);
        Interop.SetFocus(guest != 0 ? guest : _hostHandle);
    }

    /// <summary>Returns the guest's first tab stop, or its last one when entering backwards.</summary>
    private nint FindEdgeTabStop(bool backward)
    {
        var container = _container;
        if (container == null)
        {
            return 0;
        }

        var candidate = container.GetNextControl(null, !backward);
        while (candidate != null && !IsUsableTabStop(candidate))
        {
            candidate = container.GetNextControl(candidate, !backward);
        }

        return candidate != null && candidate.IsHandleCreated ? candidate.Handle : 0;
    }

    private static bool IsUsableTabStop(WF.Control control)
        => control.TabStop && control.Enabled && control.Visible;

    protected override void OnLostFocus()
    {
        base.OnLostFocus();

        if (_hostHandle == 0)
        {
            return;
        }

        // Without handing Win32 focus back, keystrokes keep reaching the guest after MewUI moves focus away.
        nint focused = Interop.GetFocus();
        bool focusInsideHost = focused == _hostHandle || Interop.IsChild(_hostHandle, focused) != 0;
        if (!focusInsideHost)
        {
            return;
        }

        if (FindVisualRoot() is Window window && window.Handle != 0)
        {
            Interop.SetFocus(window.Handle);
        }
    }

    protected override void OnDispose()
    {
        base.OnDispose();

        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyHost();
    }

    private void OnChildChanged(WF.Control? oldChild, WF.Control? newChild)
    {
        if (oldChild != null && _container != null)
        {
            _container.Controls.Remove(oldChild);
        }

        if (newChild != null && _container != null)
        {
            AttachChild(newChild);
        }

        UpdateHostBounds();
    }

    private void AttachChild(WF.Control child)
    {
        if (_container == null)
        {
            return;
        }

        child.Dock = WF.DockStyle.Fill;
        _container.Controls.Add(child);
    }

    private void UpdateHostBounds()
    {
        if (_disposed)
        {
            return;
        }

        if (FindVisualRoot() is not Window window || window.Handle == 0 || window.Dpi == 0)
        {
            // Detached from the visual tree, which is how a TabControl holds the content of tabs
            // that are not selected. The host window survives and must be hidden explicitly.
            if (_hostHandle != 0)
            {
                Interop.ShowWindow(_hostHandle, SW_HIDE);
            }

            return;
        }

        EnsureHost(window);
        if (_hostHandle == 0)
        {
            return;
        }

        double scale = window.Dpi / 96.0;
        var local = new Rect(0, 0, _arrangedBounds.Width, _arrangedBounds.Height);
        var hostPixels = ToPixelRect(TranslateRect(local, window), scale);
        var clipPixels = ToPixelRect(ComputeVisibleRect(window), scale);

        bool clippedAway = clipPixels.Right <= clipPixels.Left || clipPixels.Bottom <= clipPixels.Top;
        if (clippedAway || !IsVisible)
        {
            Interop.ShowWindow(_hostHandle, SW_HIDE);
            return;
        }

        int width = Math.Max(0, hostPixels.Right - hostPixels.Left);
        int height = Math.Max(0, hostPixels.Bottom - hostPixels.Top);

        Interop.SetWindowPos(
            _hostHandle,
            0,
            hostPixels.Left,
            hostPixels.Top,
            width,
            height,
            SWP_NOACTIVATE | SWP_NOOWNERZORDER);

        if (ClipToAncestors)
        {
            // SetWindowRgn takes host-relative coordinates and owns the region handle afterwards.
            nint region = Interop.CreateRectRgn(
                clipPixels.Left - hostPixels.Left,
                clipPixels.Top - hostPixels.Top,
                clipPixels.Right - hostPixels.Left,
                clipPixels.Bottom - hostPixels.Top);
            Interop.SetWindowRgn(_hostHandle, region, 1);
        }
        else
        {
            Interop.SetWindowRgn(_hostHandle, 0, 1);
        }

        Interop.ShowWindow(_hostHandle, SW_SHOWNOACTIVATE);

        if (_container != null)
        {
            _container.Bounds = new System.Drawing.Rectangle(0, 0, width, height);
        }
    }

    private Rect ComputeVisibleRect(Window window)
    {
        var result = TranslateRect(new Rect(0, 0, _arrangedBounds.Width, _arrangedBounds.Height), window);
        if (!ClipToAncestors)
        {
            return result;
        }

        for (Element? ancestor = Parent; ancestor != null && ancestor != window; ancestor = ancestor.Parent)
        {
            var ancestorLocal = new Rect(0, 0, ancestor.Bounds.Width, ancestor.Bounds.Height);
            result = Intersect(result, ancestor.TranslateRect(ancestorLocal, window));
        }

        return result;
    }

    private static Rect Intersect(Rect first, Rect second)
    {
        double left = Math.Max(first.X, second.X);
        double top = Math.Max(first.Y, second.Y);
        double right = Math.Min(first.Right, second.Right);
        double bottom = Math.Min(first.Bottom, second.Bottom);
        if (right <= left || bottom <= top)
        {
            return new Rect(left, top, 0, 0);
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private static PixelRect ToPixelRect(Rect rect, double scale) => new(
        LayoutRounding.RoundToPixelInt(rect.X, scale),
        LayoutRounding.RoundToPixelInt(rect.Y, scale),
        LayoutRounding.RoundToPixelInt(rect.Right, scale),
        LayoutRounding.RoundToPixelInt(rect.Bottom, scale));

    private void EnsureHost(Window window)
    {
        if (_hostHandle != 0)
        {
            if (_ownerWindowHandle != window.Handle)
            {
                EnsureParentClipsChildren(window.Handle);
                Interop.SetParent(_hostHandle, window.Handle);
                _ownerWindowHandle = window.Handle;
            }

            return;
        }

        _ownerWindowHandle = window.Handle;

        EnsureParentClipsChildren(window.Handle);

        // A focusable built-in class lets a click inside the guest transfer Win32 focus on its own,
        // and BS_OWNERDRAW keeps the host from painting a background under the guest.
        _hostHandle = Interop.CreateWindowExW(
            0,
            "BUTTON",
            string.Empty,
            WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN | BS_OWNERDRAW,
            0,
            0,
            0,
            0,
            window.Handle,
            0,
            0,
            0);

        if (_hostHandle == 0)
        {
            return;
        }

        _hostMap[_hostHandle] = this;
        _prevWndProc = Interop.SetWindowLongPtr(_hostHandle, GWLP_WNDPROC, _hostWndProcPtr);
        EnsureMessageHook();

        // Windows Forms owns its control tree, so the guest lives in a container whose handle is
        // reparented into the host. Reparenting the guest directly makes Windows Forms recreate it.
        _container = new WF.ContainerControl();
        var child = Child;
        if (child != null)
        {
            AttachChild(child);
        }

        _container.CreateControl();
        nint containerHandle = _container.Handle;
        Interop.SetParent(containerHandle, _hostHandle);
        nint containerStyle = Interop.GetWindowLongPtr(containerHandle, GWL_STYLE);
        Interop.SetWindowLongPtr(containerHandle, GWL_STYLE, containerStyle | WS_CHILD | WS_VISIBLE);
        Interop.ShowWindow(containerHandle, SW_SHOWNOACTIVATE);
    }

    /// <summary>Routes dialog keys to the guest, which the MewUI message loop does not do itself.</summary>
    private static unsafe nint GetMessageProc(int code, nint wParam, nint lParam)
    {
        if (code != HC_ACTION || wParam != PM_REMOVE)
        {
            return Interop.CallNextHookEx(_messageHook, code, wParam, lParam);
        }

        var message = (Interop.MSG*)lParam;
        if (message->Message >= WM_KEYFIRST && message->Message <= WM_KEYLAST)
        {
            var host = FindHostFor(message->HWnd);
            if (host != null && host.HandleKeyMessage(message))
            {
                // Already acted on; blank the message so the MewUI loop does not dispatch it again.
                message->Message = WM_NULL;
            }
        }

        return Interop.CallNextHookEx(_messageHook, code, wParam, lParam);
    }

    private static WinFormsHost? FindHostFor(nint target)
    {
        if (target == 0)
        {
            return null;
        }

        foreach (var host in _hostMap.Values)
        {
            nint handle = host._hostHandle;
            if (handle != 0 && (handle == target || Interop.IsChild(handle, target) != 0))
            {
                return host;
            }
        }

        return null;
    }

    private unsafe bool HandleKeyMessage(Interop.MSG* message)
    {
        nint container = ContainerHandle;
        if (container == 0)
        {
            return false;
        }

        if (message->Message == WM_KEYDOWN && message->WParam == VK_TAB && TryMoveFocusOutOfHost())
        {
            return true;
        }

        return Interop.IsDialogMessage(container, message) != 0;
    }

    /// <summary>Hands focus to the next MewUI element when Tab runs past the guest's last tab stop.</summary>
    private bool TryMoveFocusOutOfHost()
    {
        var container = _container;
        if (container == null || FindVisualRoot() is not Window window)
        {
            return false;
        }

        bool backward = (Interop.GetKeyState(VK_SHIFT) & 0x8000) != 0;
        var focused = WF.Control.FromHandle(Interop.GetFocus());
        if (focused == null)
        {
            return false;
        }

        var next = container.GetNextControl(focused, !backward);
        while (next != null && !IsUsableTabStop(next))
        {
            next = container.GetNextControl(next, !backward);
        }

        if (next != null)
        {
            return false;
        }

        return backward ? window.FocusManager.MoveFocusPrevious() : window.FocusManager.MoveFocusNext();
    }

    private static void EnsureMessageHook()
    {
        if (_messageHook != 0)
        {
            return;
        }

        _messageHook = Interop.SetWindowsHookEx(
            WH_GETMESSAGE, _getMessageProcPtr, 0, Interop.GetCurrentThreadId());
    }

    private static void ReleaseMessageHookIfIdle()
    {
        if (_messageHook == 0 || !_hostMap.IsEmpty)
        {
            return;
        }

        Interop.UnhookWindowsHookEx(_messageHook);
        _messageHook = 0;
    }

    private static void EnsureParentClipsChildren(nint windowHandle)
    {
        nint style = Interop.GetWindowLongPtr(windowHandle, GWL_STYLE);
        if ((style & WS_CLIPCHILDREN) != 0)
        {
            return;
        }

        // MewUI presents its frame over the whole client area, and a guest only repaints on WM_PAINT,
        // so without this the hosted control is erased on the next MewUI repaint and stays blank.
        Interop.SetWindowLongPtr(windowHandle, GWL_STYLE, style | WS_CLIPCHILDREN);
        Interop.SetWindowPos(
            windowHandle,
            0,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private void DestroyHost()
    {
        if (_container != null)
        {
            _container.Dispose();
            _container = null;
        }

        if (_hostHandle != 0)
        {
            _hostMap.TryRemove(_hostHandle, out _);
            Interop.DestroyWindow(_hostHandle);
            _hostHandle = 0;
        }

        ReleaseMessageHookIfIdle();
    }

    private static nint HostWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_PARENTNOTIFY && _hostMap.TryGetValue(hWnd, out var host))
        {
            uint childMsg = (uint)wParam & 0xFFFF;
            if (childMsg == WM_MOUSEMOVE && host.FindVisualRoot() is Window hovered)
            {
                hovered.ClearMouseOver();
            }
            else if (childMsg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN
                && host.FindVisualRoot() is Window clicked)
            {
                clicked.ClearMouseOver();
                clicked.FocusManager.SetFocus(host);
            }
        }

        if (_hostMap.TryGetValue(hWnd, out var target) && target._prevWndProc != 0)
        {
            return Interop.CallWindowProc(target._prevWndProc, hWnd, msg, wParam, lParam);
        }

        return Interop.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);
}
