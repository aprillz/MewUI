using Aprillz.MewUI.Input;
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
        WindowDragDropRouter.PreferNativePreviewWindow = false;
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

    /// <summary>
    /// Draws one frame; false means nothing changed and the caller may idle. <paramref name="frameAgeMs"/>
    /// is how long ago the frame's timestamp was, <c>performance.now() - frameTime</c>, so animation
    /// advances to the time the frame is presented.
    /// </summary>
    public static bool RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio, int pixelWidth, int pixelHeight, double frameAgeMs)
        => BrowserPlatformHost.Active?.RenderFrame(cssWidth, cssHeight, devicePixelRatio, pixelWidth, pixelHeight, frameAgeMs) == true;

    /// <summary>Milliseconds until a scheduled timer needs the loop again, or -1 when nothing is due.</summary>
    public static int NextWakeDelayMs() => BrowserPlatformHost.Active?.NextWakeDelayMs() ?? -1;

    /// <summary>Routes a mouse move. Touch and pen go through the overload that takes a pointer id.</summary>
    public static bool PointerMove(double x, double y, double screenX, double screenY, int buttons, ModifierKeys modifiers, int pointerType)
        => BrowserPlatformHost.Active?.PointerMove(x, y, screenX, screenY, buttons, modifiers, MapPointerType(pointerType)) == true;

    /// <summary>
    /// Routes a mouse press or release. Clicks are counted from the event time and position, since the
    /// DOM reports no click count. Touch and pen go through <see cref="PointerDown"/> and <see cref="PointerUp"/>.
    /// </summary>
    public static bool PointerButton(double x, double y, double screenX, double screenY, int button, int buttons,
        bool isDown, double timeStampMs, ModifierKeys modifiers, int pointerType)
        => BrowserPlatformHost.Active?.PointerButton(
            x, y, screenX, screenY, button, buttons, isDown, timeStampMs, modifiers, MapPointerType(pointerType)) == true;

    /// <summary>
    /// Routes a touch or pen contact going down; <paramref name="pointerType"/> is 1 for touch and 2 for
    /// a pen, and <paramref name="eventAgeMs"/> is <c>performance.now() - event.timeStamp</c>. The core
    /// reads taps, scrolls, holds and coasting from the contacts.
    /// </summary>
    public static void PointerDown(int pointerId, int pointerType, double x, double y, double screenX, double screenY,
        double eventAgeMs, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.PointerDown(pointerId, MapPointerType(pointerType), x, y, screenX, screenY, eventAgeMs, modifiers);

    /// <summary>Routes a touch or pen contact moving, or a pen hovering in range.</summary>
    public static void PointerMove(int pointerId, int pointerType, double x, double y, double screenX, double screenY,
        double eventAgeMs, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.PointerMove(pointerId, MapPointerType(pointerType), x, y, screenX, screenY, eventAgeMs, modifiers);

    /// <summary>Routes a touch or pen contact lifting.</summary>
    public static void PointerUp(int pointerId, int pointerType, double x, double y, double screenX, double screenY,
        double eventAgeMs, ModifierKeys modifiers)
        => BrowserPlatformHost.Active?.PointerUp(pointerId, MapPointerType(pointerType), x, y, screenX, screenY, eventAgeMs, modifiers);

    /// <summary>Ends a touch or pen contact the page lost without seeing it lift.</summary>
    public static void PointerCancel(int pointerId) => BrowserPlatformHost.Active?.PointerCancel(pointerId);

    private static PointerType MapPointerType(int pointerType)
        => pointerType switch { 1 => PointerType.Touch, 2 => PointerType.Pen, _ => PointerType.Mouse };

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

    public static void PointerLeave() => BrowserPlatformHost.Active?.PointerLeave();
    /// <summary>Ends a mouse press the page lost without seeing it released.</summary>
    public static void PointerCancel() => BrowserPlatformHost.Active?.PointerCancel();

    public static bool KeyDown(string code, int platformKey, ModifierKeys modifiers, bool isRepeat)
        => KeyDown(code, platformKey, modifiers, isRepeat, key: null);

    /// <summary>
    /// Routes a key press. <paramref name="key"/> is the page's <c>KeyboardEvent.key</c>; passing it lets a letter key
    /// be named by the letter it types in the current layout instead of by its position.
    /// </summary>
    public static bool KeyDown(string code, int platformKey, ModifierKeys modifiers, bool isRepeat, string? key)
        => BrowserPlatformHost.Active?.KeyDown(code, platformKey, modifiers, isRepeat, key) == true;

    public static bool KeyUp(string code, int platformKey, ModifierKeys modifiers)
        => KeyUp(code, platformKey, modifiers, key: null);

    /// <summary>Routes a key release; <paramref name="key"/> as for <see cref="KeyDown(string, int, ModifierKeys, bool, string?)"/>.</summary>
    public static bool KeyUp(string code, int platformKey, ModifierKeys modifiers, string? key)
        => BrowserPlatformHost.Active?.KeyUp(code, platformKey, modifiers, key) == true;

    /// <summary>Starts an IME composition; the control shows the pre-edit until it ends.</summary>
    public static void CompositionStart() => BrowserPlatformHost.Active?.CompositionStart();

    /// <summary>Replaces the pre-edit text of the running composition.</summary>
    public static void CompositionUpdate(string text) => BrowserPlatformHost.Active?.CompositionUpdate(text);

    /// <summary>Ends the composition, committing the text it carries.</summary>
    public static void CompositionEnd(string text) => BrowserPlatformHost.Active?.CompositionEnd(text);

    public static bool TextInput(string text)
        => BrowserPlatformHost.Active?.TextInput(text) == true;

    /// <summary>
    /// Text around the caret for the page to mirror into its IME field, as "start:end:text" over
    /// that text; empty when nothing editable holds focus.
    /// </summary>
    public static string GetTextInputState() => BrowserPlatformHost.Active?.GetTextInputState() ?? string.Empty;

    /// <summary>Replaces the given number of characters around the caret with <paramref name="text"/>.</summary>
    public static void ReplaceText(int replacePrevious, int replaceNext, string text)
        => BrowserPlatformHost.Active?.ReplaceText(replacePrevious, replaceNext, text);

    public static void FocusChanged(bool focused)
    {
        // The page reports its focus once while the runtime is still booting, before a host exists;
        // the host picks this value up when it starts so the report is not lost.
        LastReportedFocus = focused;
        BrowserPlatformHost.Active?.FocusChanged(focused);
    }

    internal static bool LastReportedFocus { get; private set; }
}
