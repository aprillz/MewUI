using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aprillz.MewUI.Native.Com;

namespace Aprillz.MewUI.Native.DirectWrite;

/// <summary>
/// Managed implementation of IDWriteFontCollectionLoader + IDWriteFontFileEnumerator
/// for loading private font files into a DWrite custom font collection.
/// This allows CreateTextFormat to find fonts not in the system collection.
/// </summary>
internal static unsafe class DWritePrivateFontCollection
{
    // IID for IDWriteFontCollectionLoader: {CCA920E4-52F0-492B-BFA8-29C72EE0A468}
    private static readonly Guid IID_IDWriteFontCollectionLoader =
        new(0xCCA920E4, 0x52F0, 0x492B, 0xBF, 0xA8, 0x29, 0xC7, 0x2E, 0xE0, 0xA4, 0x68);

    // IID for IDWriteFontFileEnumerator: {72755049-5FF7-435D-8348-4BE97CFA6C7C}
    private static readonly Guid IID_IDWriteFontFileEnumerator =
        new(0x72755049, 0x5FF7, 0x435D, 0x83, 0x48, 0x4B, 0xE9, 0x7C, 0xFA, 0x6C, 0x7C);

    private static readonly Guid IID_IUnknown =
        new(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);

    // Registered font files: key (collectionKey bytes) → list of file paths
    private static readonly ConcurrentDictionary<int, string[]> s_registeredFiles = new();
    private static int s_nextKey = 1;

    // Prevent GC of the loader's vtable and delegate pointers
    private static nint s_loaderInstance;
    private static nint s_loaderVtbl;
    private static bool s_loaderRegistered;

    // Pin delegates to prevent GC
    private static readonly delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int> s_queryInterface = &LoaderQueryInterface;
    private static readonly delegate* unmanaged[Stdcall]<nint, uint> s_addRef = &LoaderAddRef;
    private static readonly delegate* unmanaged[Stdcall]<nint, uint> s_release = &LoaderRelease;
    private static readonly delegate* unmanaged[Stdcall]<nint, nint, void*, uint, nint*, int> s_createEnumerator = &LoaderCreateEnumerator;

