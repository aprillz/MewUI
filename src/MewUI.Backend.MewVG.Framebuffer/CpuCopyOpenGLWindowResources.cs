using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.Framebuffer;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Draws into a framebuffer object that keeps every frame, and copies what changed to memory the CPU writes, such as
/// a mapped fbdev device.
/// </summary>
internal sealed unsafe class CpuCopyOpenGLWindowResources : FramebufferOpenGLWindowResources
{
    private readonly ICpuFramebufferWindowSurface _surface;
    private readonly uint _texture;
    private readonly uint _fbo;
    private byte[]? _readbackBuffer;

    private CpuCopyOpenGLWindowResources(FramebufferEglDevice device, nint context, ICpuFramebufferWindowSurface surface, uint texture, uint fbo)
        : base(device, context)
    {
        _surface = surface;
        _texture = texture;
        _fbo = fbo;
    }

    public override bool DrawsInPlace => true;

    public override bool TracksBufferAge => false;

    public override bool TargetIsTopDown => false;

    public static CpuCopyOpenGLWindowResources Create(FramebufferEglDevice device, ICpuFramebufferWindowSurface surface)
    {
        nint context = CreateCurrentContext(device);
        try
        {
            int width = surface.PixelWidth;
            int height = surface.PixelHeight;
            GL.GenTextures(1, out uint texture);
            GL.BindTexture(GL.GL_TEXTURE_2D, texture);
            GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_NEAREST);
            GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_NEAREST);
            GL.TexImage2D(GL.GL_TEXTURE_2D, 0, (int)GL.GL_RGBA, width, height, 0, GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, 0);

            uint fbo = 0;
            OpenGLExt.GenFramebuffers(1, &fbo);
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, fbo);
            OpenGLExt.FramebufferTexture2D(OpenGLExt.GL_FRAMEBUFFER, OpenGLExt.GL_COLOR_ATTACHMENT0, GL.GL_TEXTURE_2D, texture, 0);
            CheckFrameTargetComplete(width, height);
            ClearFrameTarget();
            return new CpuCopyOpenGLWindowResources(device, context, surface, texture, fbo);
        }
        finally
        {
            device.ReleaseCurrent();
        }
    }

    public override void AcquireFrameTarget()
    {
    }

    public override int QueryBufferAge() => 0;

    public override void BindFrameTarget() => OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, _fbo);

    public override void Present(IReadOnlyList<PixelArea>? areas)
    {
        int width = _surface.PixelWidth;
        int height = _surface.PixelHeight;
        BindFrameTarget();
        Capture?.TryCapture(width, height, Device.IsGles, topDown: false);
        _surface.WaitForVerticalBlank();
        if (areas == null)
        {
            CopyArea(new PixelArea(0, 0, width, height), height);
        }
        else
        {
            for (int index = 0; index < areas.Count; index++)
            {
                CopyArea(areas[index], height);
            }
        }

        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
    }

    private void CopyArea(PixelArea area, int surfaceHeight)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        int stride = area.Width * 4;
        int length = stride * area.Height;
        if (_readbackBuffer == null || _readbackBuffer.Length < length)
        {
            _readbackBuffer = new byte[Math.Max(length, (_readbackBuffer?.Length ?? 0) * 2)];
        }

        // GL rows run bottom-up; desktop GL reads BGRA directly, GLES only RGBA.
        bool bgra = SupportsBgra;
        fixed (byte* buffer = _readbackBuffer)
        {
            GL.ReadPixels(area.X, surfaceHeight - area.Y - area.Height, area.Width, area.Height,
                bgra ? GL.GL_BGRA_EXT : GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, (nint)buffer);
        }

        var pixels = _readbackBuffer.AsSpan(0, length);
        if (!bgra)
        {
            SwapRedBlue(pixels);
        }

        for (int row = 0; row < area.Height; row++)
        {
            int sourceRow = area.Height - 1 - row;
            _surface.CopyRows(pixels.Slice(sourceRow * stride, stride), stride, area.X, area.Y + row, area.Width, 1);
        }
    }

    private static void SwapRedBlue(Span<byte> pixels)
    {
        for (int offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            (pixels[offset], pixels[offset + 2]) = (pixels[offset + 2], pixels[offset]);
        }
    }

    protected override void DeleteFrameTargets()
    {
        uint fbo = _fbo;
        OpenGLExt.DeleteFramebuffers(1, &fbo);
        uint texture = _texture;
        GL.DeleteTextures(1, ref texture);
    }
}
