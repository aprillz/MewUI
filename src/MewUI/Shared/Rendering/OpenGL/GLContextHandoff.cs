using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Orders what one context draws before what another context of the same share group reads, on the GPU
/// instead of by the drawing thread waiting for the GPU to finish.
/// </summary>
internal sealed class GLContextHandoff
{
    private nint _fence;

    /// <summary>
    /// Marks the end of the drawing on the current context; false when the driver cannot order contexts
    /// this way and the caller has to wait for the GPU instead.
    /// </summary>
    public bool TrySignal()
    {
        if (!OpenGLPboExt.HasServerWait)
        {
            return false;
        }

        nint fence = OpenGLPboExt.FenceSync();
        if (fence == 0)
        {
            return false;
        }

        // A fence is signaled only once the commands before it reach the GPU.
        GL.Flush();

        nint unread = Interlocked.Exchange(ref _fence, fence);
        if (unread != 0)
        {
            // The frame it marked was never copied anywhere, so nothing waits on it.
            OpenGLPboExt.DeleteSync(unread);
        }

        return true;
    }

    /// <summary>Makes the commands that follow on the current context run after the drawing last marked.</summary>
    public void Await()
    {
        nint fence = Interlocked.Exchange(ref _fence, 0);
        if (fence != 0)
        {
            OpenGLPboExt.WaitSync(fence);
            OpenGLPboExt.DeleteSync(fence);
        }
    }
}
