using System.Diagnostics;

using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Saves presented frames as a binary PPM when MEWUI_FB_CAPTURE names a file, so a screen with no display
/// server can still be checked by eye. Each frame overwrites the file until MEWUI_FB_CAPTURE_AFTER_MS
/// (default 5000) has passed, leaving the last frame shown by then.
/// </summary>
internal sealed unsafe class FramebufferFrameCapture
{
    private const int DEFAULT_DELAY_MS = 5000;

    private readonly string _path;
    private readonly long _dueTimestamp;

    private FramebufferFrameCapture(string path, int delayMs)
    {
        _path = path;
        _dueTimestamp = Stopwatch.GetTimestamp() + delayMs * Stopwatch.Frequency / 1000;
    }

    public static FramebufferFrameCapture? FromEnvironment()
    {
        string? path = Environment.GetEnvironmentVariable("MEWUI_FB_CAPTURE");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        int delayMs = int.TryParse(Environment.GetEnvironmentVariable("MEWUI_FB_CAPTURE_AFTER_MS"), out int parsed) && parsed >= 0
            ? parsed
            : DEFAULT_DELAY_MS;
        return new FramebufferFrameCapture(path, delayMs);
    }

    /// <summary>Reads the bound framebuffer and writes it out, until the capture window has passed.</summary>
    public void TryCapture(int width, int height, bool gles)
    {
        if (Stopwatch.GetTimestamp() > _dueTimestamp)
        {
            return;
        }

        var pixels = new byte[width * height * 4];
        fixed (byte* buffer = pixels)
        {
            GL.ReadPixels(0, 0, width, height, GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, (nint)buffer);
        }

        using var stream = File.Create(_path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        stream.Write(header);
        var row = new byte[width * 3];
        for (int line = height - 1; line >= 0; line--)
        {
            int source = line * width * 4;
            for (int column = 0; column < width; column++)
            {
                row[column * 3] = pixels[source + column * 4];
                row[column * 3 + 1] = pixels[source + column * 4 + 1];
                row[column * 3 + 2] = pixels[source + column * 4 + 2];
            }

            stream.Write(row);
        }

        Console.Error.WriteLine($"[MewUI.Framebuffer] captured {width}x{height} frame to {_path} ({(gles ? "GLES" : "GL")})");
    }
}
