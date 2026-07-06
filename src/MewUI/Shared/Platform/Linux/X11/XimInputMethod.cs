using System.Runtime.InteropServices;
using System.Text;

using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Native;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// XIM-based fallback input method. Wraps <c>XOpenIM</c>/<c>XCreateIC</c>/<c>XFilterEvent</c>/<c>Xutf8LookupString</c>.
/// </summary>
internal sealed class XimInputMethod : IX11InputMethod
{
    private static readonly EnvDebugLogger ImeLogger = new("MEWUI_IME_DEBUG", "[XIM]");

    private readonly nint _display;
    private readonly nint _window;
    private nint _xim;
    private nint _xic;
    private nint _imePreeditAttributesList;
    private nint _imeSpotLocationPtr;
    private bool _usesPreeditPosition;
    private bool _disposed;

    public bool IsAvailable => _xic != 0;
    public bool SupportsPreedit => false; // XIM fallback: commit-only

#pragma warning disable CS0067 // Required by IX11InputMethod; XIM uses synchronous commit via ProcessKeyEvent
    public event Action<string>? CommitText;
    public event Action<X11PreeditState>? PreeditChanged;
#pragma warning restore CS0067

    private XimInputMethod(nint display, nint window)
    {
        _display = display;
        _window = window;
    }

    /// <summary>
    /// Attempts to create a XIM input method. Returns null if XIM is unavailable.
    /// </summary>
    internal static XimInputMethod? TryCreate(nint display, nint window)
    {
        if (display == 0 || window == 0) return null;

        var im = new XimInputMethod(display, window);
        if (!X11Ime.TryCreateInputContext(
                display,
                window,
                out im._xim,
                out im._xic,
                out im._imePreeditAttributesList,
                out im._imeSpotLocationPtr,
                out im._usesPreeditPosition))
        {
            im.Dispose();
            return null;
        }

        ImeLogger.Write($"Created: im=0x{im._xim.ToInt64():X} ic=0x{im._xic.ToInt64():X} preeditPosition={im._usesPreeditPosition}");
        return im;
    }

    /// <summary>
    /// Gets the XIC filter event mask to merge into <c>XSelectInput</c>.
    /// </summary>
    internal bool TryGetFilterEvents(out nint filterEvents)
        => X11Ime.TryGetFilterEvents(_xic, out filterEvents);

    public void OnFocusIn()
    {
        if (_xic == 0) return;
        ImeLogger.Write($"FocusIn -> XSetICFocus ic=0x{_xic.ToInt64():X}");
        NativeX11.XSetICFocus(_xic);
    }

    public void OnFocusOut()
    {
        if (_xic == 0) return;
        ImeLogger.Write($"FocusOut -> XUnsetICFocus ic=0x{_xic.ToInt64():X}");
        NativeX11.XUnsetICFocus(_xic);
    }

    public void Reset()
    {
        if (_xic == 0) return;
        NativeX11.XUnsetICFocus(_xic);
        NativeX11.XSetICFocus(_xic);
    }

    public void UpdateCursorRect(Rect rectInWindowPx)
    {
        if (!_usesPreeditPosition || _xic == 0) return;
        X11Ime.UpdateSpotLocation(_xic, (int)rectInWindowPx.X, (int)rectInWindowPx.Y);
    }

    public X11ImeProcessResult ProcessKeyEvent(ref XEvent ev, bool isKeyDown)
    {
        if (_xic == 0)
            return new X11ImeProcessResult(Handled: false, ForwardKeyToApp: true, CommittedText: null);

        bool filtered = NativeX11.XFilterEvent(ref ev, _window) != 0;

        ImeLogger.Write($"XFilterEvent filtered={filtered} isDown={isKeyDown} xic=0x{_xic.ToInt64():X}");

        // In preedit-position mode, filtered events are owned by the IME.
        if (filtered && _usesPreeditPosition)
            return new X11ImeProcessResult(Handled: true, ForwardKeyToApp: false, CommittedText: null);

        // Extract committed text (KeyPress only).
        string? committed = isKeyDown ? LookupString(ref ev.xkey, filtered) : null;

        // In commit-only mode, even filtered events should still route keys.
        return new X11ImeProcessResult(
            Handled: filtered && _usesPreeditPosition,
            ForwardKeyToApp: !(filtered && _usesPreeditPosition),
            CommittedText: committed);
    }

    private unsafe string? LookupString(ref XKeyEvent e, bool filtered)
    {
        if (filtered && _usesPreeditPosition)
            return null;

        Span<byte> buf = stackalloc byte[128];
        int byteCount;
        int lookupStatus;

        fixed (byte* p = buf)
        {
            byteCount = NativeX11.Xutf8LookupString(_xic, ref e, p, buf.Length, out _, out lookupStatus);
        }

        ImeLogger.Write($"Xutf8LookupString status={lookupStatus} bytes={byteCount} filtered={filtered}");

        // XLookupChars=2, XLookupBoth=4
        if ((lookupStatus == 2 || lookupStatus == 4) && byteCount > 0)
        {
            byteCount = Math.Min(byteCount, buf.Length);
            string s = Encoding.UTF8.GetString(buf[..byteCount]);
            if (!string.IsNullOrEmpty(s))
            {
                ImeLogger.Write($" -> text '{s}'");
                return s;
            }
        }

        return null;
    }

    /// <summary>
    /// Fallback text lookup when no XIC is available.
    /// </summary>
    internal static unsafe string? LookupStringWithoutIc(ref XKeyEvent e)
    {
        Span<byte> buf = stackalloc byte[128];
        int byteCount;
        fixed (byte* p = buf)
        {
            byteCount = NativeX11.XLookupString(ref e, p, buf.Length, out _, out _);
        }

        if (byteCount > 0)
        {
            byteCount = Math.Min(byteCount, buf.Length);
            string s = Encoding.UTF8.GetString(buf[..byteCount]);
            if (!string.IsNullOrEmpty(s))
                return s;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_xic != 0)
            {
                NativeX11.XDestroyIC(_xic);
                _xic = 0;
            }
            if (_xim != 0)
            {
                _ = NativeX11.XCloseIM(_xim);
                _xim = 0;
            }
        }
        catch { }

        try
        {
            if (_imePreeditAttributesList != 0)
            {
                NativeX11.XFree(_imePreeditAttributesList);
                _imePreeditAttributesList = 0;
            }
            if (_imeSpotLocationPtr != 0)
            {
                Marshal.FreeHGlobal(_imeSpotLocationPtr);
                _imeSpotLocationPtr = 0;
            }
        }
        catch { }
    }
}
