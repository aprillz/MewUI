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
    //   [ +0  ] lpVtbl     - pointer to the shared vtable
    //   [ +ptr] refCount   - 4 bytes, padded
    //   [ +ptr+8] gcHandle - GCHandle of the managed adapter (IntPtr-sized)
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

    /// <summary>
    /// Drops the creator's reference from <see cref="Create"/>. The unmanaged object is freed only when
    /// the COM refcount reaches zero, so OLE consumers still holding AddRef'd references stay valid.
    /// </summary>
    public static void Release(nint instance)
    {
        if (instance == 0) return;
        var ptr = (Object*)instance;
        var remaining = System.Threading.Interlocked.Decrement(ref ptr->refCount);
        if (remaining <= 0)
        {
            Destroy(ptr);
        }
    }

    private static void Destroy(Object* ptr)
    {
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
            // Increment ref count directly - UnmanagedCallersOnly methods cannot be invoked from managed code.
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
            Destroy(ptr);
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
    private Win32DropDataObject? _currentData;
    private nint _dropTargetHelper;

    public Win32DropTargetAdapter(Win32WindowBackend backend)
    {
        _backend = backend;
        TryCreateDropTargetHelper();
    }

    private void TryCreateDropTargetHelper()
    {
        // The shell helper renders OS-native drag images (file icons, thumbnails) on top of our window
        // as the cursor moves. Falls back silently when unavailable - drag still works, just without preview.
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
        EndDrag();
        _currentData = new Win32DropDataObject(pDataObj);
        var args = BuildArgs(_currentData, ptScreen, requestedEffect);
        WindowDragDropRouter.OnExternalDragEnter(_backend.Window, args);
        var effect = ToDropEffect(args);
        HelperDragEnter(pDataObj, ptScreen, effect);
        return effect;
    }

    public uint OnDragOver(POINTL ptScreen, uint requestedEffect)
    {
        if (_currentData == null) return Ole32.DROPEFFECT_NONE;
        var args = BuildArgs(_currentData, ptScreen, requestedEffect);
        WindowDragDropRouter.OnExternalDragOver(_backend.Window, args);
        var effect = ToDropEffect(args);
        HelperDragOver(ptScreen, effect);
        return effect;
    }

    public void OnDragLeave()
    {
        if (_currentData == null) return;
        HelperDragLeave();
        var args = BuildArgs(_currentData, default, Ole32.DROPEFFECT_NONE);
        WindowDragDropRouter.OnExternalDragLeave(_backend.Window, args);
        EndDrag();
    }

    public uint OnDrop(nint pDataObj, POINTL ptScreen, uint requestedEffect)
    {
        _currentData ??= new Win32DropDataObject(pDataObj);
        var args = BuildArgs(_currentData, ptScreen, requestedEffect);
        var managedEffect = WindowDragDropRouter.OnExternalDrop(_backend.Window, args);
        EndDrag();
        var effect = ToDropEffectFromManaged(managedEffect);
        HelperDrop(pDataObj, ptScreen, effect);
        return effect;
    }

    private void EndDrag()
    {
        _currentData?.Close();
        _currentData = null;
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

    private DragEventArgs BuildArgs(IDataObject data, POINTL ptScreen, uint requestedEffect)
    {
        var window = _backend.Window;
        POINT clientPx = new() { x = ptScreen.x, y = ptScreen.y };
        User32.ScreenToClient(window.Handle, ref clientPx);

        double dpi = window.DpiScale;
        var clientDip = new Point(clientPx.x / dpi, clientPx.y / dpi);
        var screenPx = new Point(ptScreen.x, ptScreen.y);

        var allowed = FromDropEffect(requestedEffect);
        if (allowed == DragDropEffects.None)
        {
            // Some sources advertise None during DragEnter but want effect negotiation - let the target pick.
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
/// The data of one OLE drag: every format the source offers in global memory, listed by its clipboard format name,
/// plus the standard formats derived from file drops, text and links. Values are read from the source on request.
/// </summary>
internal sealed unsafe class Win32DropDataObject : PlatformDataObject
{
    // Vtable indices for IDataObject:
    //   0 = QueryInterface, 1 = AddRef, 2 = Release,
    //   3 = GetData, 4 = GetDataHere, 5 = QueryGetData, 6 = GetCanonicalFormatEtc, 7 = SetData, 8 = EnumFormatEtc
    private const int ADD_REF_INDEX = 1;
    private const int RELEASE_INDEX = 2;
    private const int GET_DATA_INDEX = 3;
    private const int QUERY_GET_DATA_INDEX = 5;
    private const int ENUM_FORMAT_ETC_INDEX = 8;

    // IEnumFORMATETC::Next.
    private const int ENUM_NEXT_INDEX = 3;
    private const uint DATADIR_GET = 1;

    private const string FILES_FORMAT = "CF_HDROP";
    private const string UNICODE_TEXT_FORMAT = "CF_UNICODETEXT";
    private const string ANSI_TEXT_FORMAT = "CF_TEXT";

    // Links dragged from browsers and shortcuts.
    private const string URL_FORMAT = "UniformResourceLocatorW";
    private const string ANSI_URL_FORMAT = "UniformResourceLocator";

    private static readonly string?[] _predefinedNames =
    [
        null, "CF_TEXT", "CF_BITMAP", "CF_METAFILEPICT", "CF_SYLK", "CF_DIF", "CF_TIFF", "CF_OEMTEXT", "CF_DIB",
        "CF_PALETTE", "CF_PENDATA", "CF_RIFF", "CF_WAVE", "CF_UNICODETEXT", "CF_ENHMETAFILE", "CF_HDROP", "CF_LOCALE",
        "CF_DIBV5",
    ];

    private readonly Dictionary<string, ushort> _platformFormats = new(StringComparer.Ordinal);
    private nint _dataObject;

    public Win32DropDataObject(nint dataObject)
    {
        if (dataObject == 0)
        {
            return;
        }

        _dataObject = dataObject;
        _ = ((delegate* unmanaged[Stdcall]<nint, uint>)VTable(dataObject)[ADD_REF_INDEX])(dataObject);

        if (!EnumerateFormats())
        {
            // A source without a format enumerator is still probed for the formats the standard ones come from.
            foreach (ushort format in new[] { Ole32.CF_HDROP, Ole32.CF_UNICODETEXT, Ole32.CF_TEXT })
            {
                if (HasFormat(format))
                {
                    _platformFormats.TryAdd(NameOf(format)!, format);
                }
            }
        }

        bool hasFiles = _platformFormats.ContainsKey(FILES_FORMAT);
        bool hasLink = _platformFormats.ContainsKey(URL_FORMAT) || _platformFormats.ContainsKey(ANSI_URL_FORMAT);
        if (hasFiles)
        {
            AddFormat(StandardDataFormats.StorageItems);
        }

        if (hasFiles || hasLink)
        {
            AddFormat(StandardDataFormats.Uris);
        }

        if (_platformFormats.ContainsKey(UNICODE_TEXT_FORMAT) || _platformFormats.ContainsKey(ANSI_TEXT_FORMAT))
        {
            AddFormat(StandardDataFormats.Text);
        }

        foreach (var name in _platformFormats.Keys)
        {
            AddFormat(name);
        }
    }

    protected override object? Read(string format)
    {
        if (_dataObject == 0)
        {
            return null;
        }

        switch (format)
        {
            case StandardDataFormats.StorageItems:
                return ReadFiles();

            case StandardDataFormats.Uris:
                return ReadUris();

            case StandardDataFormats.Text:
                return ReadText();

            default:
                return _platformFormats.TryGetValue(format, out var id) ? ReadBytes(id) : null;
        }
    }

    protected override void OnClosed()
    {
        if (_dataObject != 0)
        {
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)VTable(_dataObject)[RELEASE_INDEX])(_dataObject);
            _dataObject = 0;
        }
    }

    private static nint* VTable(nint comObject) => *(nint**)comObject;

    private bool EnumerateFormats()
    {
        nint enumerator = 0;
        var enumFormatEtc = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)VTable(_dataObject)[ENUM_FORMAT_ETC_INDEX];
        if (enumFormatEtc(_dataObject, DATADIR_GET, &enumerator) != Ole32.S_OK || enumerator == 0)
        {
            return false;
        }

        try
        {
            var next = (delegate* unmanaged[Stdcall]<nint, uint, FORMATETC*, uint*, int>)VTable(enumerator)[ENUM_NEXT_INDEX];
            FORMATETC entry;
            uint fetched;
            while (next(enumerator, 1, &entry, &fetched) == Ole32.S_OK && fetched == 1)
            {
                if (entry.ptd != 0)
                {
                    Marshal.FreeCoTaskMem(entry.ptd);
                }

                // Only whole-content global memory is read here; streams and per-item contents are not listed.
                if ((entry.tymed & Ole32.TYMED_HGLOBAL) == 0 || entry.dwAspect != Ole32.DVASPECT_CONTENT || entry.lindex != -1)
                {
                    continue;
                }

                if (NameOf(entry.cfFormat) is string name)
                {
                    _platformFormats.TryAdd(name, entry.cfFormat);
                }
            }

            return true;
        }
        finally
        {
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)VTable(enumerator)[RELEASE_INDEX])(enumerator);
        }
    }

    private static string? NameOf(ushort format)
    {
        if (format < _predefinedNames.Length)
        {
            return _predefinedNames[format];
        }

        char* buffer = stackalloc char[256];
        int length = User32.GetClipboardFormatName(format, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    private bool HasFormat(ushort format)
    {
        var request = Request(format);
        var queryGetData = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, int>)VTable(_dataObject)[QUERY_GET_DATA_INDEX];
        return queryGetData(_dataObject, &request) == Ole32.S_OK;
    }

    private static FORMATETC Request(ushort format) => new()
    {
        cfFormat = format,
        ptd = 0,
        dwAspect = Ole32.DVASPECT_CONTENT,
        lindex = -1,
        tymed = Ole32.TYMED_HGLOBAL,
    };

    /// <summary>Calls <paramref name="read"/> with the locked global memory of <paramref name="format"/>, or returns null.</summary>
    private TResult? WithGlobal<TResult>(ushort format, Func<nint, nint, TResult?> read) where TResult : class
    {
        var request = Request(format);
        STGMEDIUM medium = default;
        var getData = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)VTable(_dataObject)[GET_DATA_INDEX];
        if (getData(_dataObject, &request, &medium) != Ole32.S_OK)
        {
            return null;
        }

        try
        {
            if (medium.tymed != Ole32.TYMED_HGLOBAL || medium.unionMember == 0)
            {
                return null;
            }

            var pointer = Kernel32.GlobalLock(medium.unionMember);
            if (pointer == 0)
            {
                return null;
            }

            try
            {
                return read(medium.unionMember, pointer);
            }
            finally
            {
                Kernel32.GlobalUnlock(medium.unionMember);
            }
        }
        finally
        {
            Ole32.ReleaseStgMedium(ref medium);
        }
    }

    private byte[]? ReadBytes(ushort format)
        => WithGlobal(format, (global, pointer) => new ReadOnlySpan<byte>((void*)pointer, checked((int)Kernel32.GlobalSize(global))).ToArray());

    private string? ReadString(ushort format, bool unicode)
        => WithGlobal(format, (_, pointer) => unicode ? Marshal.PtrToStringUni(pointer) : Marshal.PtrToStringAnsi(pointer));

    private string? ReadText()
    {
        if (_platformFormats.TryGetValue(UNICODE_TEXT_FORMAT, out var unicode))
        {
            return ReadString(unicode, unicode: true);
        }
        else
        {
            return _platformFormats.TryGetValue(ANSI_TEXT_FORMAT, out var ansi) ? ReadString(ansi, unicode: false) : null;
        }
    }

    private IReadOnlyList<string>? ReadFiles()
        => _platformFormats.TryGetValue(FILES_FORMAT, out var files) ? WithGlobal(files, (global, _) => ExtractDropPaths(global)) : null;

    private IReadOnlyList<string> ReadUris()
    {
        var uris = new List<string>();
        foreach (var path in ReadFiles() ?? [])
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                uris.Add(uri.AbsoluteUri);
            }
        }

        string? link = null;
        if (_platformFormats.TryGetValue(URL_FORMAT, out var unicodeLink))
        {
            link = ReadString(unicodeLink, unicode: true);
        }
        else if (_platformFormats.TryGetValue(ANSI_URL_FORMAT, out var ansiLink))
        {
            link = ReadString(ansiLink, unicode: false);
        }

        if (!string.IsNullOrWhiteSpace(link) && !uris.Contains(link, StringComparer.Ordinal))
        {
            uris.Add(link);
        }

        return uris;
    }

    private static IReadOnlyList<string> ExtractDropPaths(nint hDrop)
    {
        uint count = Shell32.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
        if (count == 0) return Array.Empty<string>();

        var paths = new List<string>((int)count);
        for (uint index = 0; index < count; index++)
        {
            uint length = Shell32.DragQueryFile(hDrop, index, null, 0);
            if (length == 0) continue;

            char[] rented = ArrayPool<char>.Shared.Rent((int)length + 1);
            try
            {
                fixed (char* buffer = rented)
                {
                    _ = Shell32.DragQueryFile(hDrop, index, buffer, length + 1);
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
}
