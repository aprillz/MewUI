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

    public static void RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio, int pixelWidth, int pixelHeight)
        => BrowserPlatformHost.Active?.RenderFrame(cssWidth, cssHeight, devicePixelRatio, pixelWidth, pixelHeight);

    public static bool PointerMove(double x, double y, double screenX, double screenY, int buttons, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.PointerMove(x, y, screenX, screenY, buttons, modifiers) == true;

    public static bool PointerButton(double x, double y, double screenX, double screenY, int button, int buttons,
        bool isDown, int clickCount, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.PointerButton(
            x, y, screenX, screenY, button, buttons, isDown, clickCount, modifiers) == true;

    public static void PointerWheel(double x, double y, double screenX, double screenY,
        double deltaX, double deltaY, int buttons, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.PointerWheel(x, y, screenX, screenY, deltaX, deltaY, buttons, modifiers);

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
