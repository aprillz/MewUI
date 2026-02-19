using System.Runtime.InteropServices;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

internal static class X11Ime
{
    private const long XIMPreeditCallbacks = 0x0002L;
    // XIMPreeditNothing | XIMStatusNothing
    private const long XIMPreeditNothing = 0x0008L;
    private const long XIMStatusNothing = 0x0400L;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint XCreateIC_3Pairs_Delegate(
        nint im,
        nint name1, nint value1,
        nint name2, nint value2,
        nint name3, nint value3,
        nint terminator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint XCreateIC_4Pairs_Delegate(
        nint im,
        nint name1, nint value1,
        nint name2, nint value2,
        nint name3, nint value3,
        nint name4, nint value4,
        nint terminator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint XVaCreateNestedList_3Pairs_Delegate(
        nint dummy,
        nint name1, nint value1,
        nint name2, nint value2,
        nint name3, nint value3,
        nint terminator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XIMProc(nint ic, nint clientData, nint callData);

    [StructLayout(LayoutKind.Sequential)]
    private struct XIMCallback
    {
        public nint callback;
        public nint client_data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XIMPreeditDrawCallbackStruct
    {
        public int caret;
        public int chg_first;
        public int chg_length;
        public nint text; // XIMText*
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XIMText
    {
        public ushort length;
        public nint feedback; // XIMFeedback*
        public int encoding_is_wchar; // Bool
        public XIMStringUnion @string;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct XIMStringUnion
    {
        [FieldOffset(0)]
        public nint multi_byte; // char*

        [FieldOffset(0)]
        public nint wide_char; // wchar_t*
    }

    private static readonly XIMProc s_preeditStartProc = PreeditStart;
    private static readonly XIMProc s_preeditDrawProc = PreeditDraw;
    private static readonly XIMProc s_preeditDoneProc = PreeditDone;

    private static readonly nint s_preeditStartPtr = Marshal.GetFunctionPointerForDelegate(s_preeditStartProc);
    private static readonly nint s_preeditDrawPtr = Marshal.GetFunctionPointerForDelegate(s_preeditDrawProc);
    private static readonly nint s_preeditDonePtr = Marshal.GetFunctionPointerForDelegate(s_preeditDoneProc);

    private static int PreeditStart(nint ic, nint clientData, nint callData)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(clientData);
            if (handle.Target is X11WindowBackend backend)
            {
                backend.ImePreeditStart();
            }
        }
        catch
        {
        }

        return 0;
    }

    private static int PreeditDraw(nint ic, nint clientData, nint callData)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(clientData);
            if (handle.Target is not X11WindowBackend backend)
            {
                return 0;
            }

            if (callData == 0)
            {
                return 0;
            }

            var draw = Marshal.PtrToStructure<XIMPreeditDrawCallbackStruct>(callData);

            string replacement = string.Empty;
            if (draw.text != 0)
            {
                var text = Marshal.PtrToStructure<XIMText>(draw.text);
                if (text.length > 0)
                {
                    if (text.encoding_is_wchar != 0 && text.@string.wide_char != 0)
                    {
                        // wchar_t on Linux is typically 4 bytes (UTF-32 code points).
                        unsafe
                        {
                            int count = text.length;
                            var p = (uint*)text.@string.wide_char;
                            Span<uint> scalars = new(p, count);
                            var sb = new System.Text.StringBuilder(capacity: count);
                            Span<char> tmp = stackalloc char[2];
                            for (int i = 0; i < scalars.Length; i++)
                            {
                                uint scalar = scalars[i];
                                if (scalar == 0)
                                {
                                    continue;
                                }

                                if (!System.Text.Rune.TryCreate(scalar, out var rune))
                                {
                                    continue;
                                }

                                int len = rune.EncodeToUtf16(tmp);
                                sb.Append(tmp[..len]);
                            }

                            replacement = sb.ToString();
                        }
                    }
                    else if (text.@string.multi_byte != 0)
                    {
                        unsafe
                        {
                            int byteCount = text.length;
                            byteCount = Math.Clamp(byteCount, 0, 4096);
                            var p = (byte*)text.@string.multi_byte;
                            Span<byte> bytes = new(p, byteCount);
                            replacement = System.Text.Encoding.UTF8.GetString(bytes);
                        }
                    }
                }
            }

            backend.ImePreeditDraw(draw.chg_first, draw.chg_length, replacement);
        }
        catch
        {
        }

        return 0;
    }

    private static int PreeditDone(nint ic, nint clientData, nint callData)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(clientData);
            if (handle.Target is X11WindowBackend backend)
            {
                backend.ImePreeditDone();
            }
        }
        catch
        {
        }

        return 0;
    }

