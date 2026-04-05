using System.Runtime.InteropServices;

using Aprillz.MewUI.Diagnostics;
using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

// Avalonia-style XIM setup:
// - Prefer XIMPreeditPosition (IME draws preedit UI; we provide spot location).
// - Fall back to XIMPreeditNothing (commit-only).
internal static partial class X11Ime
{
    private const int LC_CTYPE = 0;

    private const long XIMPreeditPosition = 0x0004L;
    private const long XIMPreeditNothing = 0x0008L;
    private const long XIMStatusNothing = 0x0400L;

    private static partial class LibC
    {
        [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint setlocale(int category, string locale);
    }

    private static readonly EnvDebugLog.Logger ImeLogger = new("MEWUI_IME_DEBUG", "[X11][IME]");

    // Cached function pointers for dynamic XIM calls (resolved once during IC creation).
    private static unsafe delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint> _createNestedList;
    private static unsafe delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint> _setICValues;
    private static unsafe delegate* unmanaged[Cdecl]<nint, nint, nint*, nint, nint> _getICValues;
    private static nint _xFreePtr;

    [StructLayout(LayoutKind.Sequential)]
    private struct XPoint
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XIMStyles
    {
        public ushort count_styles;
        public nint supported_styles; // XIMStyle*
    }

    internal static bool TryCreateInputContext(
        nint display,
        nint window,
        out nint im,
        out nint ic,
        out nint preeditAttributesList,
        out nint spotLocationPtr,
        out bool usesPreeditPosition)
    {
        im = 0;
        ic = 0;
        preeditAttributesList = 0;
        spotLocationPtr = 0;
        usesPreeditPosition = false;

        if (display == 0 || window == 0)
        {
            return false;
        }

        try
        {
            var p = LibC.setlocale(LC_CTYPE, string.Empty);
            var locale = p != 0 ? Marshal.PtrToStringUTF8(p) : null;
            ImeLogger.Write($"setlocale(LC_CTYPE, \"\") -> {(locale ?? "null")}");
        }
        catch
        {
        }

        // Choose XIM module (ibus/fcitx) before XOpenIM.
        string? envXModifiers = null;
        try { envXModifiers = Environment.GetEnvironmentVariable("XMODIFIERS"); } catch { }

        string[] candidates = new string[6];
        int candidateCount = 0;
        if (!string.IsNullOrWhiteSpace(envXModifiers))
        {
            candidates[candidateCount++] = envXModifiers!;
        }
        // Let Xlib resolve the desktop's configured/default IM first.
        // Forcing ibus ahead of the default can break systems that actually use fcitx/fcitx5.
        candidates[candidateCount++] = string.Empty;
        candidates[candidateCount++] = "@im=local";
        candidates[candidateCount++] = "@im=fcitx5";
        candidates[candidateCount++] = "@im=fcitx";
        candidates[candidateCount++] = "@im=ibus";

        for (int i = 0; i < candidateCount && im == 0; i++)
        {
            string mod = candidates[i];
            try
            {
                var r = NativeX11.XSetLocaleModifiers(mod);
                var picked = r != 0 ? Marshal.PtrToStringUTF8(r) : null;
                ImeLogger.Write($"XSetLocaleModifiers(\"{mod}\") -> {(picked ?? "null")}");
            }
            catch
            {
            }

            try
            {
                im = NativeX11.XOpenIM(display, 0, 0, 0);
                ImeLogger.Write($"XOpenIM (after '{mod}') -> 0x{im.ToInt64():X}");
            }
            catch
            {
                im = 0;
            }
        }

        ImeLogger.Write($"Selected XMODIFIERS candidate -> im=0x{im.ToInt64():X} env='{envXModifiers ?? ""}'");

        if (im == 0)
        {
            return false;
        }

        nint lib = 0;
        try
        {
            lib = NativeLibrary.Load("libX11.so.6");
            if (!NativeLibrary.TryGetExport(lib, "XCreateIC", out var pCreateIc) || pCreateIc == 0)
            {
                NativeX11.XCloseIM(im);
                im = 0;
                return false;
            }

            unsafe
            {
                var createIc3 = (delegate* unmanaged[Cdecl]<
                    nint,
                    nint, nint,
                    nint, nint,
                    nint, nint,
                    nint,
                    nint>)pCreateIc;

                var createIc4 = (delegate* unmanaged[Cdecl]<
                    nint,
                    nint, nint,
                    nint, nint,
                    nint, nint,
                    nint, nint,
                    nint,
                    nint>)pCreateIc;

                delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint> createNestedList = null;
                if (NativeLibrary.TryGetExport(lib, "XVaCreateNestedList", out var pNestedList) && pNestedList != 0)
                {
                    createNestedList = (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint>)pNestedList;
                    _createNestedList = createNestedList;
                }

                if (NativeLibrary.TryGetExport(lib, "XSetICValues", out var pSetIcValues) && pSetIcValues != 0)
                {
                    _setICValues = (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint>)pSetIcValues;
                }

                if (NativeLibrary.TryGetExport(lib, "XGetICValues", out var pGetIcValues) && pGetIcValues != 0)
                {
                    _getICValues = (delegate* unmanaged[Cdecl]<nint, nint, nint*, nint, nint>)pGetIcValues;
                }

                if (NativeLibrary.TryGetExport(lib, "XFree", out var pXFree) && pXFree != 0)
                {
                    _xFreePtr = pXFree;
                }

                delegate* unmanaged[Cdecl]<nint, nint, nint*, nint, nint> getImValues = null;
                if (NativeLibrary.TryGetExport(lib, "XGetIMValues", out var pGetImValues) && pGetImValues != 0)
                {
                    getImValues = (delegate* unmanaged[Cdecl]<nint, nint, nint*, nint, nint>)pGetImValues;
                }

                long style = ChooseInputStyle(im, getImValues, out usesPreeditPosition);
                if (usesPreeditPosition && createNestedList != null)
                {
                    ic = TryCreatePreeditPositionIc(
                        im,
                        window,
                        style,
                        createIc4,
                        createNestedList,
                        out preeditAttributesList,
                        out spotLocationPtr);
                }

                if (ic == 0)
                {
                    usesPreeditPosition = false;
                    style = XIMPreeditNothing | XIMStatusNothing;
                    ic = TryCreateCommitOnlyIc(im, window, style, createIc3);
                }
            }

            if (ic == 0)
            {
                NativeX11.XCloseIM(im);
                im = 0;
                return false;
            }

            return true;
        }
        finally
        {
            if (lib != 0)
            {
                try { NativeLibrary.Free(lib); } catch { }
            }
        }
    }

