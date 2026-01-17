using Aprillz.MewUI.Native;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed class OpenGLImage : IImage
{
    private readonly IPixelBufferSource _pixels;
    private int _pixelsVersion = -1;
    private byte[]? _rgbaCache;
    private int _rgbaCacheVersion = -1;
    private readonly Dictionary<nint, TextureEntry> _texturesByWindow = new();
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public OpenGLImage(int widthPx, int heightPx, byte[] bgra)
    {
        PixelWidth = widthPx;
        PixelHeight = heightPx;
        ArgumentNullException.ThrowIfNull(bgra);
        _pixels = new StaticPixelBufferSource(widthPx, heightPx, bgra);
        _pixelsVersion = 0;
    }

    public OpenGLImage(IPixelBufferSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.PixelFormat != BitmapPixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Unsupported pixel format: {source.PixelFormat}");
        }

        PixelWidth = source.PixelWidth;
        PixelHeight = source.PixelHeight;
        _pixels = source;
        _pixelsVersion = source.Version;
    }

    private readonly record struct TextureEntry(uint TextureId, int Version);

    public uint GetOrCreateTexture(IOpenGLWindowResources resources, nint hwnd)
    {
        if (_disposed)
        {
            return 0;
        }

        int version = _pixels.Version;
        if (_pixelsVersion != version)
        {
            _pixelsVersion = version;
            _rgbaCache = null;
            _rgbaCacheVersion = -1;
        }

        if (_texturesByWindow.TryGetValue(hwnd, out var entry) && entry.TextureId != 0 && entry.Version == version)
        {
            return entry.TextureId;
        }

        uint tex = entry.TextureId;
        if (tex == 0)
        {
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
        }
        else
        {
            GL.BindTexture(GL.GL_TEXTURE_2D, tex);
        }

        byte[] pixels;
        using (var l = _pixels.Lock())
        {
            pixels = l.Buffer;
        }

        if (pixels.Length == 0)
        {
            return 0;
        }

        uint format = resources.SupportsBgra ? GL.GL_BGRA_EXT : GL.GL_RGBA;
        if (!resources.SupportsBgra)
        {
            if (_rgbaCache == null || _rgbaCacheVersion != version)
            {
                _rgbaCache = OpenGLPixelUtils.ConvertBgraToRgba(pixels);
                _rgbaCacheVersion = version;
            }

            pixels = _rgbaCache;
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

        _texturesByWindow[hwnd] = new TextureEntry(tex, version);
        return tex;
    }

    public void Dispose()
    {
        _disposed = true;
        _texturesByWindow.Clear();
    }
}
