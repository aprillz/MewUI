using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Aprillz.MewUI.Gallery;

[SupportedOSPlatform("browser")]
internal static partial class BrowserExports
{
    [JSImport("writeClipboard", "main.js")]
    internal static partial void WriteClipboard(string text);

    static BrowserExports() => BrowserPlatform.ClipboardWriter = WriteClipboard;


    /// <summary>Draws one frame; false means nothing changed and the page may stop scheduling.</summary>
    [JSExport]
    internal static bool RenderFrame(
        double cssWidth,
        double cssHeight,
        double devicePixelRatio,
        int pixelWidth,
        int pixelHeight)
        => BrowserPlatform.RenderFrame(cssWidth, cssHeight, devicePixelRatio, pixelWidth, pixelHeight);

    /// <summary>Milliseconds until a scheduled timer needs the loop again, or -1 when nothing is due.</summary>
    [JSExport]
    internal static int NextWakeDelayMs() => BrowserPlatform.NextWakeDelayMs();

    [JSExport]
    internal static bool PointerMove(double x, double y, double screenX, double screenY, int buttons, int modifiers)
        => BrowserPlatform.PointerMove(x, y, screenX, screenY, buttons, (ModifierKeys)modifiers);

    [JSExport]
    internal static bool PointerButton(double x, double y, double screenX, double screenY, int button, int buttons,
        bool isDown, int clickCount, int modifiers, int pointerType)
        => BrowserPlatform.PointerButton(
            x, y, screenX, screenY, button, buttons, isDown, clickCount, (ModifierKeys)modifiers, pointerType);

    [JSExport]
    internal static void PointerWheel(double x, double y, double screenX, double screenY,
        double deltaX, double deltaY, int buttons, int modifiers)
        => BrowserPlatform.PointerWheel(
            x, y, screenX, screenY, deltaX, deltaY, buttons, (ModifierKeys)modifiers);

    [JSExport]
    internal static bool CaptureConsumesDrag() => BrowserPlatform.CaptureConsumesDrag();

    [JSExport]
    internal static void SetSystemDarkMode(bool isDark) => BrowserPlatform.SetSystemDarkMode(isDark);

    /// <summary>Records the text a paste gesture carried, before the paste is routed on.</summary>
    [JSExport]
    internal static void SetClipboardText(string text) => BrowserPlatform.SetClipboardText(text);

    [JSExport]
    internal static bool WantsTextInput() => BrowserPlatform.WantsTextInput();

    [JSExport]
    internal static void PointerPan(double x, double y, double screenX, double screenY,
        double deltaX, double deltaY, int modifiers)
        => BrowserPlatform.PointerPan(x, y, screenX, screenY, deltaX, deltaY, (ModifierKeys)modifiers);

    [JSExport]
    internal static void PointerPanRelease() => BrowserPlatform.PointerPanRelease();

    [JSExport]
    internal static void PointerLeave() => BrowserPlatform.PointerLeave();

    [JSExport]
    internal static void PointerCancel() => BrowserPlatform.PointerCancel();

    [JSExport]
    internal static bool KeyDown(string code, int platformKey, int modifiers, bool isRepeat)
        => BrowserPlatform.KeyDown(code, platformKey, (ModifierKeys)modifiers, isRepeat);

    [JSExport]
    internal static bool KeyUp(string code, int platformKey, int modifiers)
        => BrowserPlatform.KeyUp(code, platformKey, (ModifierKeys)modifiers);

    [JSExport]
    internal static void CompositionStart() => BrowserPlatform.CompositionStart();

    [JSExport]
    internal static void CompositionUpdate(string text) => BrowserPlatform.CompositionUpdate(text);

    [JSExport]
    internal static void CompositionEnd(string text) => BrowserPlatform.CompositionEnd(text);

    [JSExport]
    internal static bool TextInput(string text) => BrowserPlatform.TextInput(text);

    [JSExport]
    internal static void FocusChanged(bool focused) => BrowserPlatform.FocusChanged(focused);

    /// <summary>File names the host should fetch, in the order the pages need them.</summary>
    [JSExport]
    internal static string[] ResourceFileNames() => GalleryResources.FileNames;

    /// <summary>Hands one fetched resource to the gallery; the bound pages pick it up on the next frame.</summary>
    [JSExport]
    internal static void ApplyResource(string fileName, byte[] content)
        => GalleryView.Resources.Apply(fileName, content);
}
