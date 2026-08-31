using Aprillz.MewUI.Platform.Browser;

namespace Aprillz.MewUI;

public static class BrowserPlatform
{
    public static void Register()
    {
        // The canvas is the only surface, so popups and menus have to live inside it.
        PopupManager.PreferNativePopups = false;
        Application.RegisterPlatformHost(
            static () => new BrowserPlatformHost(),
            Platform.PlatformSurfaceKind.Browser,
            "Browser",
            "sans-serif");
    }

    public static ApplicationBuilder UseBrowser(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }

    /// <summary>Draws one frame; false means nothing changed and the caller may idle.</summary>
    public static bool RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio, int pixelWidth, int pixelHeight)
        => BrowserPlatformHost.Active?.RenderFrame(cssWidth, cssHeight, devicePixelRatio, pixelWidth, pixelHeight) == true;

    /// <summary>Milliseconds until a scheduled timer needs the loop again, or -1 when nothing is due.</summary>
    public static int NextWakeDelayMs() => BrowserPlatformHost.Active?.NextWakeDelayMs() ?? -1;

    public static bool PointerMove(double x, double y, double screenX, double screenY, int buttons, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.PointerMove(x, y, screenX, screenY, buttons, modifiers) == true;

    /// <summary>Routes a pointer press or release; pointerType is 0 for a mouse, 1 for touch, 2 for a pen.</summary>
    public static bool PointerButton(double x, double y, double screenX, double screenY, int button, int buttons,
        bool isDown, int clickCount, ModifierKeys modifiers, int pointerType)
        => BrowserPlatformHost.Active?.PointerButton(
            x, y, screenX, screenY, button, buttons, isDown, clickCount, modifiers,
            pointerType switch { 1 => PointerType.Touch, 2 => PointerType.Pen, _ => PointerType.Mouse }) == true;

    public static void PointerWheel(double x, double y, double screenX, double screenY,
        double deltaX, double deltaY, int buttons, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.PointerWheel(x, y, screenX, screenY, deltaX, deltaY, buttons, modifiers);

    /// <summary>True while the captured element consumes pointer movement rather than tracking a press.</summary>
    public static bool CaptureConsumesDrag() => BrowserPlatformHost.Active?.CaptureConsumesDrag() == true;

    /// <summary>True when the focused element consumes text input, so the host should present a text field.</summary>
    public static bool WantsTextInput() => BrowserPlatformHost.Active?.WantsTextInput() == true;

    /// <summary>Scrolls the element under the point by a finger delta in DIPs, tracking it one to one.</summary>
    public static void PointerPan(double x, double y, double screenX, double screenY,
        double deltaXDip, double deltaYDip, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.PointerPan(x, y, screenX, screenY, deltaXDip, deltaYDip, modifiers);

    public static void PointerLeave() => BrowserPlatformHost.Active?.PointerLeave();
    public static void PointerCancel() => BrowserPlatformHost.Active?.PointerCancel();

    public static bool KeyDown(string code, int platformKey, ModifierKeys modifiers, bool isRepeat)
        => BrowserPlatformHost.Active?.KeyDown(code, platformKey, modifiers, isRepeat) == true;

    public static bool KeyUp(string code, int platformKey, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.KeyUp(code, platformKey, modifiers) == true;

    public static bool TextInput(string text)
        => BrowserPlatformHost.Active?.TextInput(text) == true;

    public static void FocusChanged(bool focused) => BrowserPlatformHost.Active?.FocusChanged(focused);
}