    internal static unsafe bool TryGetFilterEvents(nint ic, out nint filterEvents)
    {
        filterEvents = 0;
        if (ic == 0 || _getICValues == null)
        {
            return false;
        }

        nint nFilterEvents = 0;
        try
        {
            nFilterEvents = Marshal.StringToCoTaskMemUTF8("filterEvents");
            nint value = 0;
            nint err = 0;
            try
            {
                err = _getICValues(ic, nFilterEvents, &value, 0);
            }
            catch
            {
                return false;
            }

            if (err != 0)
            {
                var errName = Marshal.PtrToStringUTF8(err) ?? "(unknown)";
                ImeLogger.Write($"XGetICValues(filterEvents) failed at '{errName}'");
                return false;
            }

            filterEvents = value;
            ImeLogger.Write($"XGetICValues(filterEvents) -> 0x{filterEvents.ToInt64():X}");
            return filterEvents != 0;
        }
        finally
        {
            if (nFilterEvents != 0)
            {
                Marshal.FreeCoTaskMem(nFilterEvents);
            }
        }
    }

    private static unsafe long ChooseInputStyle(nint im, delegate* unmanaged[Cdecl]<nint, nint, nint*, nint, nint> getImValues, out bool usesPreeditPosition)
    {
        usesPreeditPosition = false;

        if (getImValues == null)
        {
            return XIMPreeditNothing | XIMStatusNothing;
        }

        nint nQueryInputStyle = 0;
        nint stylesPtr = 0;
        try
        {
            nQueryInputStyle = Marshal.StringToCoTaskMemUTF8("queryInputStyle");
            nint err = 0;
            try
            {
                err = getImValues(im, nQueryInputStyle, &stylesPtr, 0);
            }
            catch
            {
                stylesPtr = 0;
            }

            if (err != 0)
            {
                var errName = Marshal.PtrToStringUTF8(err) ?? "(unknown)";
                ImeLogger.Write($"XGetIMValues failed at '{errName}', falling back.");
            }

            if (stylesPtr == 0)
            {
                return XIMPreeditNothing | XIMStatusNothing;
            }

            var styles = Marshal.PtrToStructure<XIMStyles>(stylesPtr);
            int count = styles.count_styles;
            if (count <= 0 || styles.supported_styles == 0)
            {
                return XIMPreeditNothing | XIMStatusNothing;
            }

            var p = (ulong*)styles.supported_styles;
            long fallback = XIMPreeditNothing | XIMStatusNothing;
            for (int i = 0; i < count; i++)
            {
                long s = unchecked((long)p[i]);
                if ((s & XIMPreeditPosition) != 0 && (s & XIMStatusNothing) != 0)
                {
                    usesPreeditPosition = true;
                    return s;
                }
                if ((s & XIMPreeditNothing) != 0 && (s & XIMStatusNothing) != 0)
                {
                    fallback = s;
                }
            }

            return fallback;
        }
        finally
        {
            if (stylesPtr != 0)
            {
                try { NativeX11.XFree(stylesPtr); } catch { }
            }
            if (nQueryInputStyle != 0) Marshal.FreeCoTaskMem(nQueryInputStyle);
        }
    }

