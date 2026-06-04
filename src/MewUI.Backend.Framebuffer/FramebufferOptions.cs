namespace Aprillz.MewUI.Rendering.Framebuffer;

public sealed record FramebufferOptions
{
    public string DevicePath { get; init; } = "/dev/fb0";

    public bool Force32BitsPerPixel { get; init; } = true;

    public bool WaitForVSync { get; init; } = true;

    public double DpiScale { get; init; } = 1.0;

    public bool EnableTouchInput { get; init; } = true;

    public string? TouchDevicePath { get; init; }

    public bool SwapTouchAxes { get; init; }

    public bool InvertTouchX { get; init; }

    public bool InvertTouchY { get; init; }

    public bool LogTouchInput { get; init; }

    public bool TouchClickOnly { get; init; }

    public bool TouchTapOnly { get; init; }

    public double TouchTapMaxMoveDip { get; init; } = 28;

    public int TouchMoveThrottleMs { get; init; } = 100;

    public bool PollTouchOnUiThread { get; init; }
}
