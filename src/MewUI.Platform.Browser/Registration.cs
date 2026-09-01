using Aprillz.MewUI.Platform.Browser;

namespace Aprillz.MewUI;

public static class BrowserPlatform
{
    private static bool _systemIsDark;

    internal static bool SystemIsDark => _systemIsDark;

    /// <summary>
    /// Reports the page's colour scheme. Call it before running the application so the first window
    /// opens in the right theme; later calls re-resolve <see cref="ThemeVariant.System"/>.
    /// </summary>
    public static void SetSystemDarkMode(bool isDark)
    {
        if (_systemIsDark == isDark)
        {
            return;
        }

        _systemIsDark = isDark;
        if (Application.IsRunning)
        {
            Application.Current.NotifySystemThemeChanged();
        }
    }

    public static void Register()
    {
        // The canvas is the only surface, so popups, menus and drag previews have to live inside it.
        PopupManager.PreferNativePopups = false;
        Input.WindowDragDropRouter.PreferNativePreviewWindow = false;
        Window.PreferNativeDialogWindows = false;
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
    public static bool RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio, int pixelWidth, int pixelHeight, double frameTimeMs)
        => BrowserPlatformHost.Active?.RenderFrame(cssWidth, cssHeight, devicePixelRatio, pixelWidth, pixelHeight, frameTimeMs) == true;

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

    /// <summary>
    /// Hands copied text to the page. A browser only writes the clipboard from a user gesture, which
    /// a copy or cut always is, so the host can post it and not wait for the result.
    /// </summary>
    public static Action<string>? ClipboardWriter { get; set; }

    /// <summary>
    /// Records the text a paste gesture carried. A browser reveals the clipboard only inside that
    /// gesture, so this has to run before the paste reaches a control.
    /// </summary>
    public static void SetClipboardText(string text) => BrowserPlatformHost.Active?.SetClipboardText(text);

    /// <summary>True while the captured element consumes pointer movement rather than tracking a press.</summary>
    public static bool CaptureConsumesDrag() => BrowserPlatformHost.Active?.CaptureConsumesDrag() == true;

    /// <summary>True when the focused element consumes text input, so the host should present a text field.</summary>
    public static bool WantsTextInput() => BrowserPlatformHost.Active?.WantsTextInput() == true;

    /// <summary>
    /// Places the page's text field over the caret, given its position and line height in DIPs. A
    /// browser draws the IME candidate list against the focused field, so a field parked in a corner
    /// puts the candidates there too.
    /// </summary>
    public static Action<double, double, double>? CaretReporter { get; set; }

    /// <summary>Reports the caret through <see cref="CaretReporter"/>, if a text control holds one.</summary>
    public static void SyncTextCaret() => BrowserPlatformHost.Active?.SyncTextCaret();

    /// <summary>Scrolls the element under the point by a finger delta in DIPs, tracking it one to one.</summary>
    public static void PointerPan(double x, double y, double screenX, double screenY,
        double deltaXDip, double deltaYDip, ModifierKeys modifiers, double timeStampMs)
        => BrowserPlatformHost.Active?.PointerPan(x, y, screenX, screenY, deltaXDip, deltaYDip, modifiers, timeStampMs);

    /// <summary>Lets a finished touch pan coast, from the speed the finger left it with.</summary>
    public static void PointerPanRelease(double timeStampMs) => BrowserPlatformHost.Active?.PointerPanRelease(timeStampMs);

    public static void PointerLeave() => BrowserPlatformHost.Active?.PointerLeave();
    public static void PointerCancel() => BrowserPlatformHost.Active?.PointerCancel();

    public static bool KeyDown(string code, int platformKey, ModifierKeys modifiers, bool isRepeat)
        => BrowserPlatformHost.Active?.KeyDown(code, platformKey, modifiers, isRepeat) == true;

    public static bool KeyUp(string code, int platformKey, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.KeyUp(code, platformKey, modifiers) == true;

    /// <summary>Starts an IME composition; the control shows the pre-edit until it ends.</summary>
    public static void CompositionStart() => BrowserPlatformHost.Active?.CompositionStart();

    /// <summary>Replaces the pre-edit text of the running composition.</summary>
    public static void CompositionUpdate(string text) => BrowserPlatformHost.Active?.CompositionUpdate(text);

    /// <summary>Ends the composition, committing the text it carries.</summary>
    public static void CompositionEnd(string text) => BrowserPlatformHost.Active?.CompositionEnd(text);

    public static bool TextInput(string text)
        => BrowserPlatformHost.Active?.TextInput(text) == true;

    public static void FocusChanged(bool focused) => BrowserPlatformHost.Active?.FocusChanged(focused);
}
