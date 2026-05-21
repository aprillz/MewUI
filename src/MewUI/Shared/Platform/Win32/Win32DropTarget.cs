using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Platform.Win32;

#pragma warning disable CS0649 // Assigned by native COM (lpVtbl, instanceSlot)

/// <summary>
/// COM-callable wrapper exposing an <c>IDropTarget</c> implementation to OLE.
/// One instance is allocated per-window in unmanaged memory; the static vtable and entry points are shared.
/// </summary>
internal static unsafe class Win32DropTarget
{
    // Layout of the unmanaged object handed to OLE:
    //   [ +0  ] lpVtbl     — pointer to the shared vtable
    //   [ +ptr] refCount   — 4 bytes, padded
    //   [ +ptr+8] gcHandle — GCHandle of the managed adapter (IntPtr-sized)
    [StructLayout(LayoutKind.Sequential)]
    private struct Object
    {
        public nint lpVtbl;
        public int refCount;
        public int _pad;
        public nint gcHandle;
    }

    // The vtable: 7 slots (QueryInterface, AddRef, Release, DragEnter, DragOver, DragLeave, Drop).
    private static nint _sharedVTable;

    // IID_IDropTarget (00000122-0000-0000-C000-000000000046)
    private static readonly Guid IID_IDropTarget = new(0x00000122, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);

    private static readonly Guid IID_IUnknown = new(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);

    /// <summary>
    /// Creates an unmanaged COM-callable IDropTarget pointer for the given managed adapter.
    /// Caller passes the result to <c>RegisterDragDrop</c> and later disposes via <see cref="Release"/>.
    /// </summary>
    public static nint Create(Win32DropTargetAdapter adapter)
    {
        EnsureVTable();
        var handle = GCHandle.Alloc(adapter);

        var ptr = (Object*)NativeMemory.Alloc((nuint)sizeof(Object));
        ptr->lpVtbl = _sharedVTable;
        ptr->refCount = 1;
        ptr->_pad = 0;
        ptr->gcHandle = GCHandle.ToIntPtr(handle);
        return (nint)ptr;
    }

    /// <summary>Releases an instance previously returned by <see cref="Create"/>.</summary>
    public static void Release(nint instance)
    {
        if (instance == 0) return;
        var ptr = (Object*)instance;
        if (ptr->gcHandle != 0)
        {
            var handle = GCHandle.FromIntPtr(ptr->gcHandle);
            if (handle.IsAllocated) handle.Free();
            ptr->gcHandle = 0;
        }
        NativeMemory.Free(ptr);
    }

