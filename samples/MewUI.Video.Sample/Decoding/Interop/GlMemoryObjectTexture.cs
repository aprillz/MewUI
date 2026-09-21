using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// Imports an NT-shared D3D11 RGBA texture into the current OpenGL context once through GL_EXT_memory_object_win32,
/// so sampling needs no per-frame interop lock. Reads are ordered against converter writes by <see cref="GlSharedSemaphores"/>.
/// </summary>
internal sealed unsafe class GlMemoryObjectTexture : IExternalRasterSource
{
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_TEXTURE_BINDING_2D = 0x8069;
    private const uint GL_RGBA8 = 0x8058;
    private const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    private const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    private const int GL_LINEAR = 0x2601;
    private const uint GL_DEDICATED_MEMORY_OBJECT_EXT = 0x9581;
    private const uint GL_HANDLE_TYPE_OPAQUE_WIN32_EXT = 0x9587;
    private const uint GL_HANDLE_TYPE_D3D11_IMAGE_EXT = 0x958B;
    private const uint GL_SYNC_GPU_COMMANDS_COMPLETE = 0x9117;
    private const uint GL_SYNC_FLUSH_COMMANDS_BIT = 0x1;
    private const uint GL_TIMEOUT_EXPIRED = 0x911B;
    private const ulong READ_FENCE_TIMEOUT_NANOSECONDS = 100_000_000;

    private static bool _loaded;
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _glCreateMemoryObjectsEXT;
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _glDeleteMemoryObjectsEXT;
    private static delegate* unmanaged[Stdcall]<uint, uint, int*, void> _glMemoryObjectParameterivEXT;
    private static delegate* unmanaged[Stdcall]<uint, ulong, uint, nint, void> _glImportMemoryWin32HandleEXT;
    private static delegate* unmanaged[Stdcall]<uint, int, uint, int, int, uint, ulong, void> _glTexStorageMem2DEXT;
    private static delegate* unmanaged[Stdcall]<uint, uint, nint> _glFenceSync;
    private static delegate* unmanaged[Stdcall]<nint, uint, ulong, uint> _glClientWaitSync;
    private static delegate* unmanaged[Stdcall]<nint, void> _glDeleteSync;
    private static int _readFenceTimeouts;

    // Index of the import attempt that last produced a usable texture, tried first for the next texture.
    private static int _workingAttempt = -1;
    private static bool _missingContextLogged;

    private readonly GlSharedSemaphores? _semaphores;
    private readonly nint _d3d11Texture;
    private uint _memoryObject;
    private uint _textureId;
    // Without GL semaphores: GL sync object placed after the latest frame that sampled the texture.
    private nint _readFence;
    private bool _disposed;

    /// <summary>
    /// Producer fence value of the conversion currently held by the texture; sampling waits for it.
    /// </summary>
    public ulong ProducedValue { get; set; }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int Version => 0;
    public RenderPixelFormat Format => RenderPixelFormat.Bgra8888;
    public BitmapAlphaMode AlphaMode => BitmapAlphaMode.Ignore;
    public bool YFlipped => false;
    public SurfaceCapabilities Capabilities =>
        SurfaceCapabilities.ExternalHandle |
        SurfaceCapabilities.ExternallySynchronized |
        SurfaceCapabilities.GpuSampleable;
    public IReadOnlyList<ExternalRasterPlane> Planes =>
    [
        new ExternalRasterPlane(0, (nint)_textureId, PixelWidth, PixelHeight, 0, Format)
    ];

    public static bool IsAvailable => TryLoad();