    internal static bool TryCreateInputContext(
        nint display,
        nint window,
        X11WindowBackend owner,
        out nint im,
        out nint ic,
        out GCHandle ownerHandle,
        out nint preeditStartCallback,
        out nint preeditDrawCallback,
        out nint preeditDoneCallback,
        out bool hasPreeditCallbacks)
    {
        im = 0;
        ic = 0;
        ownerHandle = default;
        preeditStartCallback = 0;
        preeditDrawCallback = 0;
        preeditDoneCallback = 0;
        hasPreeditCallbacks = false;

        if (display == 0 || window == 0)
        {
            return false;
        }

        // Enable locale modifiers (best-effort). The caller is responsible for configuring the process locale.
        try
        {
            _ = NativeX11.XSetLocaleModifiers(string.Empty);
        }
        catch
        {
        }

        im = NativeX11.XOpenIM(display, 0, 0, 0);
        if (im == 0)
        {
            return false;
        }

        if (!NativeLibrary.TryLoad("libX11.so.6", out var lib))
        {
            NativeX11.XCloseIM(im);
            im = 0;
            return false;
        }

        try
        {
            if (!NativeLibrary.TryGetExport(lib, "XCreateIC", out var pCreateIc) || pCreateIc == 0)
            {
                NativeX11.XCloseIM(im);
                im = 0;
                return false;
            }

            var createIc3 = Marshal.GetDelegateForFunctionPointer<XCreateIC_3Pairs_Delegate>(pCreateIc);
            var createIc4 = Marshal.GetDelegateForFunctionPointer<XCreateIC_4Pairs_Delegate>(pCreateIc);

            NativeLibrary.TryGetExport(lib, "XVaCreateNestedList", out var pNestedList);
            XVaCreateNestedList_3Pairs_Delegate? createNestedList = null;
            if (pNestedList != 0)
            {
                createNestedList = Marshal.GetDelegateForFunctionPointer<XVaCreateNestedList_3Pairs_Delegate>(pNestedList);
            }

            nint nInputStyle = 0;
            nint nClientWindow = 0;
            nint nFocusWindow = 0;
            nint nPreeditAttributes = 0;
            nint nPreeditStartCallback = 0;
            nint nPreeditDrawCallback = 0;
            nint nPreeditDoneCallback = 0;
            try
            {
                nInputStyle = Marshal.StringToCoTaskMemUTF8("inputStyle");
                nClientWindow = Marshal.StringToCoTaskMemUTF8("clientWindow");
                nFocusWindow = Marshal.StringToCoTaskMemUTF8("focusWindow");

                // Prefer preedit callbacks if possible; fall back to "nothing" (commit-only) otherwise.
                if (createNestedList != null)
                {
                    nPreeditAttributes = Marshal.StringToCoTaskMemUTF8("preeditAttributes");
                    nPreeditStartCallback = Marshal.StringToCoTaskMemUTF8("preeditStartCallback");
                    nPreeditDrawCallback = Marshal.StringToCoTaskMemUTF8("preeditDrawCallback");
                    nPreeditDoneCallback = Marshal.StringToCoTaskMemUTF8("preeditDoneCallback");

                    ownerHandle = GCHandle.Alloc(owner);
                    nint ownerPtr = GCHandle.ToIntPtr(ownerHandle);

                    preeditStartCallback = Marshal.AllocHGlobal(Marshal.SizeOf<XIMCallback>());
                    preeditDrawCallback = Marshal.AllocHGlobal(Marshal.SizeOf<XIMCallback>());
                    preeditDoneCallback = Marshal.AllocHGlobal(Marshal.SizeOf<XIMCallback>());

                    Marshal.StructureToPtr(new XIMCallback { callback = s_preeditStartPtr, client_data = ownerPtr }, preeditStartCallback, fDeleteOld: false);
                    Marshal.StructureToPtr(new XIMCallback { callback = s_preeditDrawPtr, client_data = ownerPtr }, preeditDrawCallback, fDeleteOld: false);
                    Marshal.StructureToPtr(new XIMCallback { callback = s_preeditDonePtr, client_data = ownerPtr }, preeditDoneCallback, fDeleteOld: false);

                    nint nested = createNestedList(
                        0,
                        nPreeditStartCallback, preeditStartCallback,
                        nPreeditDrawCallback, preeditDrawCallback,
                        nPreeditDoneCallback, preeditDoneCallback,
                        0);

                    if (nested != 0)
                    {
                        long style = XIMPreeditCallbacks | XIMStatusNothing;
                        ic = createIc4(
                            im,
                            nInputStyle, (nint)style,
                            nClientWindow, window,
                            nFocusWindow, window,
                            nPreeditAttributes, nested,
                            0);

                        try { NativeX11.XFree(nested); } catch { }
                    }
                }

                if (ic == 0)
                {
                    // Fall back to commit-only input.
                    if (ownerHandle.IsAllocated)
                    {
                        ownerHandle.Free();
                    }
                    if (preeditStartCallback != 0) Marshal.FreeHGlobal(preeditStartCallback);
                    if (preeditDrawCallback != 0) Marshal.FreeHGlobal(preeditDrawCallback);
                    if (preeditDoneCallback != 0) Marshal.FreeHGlobal(preeditDoneCallback);
                    ownerHandle = default;
                    preeditStartCallback = 0;
                    preeditDrawCallback = 0;
                    preeditDoneCallback = 0;

                    long style = XIMPreeditNothing | XIMStatusNothing;
                    ic = createIc3(
                        im,
                        nInputStyle, (nint)style,
                        nClientWindow, window,
                        nFocusWindow, window,
                        0);
                }
                else
                {
                    hasPreeditCallbacks = true;
                }
            }
            finally
            {
                if (nInputStyle != 0) Marshal.FreeCoTaskMem(nInputStyle);
                if (nClientWindow != 0) Marshal.FreeCoTaskMem(nClientWindow);
                if (nFocusWindow != 0) Marshal.FreeCoTaskMem(nFocusWindow);
                if (nPreeditAttributes != 0) Marshal.FreeCoTaskMem(nPreeditAttributes);
                if (nPreeditStartCallback != 0) Marshal.FreeCoTaskMem(nPreeditStartCallback);
                if (nPreeditDrawCallback != 0) Marshal.FreeCoTaskMem(nPreeditDrawCallback);
                if (nPreeditDoneCallback != 0) Marshal.FreeCoTaskMem(nPreeditDoneCallback);
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
            NativeLibrary.Free(lib);
        }
    }
}