    private static unsafe nint TryCreateCommitOnlyIc(
        nint im,
        nint window,
        long style,
        delegate* unmanaged[Cdecl]<
            nint,
            nint, nint,
            nint, nint,
            nint, nint,
            nint,
            nint> createIc3)
    {
        nint nInputStyle = 0;
        nint nClientWindow = 0;
        nint nFocusWindow = 0;
        try
        {
            nInputStyle = Marshal.StringToCoTaskMemUTF8("inputStyle");
            nClientWindow = Marshal.StringToCoTaskMemUTF8("clientWindow");
            nFocusWindow = Marshal.StringToCoTaskMemUTF8("focusWindow");
            return createIc3(
                im,
                nInputStyle, (nint)style,
                nClientWindow, window,
                nFocusWindow, window,
                0);
        }
        finally
        {
            if (nInputStyle != 0) Marshal.FreeCoTaskMem(nInputStyle);
            if (nClientWindow != 0) Marshal.FreeCoTaskMem(nClientWindow);
            if (nFocusWindow != 0) Marshal.FreeCoTaskMem(nFocusWindow);
        }
    }

    private static unsafe nint TryCreatePreeditPositionIc(
        nint im,
        nint window,
        long style,
        delegate* unmanaged[Cdecl]<
            nint,
            nint, nint,
            nint, nint,
            nint, nint,
            nint, nint,
            nint,
            nint> createIc4,
        delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint> createNestedList,
        out nint preeditAttributesList,
        out nint spotLocationPtr)
    {
        preeditAttributesList = 0;
        spotLocationPtr = 0;

        nint nInputStyle = 0;
        nint nClientWindow = 0;
        nint nFocusWindow = 0;
        nint nPreeditAttributes = 0;
        nint nSpotLocation = 0;
        try
        {
            nInputStyle = Marshal.StringToCoTaskMemUTF8("inputStyle");
            nClientWindow = Marshal.StringToCoTaskMemUTF8("clientWindow");
            nFocusWindow = Marshal.StringToCoTaskMemUTF8("focusWindow");
            nPreeditAttributes = Marshal.StringToCoTaskMemUTF8("preeditAttributes");
            nSpotLocation = Marshal.StringToCoTaskMemUTF8("spotLocation");

            spotLocationPtr = Marshal.AllocHGlobal(Marshal.SizeOf<XPoint>());
            Marshal.StructureToPtr(new XPoint { x = 0, y = 0 }, spotLocationPtr, fDeleteOld: false);

            preeditAttributesList = createNestedList(0, nSpotLocation, spotLocationPtr, 0);
            if (preeditAttributesList == 0)
            {
                Marshal.FreeHGlobal(spotLocationPtr);
                spotLocationPtr = 0;
                return 0;
            }

            return createIc4(
                im,
                nInputStyle, (nint)style,
                nClientWindow, window,
                nFocusWindow, window,
                nPreeditAttributes, preeditAttributesList,
                0);
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (nInputStyle != 0) Marshal.FreeCoTaskMem(nInputStyle);
            if (nClientWindow != 0) Marshal.FreeCoTaskMem(nClientWindow);
            if (nFocusWindow != 0) Marshal.FreeCoTaskMem(nFocusWindow);
            if (nPreeditAttributes != 0) Marshal.FreeCoTaskMem(nPreeditAttributes);
            if (nSpotLocation != 0) Marshal.FreeCoTaskMem(nSpotLocation);
        }
    }

    /// <summary>
    /// Updates the XIM spot location for the preedit position.
    /// Creates a new nested list each call (SFML pattern — reusing is unsafe).
    /// </summary>
    internal static unsafe void UpdateSpotLocation(nint ic, int x, int y)
    {
        if (ic == 0 || _createNestedList == null || _setICValues == null) return;

        var spot = new XPoint { x = x, y = y };
        nint nSpotLocation = Marshal.StringToCoTaskMemUTF8("spotLocation");
        nint nPreeditAttributes = Marshal.StringToCoTaskMemUTF8("preeditAttributes");

        try
        {
            nint preeditAttr = _createNestedList(0, nSpotLocation, (nint)(&spot), 0);
            if (preeditAttr != 0)
            {
                _setICValues(ic, nPreeditAttributes, preeditAttr, 0);
                // XFree the nested list
                if (_xFreePtr != 0)
                {
                    ((delegate* unmanaged[Cdecl]<nint, int>)_xFreePtr)(preeditAttr);
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (nSpotLocation != 0) Marshal.FreeCoTaskMem(nSpotLocation);
            if (nPreeditAttributes != 0) Marshal.FreeCoTaskMem(nPreeditAttributes);
        }
    }
}
