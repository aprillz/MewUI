using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed class OpenGLImage : IImage
{
    private readonly byte[] _bgra;
    private byte[]? _rgbaCache;
    private readonly Dictionary<nint, uint> _texturesByWindow = new();
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public OpenGLImage(int widthPx, int heightPx, byte[] bgra)
    {
        PixelWidth = widthPx;
        PixelHeight = heightPx;
        ArgumentNullException.ThrowIfNull(bgra);
        _bgra = bgra;
        if (_bgra.Length != widthPx * heightPx * 4)
        {
            throw new ArgumentException("Invalid BGRA buffer length.", nameof(bgra));
        }
    }

    public uint GetOrCreateTexture(IOpenGLWindowResources resources, nint hwnd)
    {
        if (_disposed)
        {
            return 0;
        }

        if (_texturesByWindow.TryGetValue(hwnd, out var tex) && tex != 0)
        {
            return tex;
        }

        GL.GenTextures(1, out tex);
        if (tex == 0)
        {
            return 0;
        }

        resources.TrackTexture(tex);

        GL.BindTexture(GL.GL_TEXTURE_2D, tex);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_LINEAR);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_LINEAR);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, (int)GL.GL_CLAMP_TO_EDGE);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, (int)GL.GL_CLAMP_TO_EDGE);

        byte[] pixels = _bgra;
        uint format = resources.SupportsBgra ? GL.GL_BGRA_EXT : GL.GL_RGBA;
        if (!resources.SupportsBgra)
        {
            pixels = _rgbaCache ??= OpenGLPixelUtils.ConvertBgraToRgba(_bgra);
        }

        unsafe
        {
            fixed (byte* p = pixels)
            {
                GL.TexImage2D(
                    GL.GL_TEXTURE_2D,
                    level: 0,
                    internalformat: (int)GL.GL_RGBA,
                    width: PixelWidth,
                    height: PixelHeight,
                    border: 0,
                    format: format,
                    type: GL.GL_UNSIGNED_BYTE,
                    pixels: (nint)p);
            }
        }

        _texturesByWindow[hwnd] = tex;
        return tex;
    }

    public void Dispose()
    {
        _disposed = true;
        _texturesByWindow.Clear();
    }
}
