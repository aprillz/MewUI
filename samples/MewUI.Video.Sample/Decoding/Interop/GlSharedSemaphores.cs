using System.Runtime.InteropServices;

using Aprillz.MewUI.Video.Sample.Diagnostics;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// GL-side view of <see cref="SharedTextureFences"/>: both fences imported as GL_EXT_semaphore_win32 semaphores in the
/// current context's share group.
/// </summary>
internal sealed unsafe class GlSharedSemaphores : IDisposable
{
    private const uint GL_HANDLE_TYPE_OPAQUE_WIN32_EXT = 0x9587;
    private const uint GL_HANDLE_TYPE_D3D12_FENCE_EXT = 0x9594;
    private const uint GL_D3D12_FENCE_VALUE_EXT = 0x9595;
    private const uint GL_LAYOUT_SHADER_READ_ONLY_EXT = 0x9591;

    private static bool _loaded;
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _glGenSemaphoresEXT;
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _glDeleteSemaphoresEXT;
    private static delegate* unmanaged[Stdcall]<uint, uint, nint, void> _glImportSemaphoreWin32HandleEXT;
    private static delegate* unmanaged[Stdcall]<uint, uint, ulong*, void> _glSemaphoreParameterui64vEXT;
    private static delegate* unmanaged[Stdcall]<uint, uint, uint*, uint, uint*, uint*, void> _glWaitSemaphoreEXT;
    private static delegate* unmanaged[Stdcall]<uint, uint, uint*, uint, uint*, uint*, void> _glSignalSemaphoreEXT;

    private uint _producerSemaphore;
    private uint _consumerSemaphore;
    private bool _disposed;

    private GlSharedSemaphores(SharedTextureFences fences, uint producerSemaphore, uint consumerSemaphore)
    {
        Fences = fences;
        _producerSemaphore = producerSemaphore;
        _consumerSemaphore = consumerSemaphore;
    }

    public SharedTextureFences Fences { get; }

    /// <summary>
    /// Imports both fences into the current GL context, or returns null when the extension or the import is unavailable.
    /// </summary>
    public static GlSharedSemaphores? TryCreate(SharedTextureFences fences)
    {
        if (!TryLoad())
        {
            SampleLog.Write("[fence] GL_EXT_semaphore_win32 entry points unavailable.");
            return null;
        }

        // D3D12_FENCE is the handle type a shared D3D11 fence maps to; try the opaque NT handle type when a driver rejects it.
        foreach (uint handleType in new[] { GL_HANDLE_TYPE_D3D12_FENCE_EXT, GL_HANDLE_TYPE_OPAQUE_WIN32_EXT })
        {
            _ = glGetError();
            uint producer = ImportFence(fences.ProducerFenceHandle, handleType);
            uint producerError = glGetError();
            uint consumer = ImportFence(fences.ConsumerFenceHandle, handleType);
            uint consumerError = glGetError();
            if (producerError == 0 && consumerError == 0)
            {
                SampleLog.Write($"[fence] GL semaphores imported (handle type 0x{handleType:X}): producer={producer} consumer={consumer}");
                return new GlSharedSemaphores(fences, producer, consumer);
            }

            SampleLog.Write($"[fence] GL semaphore import failed (handle type 0x{handleType:X}): producer glError=0x{producerError:X}, consumer glError=0x{consumerError:X}.");
            DeleteSemaphore(producer);
            DeleteSemaphore(consumer);
        }

        return null;
    }

    /// <summary>
    /// Makes later GL commands that sample <paramref name="glTexture"/> wait until the producer fence reaches <paramref name="value"/>.
    /// </summary>
    public void WaitProduced(ulong value, uint glTexture)
    {
        if (_disposed || value == 0)
        {
            return;
        }

        _glSemaphoreParameterui64vEXT(_producerSemaphore, GL_D3D12_FENCE_VALUE_EXT, &value);
        uint layout = GL_LAYOUT_SHADER_READ_ONLY_EXT;
        _glWaitSemaphoreEXT(_producerSemaphore, 0, null, 1, &glTexture, &layout);
    }

    /// <summary>
    /// Signals the consumer fence to <paramref name="value"/> once GL commands issued so far have read <paramref name="glTexture"/>.
    /// </summary>
    public void SignalConsumed(ulong value, uint glTexture)
    {
        if (_disposed)
        {
            return;
        }

        _glSemaphoreParameterui64vEXT(_consumerSemaphore, GL_D3D12_FENCE_VALUE_EXT, &value);
        uint layout = GL_LAYOUT_SHADER_READ_ONLY_EXT;
        _glSignalSemaphoreEXT(_consumerSemaphore, 0, null, 1, &glTexture, &layout);
        // D3D11 waits on this value on its own queue; flush so the signal is actually submitted.
        glFlush();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DeleteSemaphore(_producerSemaphore);
        DeleteSemaphore(_consumerSemaphore);
        _producerSemaphore = 0;
        _consumerSemaphore = 0;
    }

    private static uint ImportFence(nint handle, uint handleType)
    {
        uint semaphore = 0;
        _glGenSemaphoresEXT(1, &semaphore);
        _glImportSemaphoreWin32HandleEXT(semaphore, handleType, handle);
        return semaphore;
    }

    private static void DeleteSemaphore(uint semaphore)
    {
        if (semaphore != 0)
        {
            _glDeleteSemaphoresEXT(1, &semaphore);
        }
    }

    private static bool TryLoad()
    {
        if (_loaded)
        {
            return _glSignalSemaphoreEXT != null;
        }

        _loaded = true;
        _glGenSemaphoresEXT = (delegate* unmanaged[Stdcall]<int, uint*, void>)wglGetProcAddress("glGenSemaphoresEXT");
        _glDeleteSemaphoresEXT = (delegate* unmanaged[Stdcall]<int, uint*, void>)wglGetProcAddress("glDeleteSemaphoresEXT");
        _glImportSemaphoreWin32HandleEXT = (delegate* unmanaged[Stdcall]<uint, uint, nint, void>)wglGetProcAddress("glImportSemaphoreWin32HandleEXT");
        _glSemaphoreParameterui64vEXT = (delegate* unmanaged[Stdcall]<uint, uint, ulong*, void>)wglGetProcAddress("glSemaphoreParameterui64vEXT");
        _glWaitSemaphoreEXT = (delegate* unmanaged[Stdcall]<uint, uint, uint*, uint, uint*, uint*, void>)wglGetProcAddress("glWaitSemaphoreEXT");
        _glSignalSemaphoreEXT = (delegate* unmanaged[Stdcall]<uint, uint, uint*, uint, uint*, uint*, void>)wglGetProcAddress("glSignalSemaphoreEXT");

        bool available = _glGenSemaphoresEXT != null
            && _glDeleteSemaphoresEXT != null
            && _glImportSemaphoreWin32HandleEXT != null
            && _glSemaphoreParameterui64vEXT != null
            && _glWaitSemaphoreEXT != null
            && _glSignalSemaphoreEXT != null;
        if (!available)
        {
            _glSignalSemaphoreEXT = null;
        }

        return available;
    }

    [DllImport("opengl32.dll")]
    private static extern nint wglGetProcAddress(string name);

    [DllImport("opengl32.dll")]
    private static extern uint glGetError();

    [DllImport("opengl32.dll")]
    private static extern void glFlush();
}
