namespace Aprillz.MewUI.Native;

/// <summary>
/// WGL_NV_DX_interop / WGL_NV_DX_interop2 entry points. These let an OpenGL context
/// share textures with a Direct3D 9/10/11 device without going through the CPU. Used
/// by the video sample to wrap a D3D11VA-decoded ID3D11Texture2D as a GL texture id
/// for NanoVG sampling.
/// </summary>
/// <remarks>
/// <para>
/// Function pointers are loaded lazily via <c>wglGetProcAddress</c>; if the driver
/// doesn't expose the extension (rare on modern desktop GPUs but possible on Intel
/// integrated drivers and remote-desktop sessions), <see cref="IsAvailable"/> returns
/// <see langword="false"/> and callers fall back to the PBO-readback path.
/// </para>
/// <para>
/// Lifecycle: <see cref="OpenDevice"/> per D3D device shared with GL, then per texture
/// <see cref="RegisterObject"/> → repeat <see cref="LockObject"/> /
/// <see cref="UnlockObject"/> around each frame's GL sample → <see cref="UnregisterObject"/>
/// at teardown → <see cref="CloseDevice"/> when no more textures use it.
/// </para>
/// </remarks>
internal static unsafe class WglDxInterop
{
    /// <summary>WGL_ACCESS_READ_ONLY_NV — the GL side only samples, never writes.</summary>
    public const uint WGL_ACCESS_READ_ONLY_NV = 0x00000000;
    public const uint WGL_ACCESS_READ_WRITE_NV = 0x00000001;
    public const uint WGL_ACCESS_WRITE_DISCARD_NV = 0x00000002;

    /// <summary>GL_TEXTURE_2D — pass to <see cref="RegisterObject"/> as <c>type</c>.</summary>
    public const uint GL_TEXTURE_2D = 0x0DE1;

    private static delegate* unmanaged[Stdcall]<nint, nint> _wglDXOpenDeviceNV;
    private static delegate* unmanaged[Stdcall]<nint, int> _wglDXCloseDeviceNV;
    private static delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, nint> _wglDXRegisterObjectNV;
    private static delegate* unmanaged[Stdcall]<nint, nint, int> _wglDXUnregisterObjectNV;
    private static delegate* unmanaged[Stdcall]<nint, int, nint*, int> _wglDXLockObjectsNV;
    private static delegate* unmanaged[Stdcall]<nint, int, nint*, int> _wglDXUnlockObjectsNV;
    private static bool _loaded;
    private static bool _available;

    /// <summary>
    /// Resolves the WGL_NV_DX_interop function pointers via <c>wglGetProcAddress</c>.
    /// Idempotent — safe to call multiple times. The current GL context must be
    /// active when this is invoked; <c>wglGetProcAddress</c> returns 0 with no
    /// active context.
    /// </summary>
    /// <returns><see langword="true"/> if every required entry point resolved.</returns>
    public static bool TryLoad()
    {
        if (_loaded) return _available;
        _loaded = true;

        _wglDXOpenDeviceNV = (delegate* unmanaged[Stdcall]<nint, nint>)
            OpenGL32.wglGetProcAddress("wglDXOpenDeviceNV");
        _wglDXCloseDeviceNV = (delegate* unmanaged[Stdcall]<nint, int>)
            OpenGL32.wglGetProcAddress("wglDXCloseDeviceNV");
        _wglDXRegisterObjectNV = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, nint>)
            OpenGL32.wglGetProcAddress("wglDXRegisterObjectNV");
        _wglDXUnregisterObjectNV = (delegate* unmanaged[Stdcall]<nint, nint, int>)
            OpenGL32.wglGetProcAddress("wglDXUnregisterObjectNV");
        _wglDXLockObjectsNV = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)
            OpenGL32.wglGetProcAddress("wglDXLockObjectsNV");
        _wglDXUnlockObjectsNV = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)
            OpenGL32.wglGetProcAddress("wglDXUnlockObjectsNV");

        _available = _wglDXOpenDeviceNV != null
            && _wglDXCloseDeviceNV != null
            && _wglDXRegisterObjectNV != null
            && _wglDXUnregisterObjectNV != null
            && _wglDXLockObjectsNV != null
            && _wglDXUnlockObjectsNV != null;
        return _available;
    }

    /// <summary>True when all WGL_NV_DX_interop entry points are loaded and usable.
    /// Call <see cref="TryLoad"/> first.</summary>
    public static bool IsAvailable => _available;

    /// <summary>Opens a handle to the supplied D3D device for GL interop. The returned
    /// handle is opaque — pass it to <see cref="RegisterObject"/> /
    /// <see cref="CloseDevice"/>. Returns 0 on failure (driver mismatch, device
    /// not D3D9/10/11 device, etc.).</summary>
    public static nint OpenDevice(nint d3dDevice) =>
        _wglDXOpenDeviceNV != null ? _wglDXOpenDeviceNV(d3dDevice) : 0;

    /// <summary>Closes a device handle from <see cref="OpenDevice"/>.</summary>
    public static int CloseDevice(nint deviceHandle) =>
        _wglDXCloseDeviceNV != null ? _wglDXCloseDeviceNV(deviceHandle) : 0;

    /// <summary>Registers a D3D texture with the GL side. <paramref name="glName"/>
    /// must be a previously-allocated GL texture object name (not yet bound to any
    /// data — register binds it). <paramref name="type"/> is typically
    /// <see cref="GL_TEXTURE_2D"/>. Returns an opaque handle for
    /// Lock/Unlock/Unregister calls; 0 on failure.</summary>
    public static nint RegisterObject(nint deviceHandle, nint dxObject, uint glName, uint type, uint access) =>
        _wglDXRegisterObjectNV != null ? _wglDXRegisterObjectNV(deviceHandle, dxObject, glName, type, access) : 0;

    /// <summary>Unregisters a previously-registered object handle.</summary>
    public static int UnregisterObject(nint deviceHandle, nint objectHandle) =>
        _wglDXUnregisterObjectNV != null ? _wglDXUnregisterObjectNV(deviceHandle, objectHandle) : 0;

    /// <summary>Locks one object for GL access. After this returns, the GL side may
    /// sample the texture; the D3D side MUST NOT modify it until <see cref="UnlockObject"/>.
    /// Use the single-object overload helper that wraps the count=1 call.</summary>
    public static int LockObject(nint deviceHandle, nint objectHandle)
    {
        if (_wglDXLockObjectsNV == null) return 0;
        nint local = objectHandle;
        return _wglDXLockObjectsNV(deviceHandle, 1, &local);
    }

    /// <summary>Unlocks one object previously locked via <see cref="LockObject"/>.
    /// After this returns, the D3D side may modify the texture again.</summary>
    public static int UnlockObject(nint deviceHandle, nint objectHandle)
    {
        if (_wglDXUnlockObjectsNV == null) return 0;
        nint local = objectHandle;
        return _wglDXUnlockObjectsNV(deviceHandle, 1, &local);
    }
}