    /// <summary>
    /// Creates a DWrite custom font collection containing the specified font files.
    /// The loader is registered/unregistered as needed.
    /// </summary>
    public static nint CreateCollection(IDWriteFactory* factory, string[] fontFilePaths)
    {
        if (fontFilePaths.Length == 0) return 0;

        EnsureLoaderRegistered(factory);

        // Assign a unique key for this set of files
        int key = Interlocked.Increment(ref s_nextKey);
        s_registeredFiles[key] = fontFilePaths;

        try
        {
            // IDWriteFactory::CreateCustomFontCollection (vtable index 4)
            // HRESULT CreateCustomFontCollection(IDWriteFontCollectionLoader*, void* collectionKey, UINT32 keySize, IDWriteFontCollection**)
            var fn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, nint, void*, uint, nint*, int>)factory->lpVtbl[4];
            nint collection = 0;
            int hr = fn(factory, s_loaderInstance, &key, (uint)sizeof(int), &collection);
            return hr >= 0 ? collection : 0;
        }
        finally
        {
            // Keep the key registered — DWrite may query it during the collection's lifetime
        }
    }

    /// <summary>
    /// Removes a key from the registered files. Call when the collection is released.
    /// </summary>
    public static void RemoveKey(int key) => s_registeredFiles.TryRemove(key, out _);

    private static void EnsureLoaderRegistered(IDWriteFactory* factory)
    {
        if (s_loaderRegistered) return;

        // Build vtable: [QueryInterface, AddRef, Release, CreateEnumeratorFromKey]
        var vtbl = (nint*)NativeMemory.AllocZeroed(4, (nuint)sizeof(nint));
        vtbl[0] = (nint)s_queryInterface;
        vtbl[1] = (nint)s_addRef;
        vtbl[2] = (nint)s_release;
        vtbl[3] = (nint)s_createEnumerator;
        s_loaderVtbl = (nint)vtbl;

        // Loader instance: just a pointer to the vtable pointer
        var instance = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));
        *instance = (nint)vtbl;
        s_loaderInstance = (nint)instance;

        // IDWriteFactory::RegisterFontCollectionLoader (vtable index 5)
        var regFn = (delegate* unmanaged[Stdcall]<IDWriteFactory*, nint, int>)factory->lpVtbl[5];
        int hr = regFn(factory, s_loaderInstance);
        s_loaderRegistered = hr >= 0;
    }

    // --- IDWriteFontCollectionLoader COM callbacks ---

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int LoaderQueryInterface(nint self, Guid* riid, nint* ppv)
    {
        if (*riid == IID_IUnknown || *riid == IID_IDWriteFontCollectionLoader)
        {
            *ppv = self;
            return 0; // S_OK
        }
        *ppv = 0;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint LoaderAddRef(nint self) => 1; // Static instance, no ref counting

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint LoaderRelease(nint self) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int LoaderCreateEnumerator(nint self, nint factory, void* collectionKey, uint collectionKeySize, nint* enumerator)
    {
        if (collectionKeySize != sizeof(int) || collectionKey == null)
        {
            *enumerator = 0;
            return unchecked((int)0x80070057); // E_INVALIDARG
        }

        int key = *(int*)collectionKey;
        if (!s_registeredFiles.TryGetValue(key, out var files))
        {
            *enumerator = 0;
            return unchecked((int)0x80070057);
        }

        *enumerator = FontFileEnumerator.Create((IDWriteFactory*)factory, files);
        return 0; // S_OK
    }

    /// <summary>
    /// Managed IDWriteFontFileEnumerator — iterates over a list of font file paths.
    /// </summary>
    private static class FontFileEnumerator
    {
        // Per-enumerator state stored in unmanaged memory
        private struct EnumeratorState
        {
            public nint Vtbl;
            public int RefCount;
            public int CurrentIndex;  // -1 = before first, 0..N-1 = current, N = past end
            public int FileCount;
            public nint Factory;      // IDWriteFactory*, not owned
            public nint CurrentFile;  // IDWriteFontFile*, owned (released on MoveNext/Release)
            // File paths stored separately via GCHandle
            public nint FilePathsHandle;
        }

        private static readonly delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int> _qi = &QI;
        private static readonly delegate* unmanaged[Stdcall]<nint, uint> _addRef = &AddRef;
        private static readonly delegate* unmanaged[Stdcall]<nint, uint> _release = &Release;
        private static readonly delegate* unmanaged[Stdcall]<nint, int*, int> _moveNext = &MoveNext;
        private static readonly delegate* unmanaged[Stdcall]<nint, nint*, int> _getCurrent = &GetCurrentFontFile;

        private static nint s_vtbl;

        public static nint Create(IDWriteFactory* factory, string[] filePaths)
        {
            if (s_vtbl == 0)
            {
                var vtbl = (nint*)NativeMemory.AllocZeroed(5, (nuint)sizeof(nint));
                vtbl[0] = (nint)_qi;
                vtbl[1] = (nint)_addRef;
                vtbl[2] = (nint)_release;
                vtbl[3] = (nint)_moveNext;
                vtbl[4] = (nint)_getCurrent;
                s_vtbl = (nint)vtbl;
            }

            var state = (EnumeratorState*)NativeMemory.AllocZeroed(1, (nuint)sizeof(EnumeratorState));
            state->Vtbl = s_vtbl;
            state->RefCount = 1;
            state->CurrentIndex = -1;
            state->FileCount = filePaths.Length;
            state->Factory = (nint)factory;
            state->CurrentFile = 0;

            // Pin the string array
            var handle = GCHandle.Alloc(filePaths);
            state->FilePathsHandle = GCHandle.ToIntPtr(handle);

            return (nint)state;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int QI(nint self, Guid* riid, nint* ppv)
        {
            if (*riid == IID_IUnknown || *riid == IID_IDWriteFontFileEnumerator)
            {
                *ppv = self;
                var state = (EnumeratorState*)self;
                Interlocked.Increment(ref state->RefCount);
                return 0;
            }
            *ppv = 0;
            return unchecked((int)0x80004002);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddRef(nint self)
        {
            var state = (EnumeratorState*)self;
            return (uint)Interlocked.Increment(ref state->RefCount);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint Release(nint self)
        {
            var state = (EnumeratorState*)self;
            int newRef = Interlocked.Decrement(ref state->RefCount);
            if (newRef <= 0)
            {
                if (state->CurrentFile != 0)
                    ComHelpers.Release(state->CurrentFile);
                if (state->FilePathsHandle != 0)
                    GCHandle.FromIntPtr(state->FilePathsHandle).Free();
                NativeMemory.Free(state);
            }
            return (uint)Math.Max(0, newRef);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int MoveNext(nint self, int* hasCurrentFile)
        {
            var state = (EnumeratorState*)self;

            // Release previous file
            if (state->CurrentFile != 0)
            {
                ComHelpers.Release(state->CurrentFile);
                state->CurrentFile = 0;
            }

            state->CurrentIndex++;
            if (state->CurrentIndex >= state->FileCount)
            {
                *hasCurrentFile = 0;
                return 0;
            }

            // Get file path from pinned array
            var handle = GCHandle.FromIntPtr(state->FilePathsHandle);
            var paths = (string[])handle.Target!;
            var path = paths[state->CurrentIndex];

            // Create font file reference
            var factory = (IDWriteFactory*)state->Factory;
            int hr = DWriteVTable.CreateFontFileReference(factory, path, out nint fontFile);
            if (hr < 0 || fontFile == 0)
            {
                *hasCurrentFile = 0;
                return 0; // Skip bad files, don't fail the enumeration
            }

            state->CurrentFile = fontFile;
            *hasCurrentFile = 1;
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetCurrentFontFile(nint self, nint* fontFile)
        {
            var state = (EnumeratorState*)self;
            if (state->CurrentFile == 0)
            {
                *fontFile = 0;
                return unchecked((int)0x80004005); // E_FAIL
            }

            // AddRef the font file before returning (caller will Release)
            var vtbl = *(nint**)state->CurrentFile;
            var addRef = (delegate* unmanaged[Stdcall]<nint, uint>)vtbl[1];
            addRef(state->CurrentFile);

            *fontFile = state->CurrentFile;
            return 0;
        }
    }
}
