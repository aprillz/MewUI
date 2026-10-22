using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform.Linux.Framebuffer;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the Linux framebuffer platform host with <see cref="Application"/>: one full-screen window drawn
/// straight to a DRM/KMS output or an fbdev device, with input from libinput.
/// </summary>
public static class FramebufferPlatform
{
    public static string PlatformIdentifier => FramebufferPlatformHost.PlatformIdentifier;

    public static void Register(FramebufferPlatformOptions? options = null)
    {
        var resolved = options ?? new FramebufferPlatformOptions();

        // The screen is the only surface, so popups, menus, dialogs and drag previews have to live inside it.
        PopupManager.PreferNativePopups = false;
        WindowDragDropRouter.PreferNativePreviewWindow = false;
        Window.PreferNativeDialogWindows = false;

        Application.RegisterPlatformHost(() => new FramebufferPlatformHost(resolved), Platform.PlatformSurfaceKind.Framebuffer,
            "Framebuffer", Platform.Linux.LinuxFontDefaults.SystemFontFamily);
        Rendering.RenderResourceMetrics.ProcessMemoryReader = Platform.Linux.LinuxProcessMemory.Read;
    }

    public static ApplicationBuilder UseFramebuffer(this ApplicationBuilder builder, Action<FramebufferPlatformOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new FramebufferPlatformOptions();
        configure?.Invoke(options);
        Register(options);

        return builder;
    }
}

/// <summary>Selects how the framebuffer platform reaches the screen.</summary>
public enum FramebufferOutputKind
{
    /// <summary>DRM/KMS when a card with a connected display can be opened, otherwise fbdev.</summary>
    Auto,

    /// <summary>DRM/KMS mode setting with GBM buffers and page flips.</summary>
    DrmKms,

    /// <summary>A Linux fbdev device written by the CPU.</summary>
    Fbdev,
}

/// <summary>Where the framebuffer host gets its device access from.</summary>
public enum FramebufferSessionKind
{
    /// <summary><see cref="Seat"/> when a seat manager accepts the process, <see cref="Direct"/> otherwise.</summary>
    Auto,

    /// <summary>
    /// The seat manager (logind or seatd, through libseat) hands out the DRM card and the input devices. The process
    /// has to run inside an active session on the seat; the app pauses while another virtual terminal is active and
    /// resumes with the screen when it comes back.
    /// </summary>
    Seat,

    /// <summary>The device nodes are opened directly, which needs the video and input groups and no other DRM master.</summary>
    Direct,
}

/// <summary>Configures the Linux framebuffer platform host.</summary>
public sealed class FramebufferPlatformOptions
{
    /// <summary>Where device access comes from. <see cref="FramebufferSessionKind.Auto"/> by default.</summary>
    public FramebufferSessionKind Session { get; set; } = FramebufferSessionKind.Auto;

    /// <summary>
    /// Whether Ctrl+Alt+F1 to F12 switch the virtual terminal through the seat manager instead of reaching the app. On
    /// by default; a kiosk turns it off. Has no effect with direct device access.
    /// </summary>
    public bool VtSwitchKeys { get; set; } = true;

    /// <summary>How to reach the screen.</summary>
    public FramebufferOutputKind Output { get; set; } = FramebufferOutputKind.Auto;

    /// <summary>DRM card to open, or <see langword="null"/> to take the first card with a connected display.</summary>
    public string? DrmDevicePath { get; set; }

    /// <summary>fbdev device to open, or <see langword="null"/> for the FRAMEBUFFER variable, then /dev/fb0.</summary>
    public string? FbDevicePath { get; set; }

    /// <summary>DRM connector to drive by kernel name (for example HDMI-A-1), or <see langword="null"/> for the first connected one.</summary>
    public string? ConnectorName { get; set; }

    /// <summary>DRM mode size in pixels, or <see langword="null"/> for the display's preferred mode.</summary>
    public (int Width, int Height)? VideoMode { get; set; }

    /// <summary>Device pixels per DIP. Display size reports are not trusted, so this is not derived from them.</summary>
    public double DpiScale { get; set; } = 1.0;

    /// <summary>
    /// Whether a mouse pointer is drawn: on the DRM cursor plane, or by the CPU on fbdev. It appears once the mouse
    /// first moves and hides on a touch until the mouse moves again. Off by default.
    /// </summary>
    public bool ShowCursor { get; set; }

    /// <summary>libinput seat to read devices from.</summary>
    public string Seat { get; set; } = "seat0";

    /// <summary>xkb layout name (for example us or kr), or <see langword="null"/> for XKB_DEFAULT_LAYOUT, then us.</summary>
    public string? KeyboardLayout { get; set; }

    /// <summary>Theme variant reported as the system theme; there is no desktop setting to read.</summary>
    public ThemeVariant SystemTheme { get; set; } = ThemeVariant.Light;
}
