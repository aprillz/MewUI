using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Aprillz.MewUI.Gallery;

[SupportedOSPlatform("browser")]
internal static partial class BrowserExports
{
    [JSImport("writeClipboard", "main.js")]
    internal static partial void WriteClipboard(string text);

    [JSImport("moveTextInput", "main.js")]
    internal static partial void MoveTextInput(double x, double y, double height);

    static BrowserExports()
    {
        BrowserPlatform.ClipboardWriter = WriteClipboard;
        BrowserPlatform.CaretReporter = MoveTextInput;
    }


    /// <summary>Draws one frame; false means nothing changed and the page may stop scheduling.</summary>
    [JSExport]
    internal static bool RenderFrame(
        double cssWidth,
        double cssHeight,
        double devicePixelRatio,
        int pixelWidth,
        int pixelHeight,
        double frameAgeMs)
        => BrowserPlatform.RenderFrame(cssWidth, cssHeight, devicePixelRatio, pixelWidth, pixelHeight, frameAgeMs);

    /// <summary>Milliseconds until a scheduled timer needs the loop again, or -1 when nothing is due.</summary>
    [JSExport]
    internal static int NextWakeDelayMs() => BrowserPlatform.NextWakeDelayMs();

    [JSExport]
    internal static bool MouseMove(double x, double y, double screenX, double screenY, int buttons, int modifiers)
        => BrowserPlatform.PointerMove(x, y, screenX, screenY, buttons, (ModifierKeys)modifiers, pointerType: 0);

    [JSExport]
    internal static bool MouseButton(double x, double y, double screenX, double screenY, int button, int buttons,
        bool isDown, double timeStampMs, int modifiers)
        => BrowserPlatform.PointerButton(
            x, y, screenX, screenY, button, buttons, isDown, timeStampMs, (ModifierKeys)modifiers, pointerType: 0);

    [JSExport]
    internal static void MouseCancel() => BrowserPlatform.PointerCancel();

    [JSExport]
    internal static void PointerDown(int pointerId, int pointerType, double x, double y, double screenX, double screenY,
        double eventAgeMs, int modifiers)
        => BrowserPlatform.PointerDown(pointerId, pointerType, x, y, screenX, screenY, eventAgeMs, (ModifierKeys)modifiers);

    [JSExport]
    internal static void PointerMove(int pointerId, int pointerType, double x, double y, double screenX, double screenY,
        double eventAgeMs, int modifiers)
        => BrowserPlatform.PointerMove(pointerId, pointerType, x, y, screenX, screenY, eventAgeMs, (ModifierKeys)modifiers);

    [JSExport]
    internal static void PointerUp(int pointerId, int pointerType, double x, double y, double screenX, double screenY,
        double eventAgeMs, int modifiers)
        => BrowserPlatform.PointerUp(pointerId, pointerType, x, y, screenX, screenY, eventAgeMs, (ModifierKeys)modifiers);

    [JSExport]
    internal static void PointerCancel(int pointerId) => BrowserPlatform.PointerCancel(pointerId);

    [JSExport]
    internal static void PointerWheel(double x, double y, double screenX, double screenY,
        double deltaX, double deltaY, int buttons, int modifiers)
        => BrowserPlatform.PointerWheel(
            x, y, screenX, screenY, deltaX, deltaY, buttons, (ModifierKeys)modifiers);

    [JSExport]
    internal static void SetSystemDarkMode(bool isDark) => BrowserPlatform.SetSystemDarkMode(isDark);

    /// <summary>Records the text a paste gesture carried, before the paste is routed on.</summary>
    [JSExport]
    internal static void SetClipboardText(string text) => BrowserPlatform.SetClipboardText(text);

    [JSExport]
    internal static bool WantsTextInput() => BrowserPlatform.WantsTextInput();

    [JSExport]
    internal static void SyncTextCaret() => BrowserPlatform.SyncTextCaret();

    [JSExport]
    internal static void PointerLeave() => BrowserPlatform.PointerLeave();

    [JSExport]
    internal static bool KeyDown(string code, int platformKey, int modifiers, bool isRepeat, string? key)
        => BrowserPlatform.KeyDown(code, platformKey, (ModifierKeys)modifiers, isRepeat, key);

    [JSExport]
    internal static bool KeyUp(string code, int platformKey, int modifiers, string? key)
        => BrowserPlatform.KeyUp(code, platformKey, (ModifierKeys)modifiers, key);

    [JSExport]
    internal static void CompositionStart() => BrowserPlatform.CompositionStart();

    [JSExport]
    internal static void CompositionUpdate(string text) => BrowserPlatform.CompositionUpdate(text);

    [JSExport]
    internal static void CompositionEnd(string text) => BrowserPlatform.CompositionEnd(text);

    [JSExport]
    internal static bool TextInput(string text) => BrowserPlatform.TextInput(text);

    [JSExport]
    internal static string GetTextInputState() => BrowserPlatform.GetTextInputState();

    [JSExport]
    internal static void ReplaceText(int replacePrevious, int replaceNext, string text)
        => BrowserPlatform.ReplaceText(replacePrevious, replaceNext, text);

    [JSExport]
    internal static void FocusChanged(bool focused) => BrowserPlatform.FocusChanged(focused);

    /// <summary>
    /// File names the host should fetch, in the order the pages need them. Font files are left out: this
    /// backend draws with the browser's fonts, which a registered font file does not reach.
    /// </summary>
    [JSExport]
    internal static string[] ResourceFileNames()
        => GalleryResources.FileNames.Where(name => !name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>Hands one fetched resource to the gallery; the bound pages pick it up on the next frame.</summary>
    [JSExport]
    internal static void ApplyResource(string fileName, byte[] content)
        => GalleryView.Resources.Apply(fileName, content);
}