    private static void EnsureVTable()
    {
        if (_sharedVTable != 0) return;

        var vtable = (nint*)NativeMemory.Alloc((nuint)(sizeof(nint) * 7));
        vtable[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        vtable[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        vtable[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&ReleaseRef;
        vtable[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint, POINTL, uint*, int>)&DragEnter;
        vtable[4] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, POINTL, uint*, int>)&DragOver;
        vtable[5] = (nint)(delegate* unmanaged[Stdcall]<nint, int>)&DragLeave;
        vtable[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint, POINTL, uint*, int>)&Drop;

        _sharedVTable = (nint)vtable;
    }

    private static Win32DropTargetAdapter? AdapterFor(nint pThis)
    {
        var ptr = (Object*)pThis;
        if (ptr == null || ptr->gcHandle == 0) return null;
        var handle = GCHandle.FromIntPtr(ptr->gcHandle);
        return handle.IsAllocated ? handle.Target as Win32DropTargetAdapter : null;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int QueryInterface(nint pThis, Guid* riid, nint* ppvObject)
    {
        if (riid == null || ppvObject == null) return Ole32.E_NOINTERFACE;
        if (*riid == IID_IDropTarget || *riid == IID_IUnknown)
        {
            *ppvObject = pThis;
            // Increment ref count directly — UnmanagedCallersOnly methods cannot be invoked from managed code.
            var ptr = (Object*)pThis;
            System.Threading.Interlocked.Increment(ref ptr->refCount);
            return Ole32.S_OK;
        }
        *ppvObject = 0;
        return Ole32.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static uint AddRef(nint pThis)
    {
        var ptr = (Object*)pThis;
        return (uint)System.Threading.Interlocked.Increment(ref ptr->refCount);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static uint ReleaseRef(nint pThis)
    {
        var ptr = (Object*)pThis;
        var remaining = System.Threading.Interlocked.Decrement(ref ptr->refCount);
        if (remaining <= 0)
        {
            Release(pThis);
            return 0;
        }
        return (uint)remaining;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int DragEnter(nint pThis, nint pDataObj, uint grfKeyState, POINTL pt, uint* pdwEffect)
    {
        var adapter = AdapterFor(pThis);
        if (adapter == null || pdwEffect == null) return Ole32.S_OK;

        var requested = *pdwEffect;
        var effect = adapter.OnDragEnter(pDataObj, pt, requested);
        *pdwEffect = effect;
        return Ole32.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int DragOver(nint pThis, uint grfKeyState, POINTL pt, uint* pdwEffect)
    {
        var adapter = AdapterFor(pThis);
        if (adapter == null || pdwEffect == null) return Ole32.S_OK;

        var requested = *pdwEffect;
        var effect = adapter.OnDragOver(pt, requested);
        *pdwEffect = effect;
        return Ole32.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int DragLeave(nint pThis)
    {
        AdapterFor(pThis)?.OnDragLeave();
        return Ole32.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int Drop(nint pThis, nint pDataObj, uint grfKeyState, POINTL pt, uint* pdwEffect)
    {
        var adapter = AdapterFor(pThis);
        if (adapter == null || pdwEffect == null) return Ole32.S_OK;

        var requested = *pdwEffect;
        var effect = adapter.OnDrop(pDataObj, pt, requested);
        *pdwEffect = effect;
        return Ole32.S_OK;
    }
}

/// <summary>
/// Managed glue that converts OLE drag-and-drop callbacks into <see cref="WindowDragDropRouter"/> events.
/// One instance per <see cref="Win32WindowBackend"/>; held alive by a GCHandle stored in the unmanaged IDropTarget object.
/// </summary>
internal sealed unsafe class Win32DropTargetAdapter
{
    // IDropTargetHelper vtable slots (after IUnknown):
    //   3 = DragEnter(HWND, IDataObject*, POINT*, DWORD)
    //   4 = DragLeave()
    //   5 = DragOver(POINT*, DWORD)
    //   6 = Drop(IDataObject*, POINT*, DWORD)
    //   7 = Show(BOOL)
    private const int HelperDragEnterIndex = 3;

    private const int HelperDragLeaveIndex = 4;
    private const int HelperDragOverIndex = 5;
    private const int HelperDropIndex = 6;
    private const int IUnknownReleaseIndex = 2;

    private readonly Win32WindowBackend _backend;
    private nint _currentDataObject;
    private nint _dropTargetHelper;

    public Win32DropTargetAdapter(Win32WindowBackend backend)
    {
        _backend = backend;
        TryCreateDropTargetHelper();
    }

    private void TryCreateDropTargetHelper()
    {
        // The shell helper renders OS-native drag images (file icons, thumbnails) on top of our window
        // as the cursor moves. Falls back silently when unavailable — drag still works, just without preview.
        int hr = Ole32.CoCreateInstance(
            in Ole32.CLSID_DragDropHelper,
            0,
            Ole32.CLSCTX_INPROC_SERVER,
            in Ole32.IID_IDropTargetHelper,
            out var helper);
        if (hr == Ole32.S_OK && helper != 0)
        {
            _dropTargetHelper = helper;
        }
    }

    public void ReleaseHelper()
    {
        if (_dropTargetHelper == 0) return;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)((nint*)*(void**)_dropTargetHelper)[IUnknownReleaseIndex];
        _ = release(_dropTargetHelper);
        _dropTargetHelper = 0;
    }

    public uint OnDragEnter(nint pDataObj, POINTL ptScreen, uint requestedEffect)
    {
        _currentDataObject = pDataObj;
        var args = BuildArgs(pDataObj, ptScreen, requestedEffect, materializePayload: false);
        WindowDragDropRouter.OnExternalDragEnter(_backend.Window, args);
        var effect = ToDropEffect(args);
        HelperDragEnter(pDataObj, ptScreen, effect);
        return effect;
    }

    public uint OnDragOver(POINTL ptScreen, uint requestedEffect)
    {
        if (_currentDataObject == 0) return Ole32.DROPEFFECT_NONE;
        var args = BuildArgs(_currentDataObject, ptScreen, requestedEffect, materializePayload: false);
        WindowDragDropRouter.OnExternalDragOver(_backend.Window, args);
        var effect = ToDropEffect(args);
        HelperDragOver(ptScreen, effect);
        return effect;
    }

    public void OnDragLeave()
    {
        if (_currentDataObject == 0) return;
        HelperDragLeave();
        var args = BuildArgs(_currentDataObject, default, Ole32.DROPEFFECT_NONE, materializePayload: false);
        WindowDragDropRouter.OnExternalDragLeave(_backend.Window, args);
        _currentDataObject = 0;
    }

    public uint OnDrop(nint pDataObj, POINTL ptScreen, uint requestedEffect)
    {
        _currentDataObject = pDataObj;
        var args = BuildArgs(pDataObj, ptScreen, requestedEffect, materializePayload: true);
        var managedEffect = WindowDragDropRouter.OnExternalDrop(_backend.Window, args);
        _currentDataObject = 0;
        var effect = ToDropEffectFromManaged(managedEffect);
        HelperDrop(pDataObj, ptScreen, effect);
        return effect;
    }

    private void HelperDragEnter(nint pDataObj, POINTL pt, uint effect)
    {
        if (_dropTargetHelper == 0) return;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, nint, POINTL*, uint, int>)((nint*)*(void**)_dropTargetHelper)[HelperDragEnterIndex];
        _ = fn(_dropTargetHelper, _backend.Handle, pDataObj, &pt, effect);
    }

    private void HelperDragOver(POINTL pt, uint effect)
    {
        if (_dropTargetHelper == 0) return;
        var fn = (delegate* unmanaged[Stdcall]<nint, POINTL*, uint, int>)((nint*)*(void**)_dropTargetHelper)[HelperDragOverIndex];
        _ = fn(_dropTargetHelper, &pt, effect);
    }

    private void HelperDragLeave()
    {
        if (_dropTargetHelper == 0) return;
        var fn = (delegate* unmanaged[Stdcall]<nint, int>)((nint*)*(void**)_dropTargetHelper)[HelperDragLeaveIndex];
        _ = fn(_dropTargetHelper);
    }

    private void HelperDrop(nint pDataObj, POINTL pt, uint effect)
    {
        if (_dropTargetHelper == 0) return;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, POINTL*, uint, int>)((nint*)*(void**)_dropTargetHelper)[HelperDropIndex];
        _ = fn(_dropTargetHelper, pDataObj, &pt, effect);
    }

    private DragEventArgs BuildArgs(nint pDataObj, POINTL ptScreen, uint requestedEffect, bool materializePayload)
    {
        var window = _backend.Window;
        POINT clientPx = new() { x = ptScreen.x, y = ptScreen.y };
        User32.ScreenToClient(window.Handle, ref clientPx);

        double dpi = window.DpiScale;
        var clientDip = new Point(clientPx.x / dpi, clientPx.y / dpi);
        var screenPx = new Point(ptScreen.x, ptScreen.y);

        var data = Win32DataObjectAdapter.From(pDataObj, materializePayload);
        var allowed = FromDropEffect(requestedEffect);
        if (allowed == DragDropEffects.None)
        {
            // Some sources advertise None during DragEnter but want effect negotiation — let the target pick.
            allowed = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
        }

        return new DragEventArgs(data, clientDip, screenPx, allowed);
    }

    private static DragDropEffects FromDropEffect(uint dwEffect)
    {
        var result = DragDropEffects.None;
        if ((dwEffect & Ole32.DROPEFFECT_COPY) != 0) result |= DragDropEffects.Copy;
        if ((dwEffect & Ole32.DROPEFFECT_MOVE) != 0) result |= DragDropEffects.Move;
        if ((dwEffect & Ole32.DROPEFFECT_LINK) != 0) result |= DragDropEffects.Link;
        return result;
    }

    private static uint ToDropEffect(DragEventArgs args)
    {
        if (!args.Accepted) return Ole32.DROPEFFECT_NONE;
        return ToDropEffectFromManaged(args.Effect & args.AllowedEffects);
    }

    private static uint ToDropEffectFromManaged(DragDropEffects effects)
    {
        // Prefer one effect (most explorers/apps render only one). Order: Move > Copy > Link.
        if ((effects & DragDropEffects.Move) != 0) return Ole32.DROPEFFECT_MOVE;
        if ((effects & DragDropEffects.Copy) != 0) return Ole32.DROPEFFECT_COPY;
        if ((effects & DragDropEffects.Link) != 0) return Ole32.DROPEFFECT_LINK;
        return Ole32.DROPEFFECT_NONE;
    }
}

/// <summary>
/// Reads a Win32 <c>IDataObject*</c> COM pointer and converts the supported formats into a managed
/// <see cref="IDataObject"/> snapshot.
/// </summary>
internal static unsafe class Win32DataObjectAdapter
{
    // Vtable indices for IDataObject:
    //   3 = GetData, 4 = GetDataHere, 5 = QueryGetData, 6 = GetCanonicalFormatEtc, 7 = SetData, 8 = EnumFormatEtc
    private const int GetDataIndex = 3;

    private const int QueryGetDataIndex = 5;

    public static IDataObject From(nint pDataObj, bool materialize)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        if (pDataObj == 0) return new DataObject(data);

        // Probe what's available; if not materializing, register format keys only (empty payload).
        if (HasFormat(pDataObj, Ole32.CF_HDROP, Ole32.TYMED_HGLOBAL))
        {
            data[StandardDataFormats.StorageItems] = materialize
                ? (object?)ReadHDrop(pDataObj) ?? Array.Empty<string>()
                : Array.Empty<string>();
        }

        if (HasFormat(pDataObj, Ole32.CF_UNICODETEXT, Ole32.TYMED_HGLOBAL))
        {
            data[StandardDataFormats.Text] = materialize
                ? (object)(ReadUnicodeText(pDataObj) ?? string.Empty)
                : string.Empty;
        }
        else if (HasFormat(pDataObj, Ole32.CF_TEXT, Ole32.TYMED_HGLOBAL))
        {
            data[StandardDataFormats.Text] = materialize
                ? (object)(ReadAnsiText(pDataObj) ?? string.Empty)
                : string.Empty;
        }

        return new DataObject(data);
    }

    private static bool HasFormat(nint pDataObj, ushort cfFormat, uint tymed)
    {
        var fe = new FORMATETC
        {
            cfFormat = cfFormat,
            ptd = 0,
            dwAspect = Ole32.DVASPECT_CONTENT,
            lindex = -1,
            tymed = tymed,
        };
        var pUnk = (void**)pDataObj;
        var queryGetData = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, int>)((nint*)*pUnk)[QueryGetDataIndex];
        return queryGetData(pDataObj, &fe) == Ole32.S_OK;
    }

    private static bool TryGetData(nint pDataObj, ushort cfFormat, uint tymed, out STGMEDIUM medium)
    {
        medium = default;
        var fe = new FORMATETC
        {
            cfFormat = cfFormat,
            ptd = 0,
            dwAspect = Ole32.DVASPECT_CONTENT,
            lindex = -1,
            tymed = tymed,
        };
        var pUnk = (void**)pDataObj;
        var getData = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)((nint*)*pUnk)[GetDataIndex];

        STGMEDIUM tmp = default;
        int hr = getData(pDataObj, &fe, &tmp);
        if (hr != Ole32.S_OK)
        {
            return false;
        }
        medium = tmp;
        return true;
    }

    private static IReadOnlyList<string>? ReadHDrop(nint pDataObj)
    {
        if (!TryGetData(pDataObj, Ole32.CF_HDROP, Ole32.TYMED_HGLOBAL, out var medium)) return null;
        try
        {
            var hDrop = medium.unionMember;
            if (hDrop == 0) return null;
            return ExtractDropPaths(hDrop);
        }
        finally
        {
            Ole32.ReleaseStgMedium(ref medium);
        }
    }

    private static IReadOnlyList<string> ExtractDropPaths(nint hDrop)
    {
        uint count = Shell32.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
        if (count == 0) return Array.Empty<string>();

        var paths = new List<string>((int)count);
        for (uint i = 0; i < count; i++)
        {
            uint length = Shell32.DragQueryFile(hDrop, i, null, 0);
            if (length == 0) continue;

            char[] rented = ArrayPool<char>.Shared.Rent((int)length + 1);
            try
            {
                fixed (char* buffer = rented)
                {
                    _ = Shell32.DragQueryFile(hDrop, i, buffer, length + 1);
                    if (buffer[0] != '\0')
                    {
                        paths.Add(new string(buffer, 0, (int)length));
                    }
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented, clearArray: true);
            }
        }
        return paths;
    }

    private static string? ReadUnicodeText(nint pDataObj)
    {
        if (!TryGetData(pDataObj, Ole32.CF_UNICODETEXT, Ole32.TYMED_HGLOBAL, out var medium)) return null;
        try
        {
            var hGlobal = medium.unionMember;
            if (hGlobal == 0) return null;
            var ptr = Kernel32.GlobalLock(hGlobal);
            if (ptr == 0) return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Kernel32.GlobalUnlock(hGlobal);
            }
        }
        finally
        {
            Ole32.ReleaseStgMedium(ref medium);
        }
    }

    private static string? ReadAnsiText(nint pDataObj)
    {
        if (!TryGetData(pDataObj, Ole32.CF_TEXT, Ole32.TYMED_HGLOBAL, out var medium)) return null;
        try
        {
            var hGlobal = medium.unionMember;
            if (hGlobal == 0) return null;
            var ptr = Kernel32.GlobalLock(hGlobal);
            if (ptr == 0) return null;
            try
            {
                return Marshal.PtrToStringAnsi(ptr);
            }
            finally
            {
                Kernel32.GlobalUnlock(hGlobal);
            }
        }
        finally
        {
            Ole32.ReleaseStgMedium(ref medium);
        }
    }
}