    public GlMemoryObjectTexture(nint sharedHandle, nint d3d11Texture, int pixelWidth, int pixelHeight, GlSharedSemaphores? semaphores)
    {
        if (sharedHandle == 0) throw new ArgumentException("Shared handle is 0.", nameof(sharedHandle));
        if (!TryLoad()) throw new InvalidOperationException("GL_EXT_memory_object_win32 entry points are unavailable.");

        _semaphores = semaphores;
        _d3d11Texture = d3d11Texture;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;

        ulong byteSize = (ulong)pixelWidth * (ulong)pixelHeight * 4;
        // The allocation behind a D3D11 texture is padded and its size cannot be queried; storage fails when the declared
        // size is smaller than the driver's layout (NVIDIA), and Intel rejects the D3D11_IMAGE handle type outright.
        (uint HandleType, ulong Size)[] attempts =
        [
            (GL_HANDLE_TYPE_D3D11_IMAGE_EXT, byteSize),
            (GL_HANDLE_TYPE_D3D11_IMAGE_EXT, byteSize * 2),
            (GL_HANDLE_TYPE_D3D11_IMAGE_EXT, 0),
            (GL_HANDLE_TYPE_OPAQUE_WIN32_EXT, byteSize),
            (GL_HANDLE_TYPE_OPAQUE_WIN32_EXT, byteSize * 2),
        ];

        int firstAttempt = _workingAttempt >= 0 ? _workingAttempt : 0;
        var attemptLog = new System.Text.StringBuilder();
        for (int index = firstAttempt; index < attempts.Length; index++)
        {
            var (handleType, size) = attempts[index];
            if (TryImport(sharedHandle, handleType, size, pixelWidth, pixelHeight, out uint importError, out uint storageError))
            {
                attemptLog.Append($" [type=0x{handleType:X} size={size} ok]");
                if (_workingAttempt != index)
                {
                    _workingAttempt = index;
                    Aprillz.MewUI.Video.Sample.Diagnostics.SampleLog.Write($"[memobj] import attempts:{attemptLog}");
                }

                return;
            }

            attemptLog.Append($" [type=0x{handleType:X} size={size} import=0x{importError:X} storage=0x{storageError:X}]");
        }

        Aprillz.MewUI.Video.Sample.Diagnostics.SampleLog.Write($"[memobj] import attempts:{attemptLog}");
        throw new InvalidOperationException("no memory object import attempt produced a usable texture.");
    }

    private bool TryImport(nint sharedHandle, uint handleType, ulong size, int pixelWidth, int pixelHeight, out uint importError, out uint storageError)
    {
        _ = DrainErrors();
        uint memoryObject = 0;
        _glCreateMemoryObjectsEXT(1, &memoryObject);
        int dedicated = 1;
        _glMemoryObjectParameterivEXT(memoryObject, GL_DEDICATED_MEMORY_OBJECT_EXT, &dedicated);
        _glImportMemoryWin32HandleEXT(memoryObject, size, handleType, sharedHandle);
        importError = glGetError();
        storageError = 0;
        if (importError != 0)
        {
            _glDeleteMemoryObjectsEXT(1, &memoryObject);
            return false;
        }

        int previousBinding = 0;
        glGetIntegerv(GL_TEXTURE_BINDING_2D, &previousBinding);
        uint textureId = 0;
        glGenTextures(1, &textureId);
        glBindTexture(GL_TEXTURE_2D, textureId);
        _glTexStorageMem2DEXT(GL_TEXTURE_2D, 1, GL_RGBA8, pixelWidth, pixelHeight, memoryObject, 0);
        storageError = glGetError();
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glBindTexture(GL_TEXTURE_2D, (uint)previousBinding);
        if (storageError != 0)
        {
            glDeleteTextures(1, &textureId);
            _glDeleteMemoryObjectsEXT(1, &memoryObject);
            return false;
        }

        _memoryObject = memoryObject;
        _textureId = textureId;
        return true;
    }

    public IExternalRasterLease Acquire()
    {
        _semaphores?.WaitProduced(ProducedValue, _textureId);
        return new Lease(this);
    }

    /// <summary>
    /// Blocks until GL has finished the commands that sampled this texture, so D3D11 may convert into it again.
    /// Needed only when GL semaphores are unavailable; call with the GL context current.
    /// </summary>
    public void WaitForReads()
    {
        if (_readFence == 0 || wglGetCurrentContext() == 0)
        {
            return;
        }

        uint result = _glClientWaitSync(_readFence, GL_SYNC_FLUSH_COMMANDS_BIT, READ_FENCE_TIMEOUT_NANOSECONDS);
        if (result == GL_TIMEOUT_EXPIRED && Interlocked.Increment(ref _readFenceTimeouts) == 1)
        {
            Aprillz.MewUI.Video.Sample.Diagnostics.SampleLog.Write("[memobj] GL read fence wait timed out; texture handed back unfenced.");
        }

        _glDeleteSync(_readFence);
        _readFence = 0;
    }

