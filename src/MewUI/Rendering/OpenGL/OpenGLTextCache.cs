using System.Collections.Generic;

using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal readonly record struct OpenGLTextCacheKey(
    string Text,
    nint FontHandle,
    string FontId,
    int FontSizePx,
    uint ColorArgb,
    int WidthPx,
    int HeightPx,
    int HAlign,
    int VAlign,
    int Wrapping
);

internal readonly record struct OpenGLTextureEntry(uint TextureId, int WidthPx, int HeightPx);

internal sealed class OpenGLTextCache : IDisposable
{
    private readonly Dictionary<OpenGLTextCacheKey, OpenGLTextureEntry> _textures = new();
    private bool _disposed;

    public OpenGLTextureEntry GetOrCreateTexture(
        bool supportsBgra,
        nint hdc,
        OpenGLTextCacheKey key,
        Func<OpenGLTextBitmap> factory)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OpenGLTextCache));

        if (_textures.TryGetValue(key, out var entry))
            return entry;

        var bmp = factory();
        uint tex = UploadTexture(supportsBgra, bmp);
        entry = new OpenGLTextureEntry(tex, bmp.WidthPx, bmp.HeightPx);
        _textures[key] = entry;
        return entry;
    }

    private static uint UploadTexture(bool supportsBgra, OpenGLTextBitmap bmp)
    {
        GL.GenTextures(1, out uint tex);
        GL.BindTexture(GL.GL_TEXTURE_2D, tex);
        // Keep text crisp; callers can change later if needed.
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_NEAREST);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_NEAREST);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, (int)GL.GL_CLAMP);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, (int)GL.GL_CLAMP);

        var data = bmp.Data;
        uint format = supportsBgra ? GL.GL_BGRA_EXT : GL.GL_RGBA;

        // If BGRA is not supported, we convert.
        if (!supportsBgra)
            data = OpenGLTextRasterizer.ConvertBgraToRgba(data);

        unsafe
        {
            fixed (byte* p = data)
            {
                GL.TexImage2D(GL.GL_TEXTURE_2D, 0, (int)GL.GL_RGBA, bmp.WidthPx, bmp.HeightPx, 0,
                    format, GL.GL_UNSIGNED_BYTE, (nint)p);
            }
        }

        return tex;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var kvp in _textures)
        {
            uint tex = kvp.Value.TextureId;
            GL.DeleteTextures(1, ref tex);
        }

        _textures.Clear();
    }
}
