namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>The screen the framebuffer host draws on: a DRM/KMS output or an fbdev device.</summary>
internal interface IFramebufferOutput : IDisposable
{
    /// <summary>One line naming the device, connector and mode, for the startup log.</summary>
    string Description { get; }

    int PixelWidth { get; }

    int PixelHeight { get; }

    /// <summary>Refresh rate in Hz, or 0 when the device does not report one.</summary>
    int RefreshHz { get; }

    /// <summary>True while a presented frame waits for scanout; <see cref="EventFd"/> then reports when it lands.</summary>
    bool FlipPending { get; }

    /// <summary>
    /// True when the output can take another frame. A flip in flight still leaves room for one queued frame, so
    /// the next frame is built while the GPU and display finish the last one.
    /// </summary>
    bool CanTakeFrame { get; }

    /// <summary>File descriptor to poll for presentation events, or -1 when there is none.</summary>
    int EventFd { get; }

    /// <summary>Handles the presentation events waiting on <see cref="EventFd"/>.</summary>
    void DispatchEvents();

    /// <summary>The window surface a graphics backend draws the single window into.</summary>
    IWindowSurface WindowSurface { get; }

    /// <summary>Shows or hides the pointer; outputs without a cursor plane ignore it.</summary>
    void SetCursorVisible(bool visible);

    /// <summary>Moves the pointer to a screen pixel.</summary>
    void MoveCursor(int x, int y);
}