    private void SignalRead()
    {
        if (_disposed)
        {
            return;
        }

        if (_semaphores is null)
        {
            if (wglGetCurrentContext() != 0 && _glFenceSync != null)
            {
                if (_readFence != 0)
                {
                    _glDeleteSync(_readFence);
                }

                _readFence = _glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
            }

            return;
        }

        // Without a current context the signal would be dropped, and D3D11 would wait forever on a value never reached.
        if (wglGetCurrentContext() == 0)
        {
            if (!_missingContextLogged)
            {
                _missingContextLogged = true;
                Aprillz.MewUI.Video.Sample.Diagnostics.SampleLog.Write("[memobj] lease released without a current GL context; read not fenced.");
            }

            return;
        }

        ulong value = _semaphores.Fences.ReserveReadValue(_d3d11Texture);
        _semaphores.SignalConsumed(value, _textureId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_readFence != 0)
        {
            _glDeleteSync(_readFence);
            _readFence = 0;
        }

        if (_textureId != 0)
        {
            uint textureId = _textureId;
            glDeleteTextures(1, &textureId);
            _textureId = 0;
        }

        if (_memoryObject != 0)
        {
            uint memoryObject = _memoryObject;
            _glDeleteMemoryObjectsEXT(1, &memoryObject);
            _memoryObject = 0;
        }
    }

    private static uint DrainErrors()
    {
        uint first = 0;
        for (int index = 0; index < 16; index++)
        {
            uint error = glGetError();
            if (error == 0)
            {
                break;
            }

            if (first == 0)
            {
                first = error;
            }
        }

        return first;
    }

    private static bool TryLoad()
    {
        if (_loaded)
        {
            return _glTexStorageMem2DEXT != null;
        }

        _loaded = true;
        _glCreateMemoryObjectsEXT = (delegate* unmanaged[Stdcall]<int, uint*, void>)wglGetProcAddress("glCreateMemoryObjectsEXT");
        _glDeleteMemoryObjectsEXT = (delegate* unmanaged[Stdcall]<int, uint*, void>)wglGetProcAddress("glDeleteMemoryObjectsEXT");
        _glMemoryObjectParameterivEXT = (delegate* unmanaged[Stdcall]<uint, uint, int*, void>)wglGetProcAddress("glMemoryObjectParameterivEXT");
        _glImportMemoryWin32HandleEXT = (delegate* unmanaged[Stdcall]<uint, ulong, uint, nint, void>)wglGetProcAddress("glImportMemoryWin32HandleEXT");
        _glTexStorageMem2DEXT = (delegate* unmanaged[Stdcall]<uint, int, uint, int, int, uint, ulong, void>)wglGetProcAddress("glTexStorageMem2DEXT");
        _glFenceSync = (delegate* unmanaged[Stdcall]<uint, uint, nint>)wglGetProcAddress("glFenceSync");
        _glClientWaitSync = (delegate* unmanaged[Stdcall]<nint, uint, ulong, uint>)wglGetProcAddress("glClientWaitSync");
        _glDeleteSync = (delegate* unmanaged[Stdcall]<nint, void>)wglGetProcAddress("glDeleteSync");
        if (_glFenceSync == null || _glClientWaitSync == null || _glDeleteSync == null)
        {
            _glFenceSync = null;
        }

        bool available = _glCreateMemoryObjectsEXT != null
            && _glDeleteMemoryObjectsEXT != null
            && _glMemoryObjectParameterivEXT != null
            && _glImportMemoryWin32HandleEXT != null
            && _glTexStorageMem2DEXT != null;
        if (!available)
        {
            _glTexStorageMem2DEXT = null;
        }

        return available;
    }

    private sealed class Lease(GlMemoryObjectTexture owner) : IExternalRasterLease
    {
        public nint NativeHandle => (nint)owner._textureId;
        public nint NativeAlternateHandle => 0;
        public int PixelWidth => owner.PixelWidth;
        public int PixelHeight => owner.PixelHeight;
        public bool YFlipped => owner.YFlipped;

        public void Dispose() => owner.SignalRead();
    }

    [DllImport("opengl32.dll")]
    private static extern nint wglGetProcAddress(string name);

    [DllImport("opengl32.dll")]
    private static extern uint glGetError();

    [DllImport("opengl32.dll")]
    private static extern nint wglGetCurrentContext();

    [DllImport("opengl32.dll")]
    private static extern void glGetIntegerv(uint name, int* value);

    [DllImport("opengl32.dll")]
    private static extern void glGenTextures(int count, uint* textures);

    [DllImport("opengl32.dll")]
    private static extern void glDeleteTextures(int count, uint* textures);

    [DllImport("opengl32.dll")]
    private static extern void glBindTexture(uint target, uint texture);

    [DllImport("opengl32.dll")]
    private static extern void glTexParameteri(uint target, uint name, int value);
}
