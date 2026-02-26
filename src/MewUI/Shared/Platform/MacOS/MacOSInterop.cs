using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Platform.MacOS;

// Cocoa "point" (DIP) coordinate in double precision.
[StructLayout(LayoutKind.Sequential)]
internal readonly struct NSPoint
{
    public readonly double x;
    public readonly double y;

    public NSPoint(double x, double y)
    {
        this.x = x;
        this.y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NSSize
{
    public readonly double width;
    public readonly double height;

    public NSSize(double width, double height)
    {
        this.width = width;
        this.height = height;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NSRect
{
    public readonly NSPoint origin;
    public readonly NSSize size;

    public NSRect(double x, double y, double width, double height)
    {
        origin = new NSPoint(x, y);
        size = new NSSize(width, height);
    }
}

internal static unsafe class MacOSInterop
{
    private static bool _frameworksLoaded;
    private static nint _nsApp;
    private static nint _wakeEvent;
    private static nint _defaultRunLoopMode;
    private static nint _trackingRunLoopMode;
    private static bool _finishedLaunching;

    private static nint SelSharedApplication;
    private static nint SelSetActivationPolicy;
    private static nint SelActivateIgnoringOtherApps;
    private static nint SelFinishLaunching;
    private static nint SelNextEvent;
    private static nint SelSendEvent;
    private static nint SelUpdateWindows;
    private static nint SelKeyWindow;
    private static nint SelTerminate;
    private static nint SelPostEventAtStart;
    private static nint SelMainScreen;
    private static nint SelBackingScaleFactor;
    private static nint SelWindow;
    private static nint SelScreen;
    private static nint SelBounds;
    private static nint SelFrame;
    private static nint SelType;
    private static nint SelLocationInWindow;
    private static nint SelButtonNumber;
    private static nint SelClickCount;
    private static nint SelModifierFlags;
    private static nint SelDeltaX;
    private static nint SelDeltaY;
    private static nint SelScrollingDeltaX;
    private static nint SelScrollingDeltaY;
    private static nint SelKeyCode;
    private static nint SelCharacters;
    private static nint SelCharactersIgnoringModifiers;
    private static nint SelUTF8String;
    private static nint SelRespondsToSelector;
    private static nint SelStandardUserDefaults;
    private static nint SelStringForKey;
    private static nint SelIsVisible;
    private static nint SelInLiveResize;
    private static nint SelGeneralPasteboard;
    private static nint SelClearContents;
    private static nint SelSetStringForType;
    private static nint SelStringForType;
    private static nint SelDefaultCenter;
    private static nint SelAddObserverSelectorNameObject;
    private static nint SelRemoveObserverNameObject;
    private static nint SelAlloc;
    private static nint SelInit;
    private static nint ClsNSApplication;
    private static nint ClsNSEvent;
    private static nint ClsNSDate;
    private static nint ClsNSString;
    private static nint ClsNSScreen;
    private static nint ClsNSUserDefaults;
    private static nint ClsNSPasteboard;
    private static nint ClsNSDistributedNotificationCenter;
    private static nint ClsNSObject;

    private static nint SelOtherEventWithType;
    private static nint SelDateWithTimeIntervalSinceNow;
    private static nint SelDistantFuture;
    private static nint SelNewAutoreleasePool;
    private static nint SelDrainAutoreleasePool;
    private static nint ClsNSAutoreleasePool;
    private static nint _appleInterfaceStyleKey;
    private static nint _pasteboardTypeString;
    private static nint _themeNotificationName;
    private static nint SelThemeChanged;
    private static nint ClsMewUIThemeObserver;
    private static nint _themeObserver;
    private static Action? _themeChangedCallback;
    private static readonly object _themeObserverGate = new();

    public static void EnsureApplicationInitialized()
    {
        if (_nsApp != 0)
        {
            return;
        }

        EnsureFrameworksLoaded();

        SelSharedApplication = ObjC.Sel("sharedApplication");
        SelSetActivationPolicy = ObjC.Sel("setActivationPolicy:");
        SelActivateIgnoringOtherApps = ObjC.Sel("activateIgnoringOtherApps:");
        SelFinishLaunching = ObjC.Sel("finishLaunching");
        SelNextEvent = ObjC.Sel("nextEventMatchingMask:untilDate:inMode:dequeue:");
        SelSendEvent = ObjC.Sel("sendEvent:");
        SelUpdateWindows = ObjC.Sel("updateWindows");
        SelKeyWindow = ObjC.Sel("keyWindow");
        SelTerminate = ObjC.Sel("terminate:");
        SelPostEventAtStart = ObjC.Sel("postEvent:atStart:");
        SelMainScreen = ObjC.Sel("mainScreen");
        SelBackingScaleFactor = ObjC.Sel("backingScaleFactor");
        SelWindow = ObjC.Sel("window");
        SelScreen = ObjC.Sel("screen");
        SelBounds = ObjC.Sel("bounds");
        SelFrame = ObjC.Sel("frame");
        SelType = ObjC.Sel("type");
        SelLocationInWindow = ObjC.Sel("locationInWindow");
        SelButtonNumber = ObjC.Sel("buttonNumber");
        SelClickCount = ObjC.Sel("clickCount");
        SelModifierFlags = ObjC.Sel("modifierFlags");
        SelDeltaX = ObjC.Sel("deltaX");
        SelDeltaY = ObjC.Sel("deltaY");
        SelScrollingDeltaX = ObjC.Sel("scrollingDeltaX");
        SelScrollingDeltaY = ObjC.Sel("scrollingDeltaY");
        SelKeyCode = ObjC.Sel("keyCode");
        SelCharacters = ObjC.Sel("characters");
        SelCharactersIgnoringModifiers = ObjC.Sel("charactersIgnoringModifiers");
        SelUTF8String = ObjC.Sel("UTF8String");
        SelRespondsToSelector = ObjC.Sel("respondsToSelector:");
        SelStandardUserDefaults = ObjC.Sel("standardUserDefaults");
        SelStringForKey = ObjC.Sel("stringForKey:");
        SelIsVisible = ObjC.Sel("isVisible");
        SelInLiveResize = ObjC.Sel("inLiveResize");
        SelGeneralPasteboard = ObjC.Sel("generalPasteboard");
        SelClearContents = ObjC.Sel("clearContents");
        SelSetStringForType = ObjC.Sel("setString:forType:");
        SelStringForType = ObjC.Sel("stringForType:");
        SelDefaultCenter = ObjC.Sel("defaultCenter");
        SelAddObserverSelectorNameObject = ObjC.Sel("addObserver:selector:name:object:");
        SelRemoveObserverNameObject = ObjC.Sel("removeObserver:name:object:");
        SelAlloc = ObjC.Sel("alloc");
        SelInit = ObjC.Sel("init");

        ClsNSApplication = ObjC.GetClass("NSApplication");
        ClsNSEvent = ObjC.GetClass("NSEvent");
        ClsNSDate = ObjC.GetClass("NSDate");
        ClsNSString = ObjC.GetClass("NSString");
        ClsNSScreen = ObjC.GetClass("NSScreen");
        ClsNSUserDefaults = ObjC.GetClass("NSUserDefaults");
        ClsNSAutoreleasePool = ObjC.GetClass("NSAutoreleasePool");
        ClsNSPasteboard = ObjC.GetClass("NSPasteboard");
        ClsNSDistributedNotificationCenter = ObjC.GetClass("NSDistributedNotificationCenter");
        ClsNSObject = ObjC.GetClass("NSObject");

        SelOtherEventWithType = ObjC.Sel("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:");
        SelDateWithTimeIntervalSinceNow = ObjC.Sel("dateWithTimeIntervalSinceNow:");
        SelDistantFuture = ObjC.Sel("distantFuture");
        SelNewAutoreleasePool = ObjC.Sel("new");
        SelDrainAutoreleasePool = ObjC.Sel("drain");

        _nsApp = ObjC.MsgSend_nint(ClsNSApplication, SelSharedApplication);

        // NSApplicationActivationPolicyRegular = 0
        ObjC.MsgSend_void_nint_int(_nsApp, SelSetActivationPolicy, 0);
        ObjC.MsgSend_void_nint_bool(_nsApp, SelActivateIgnoringOtherApps, true);

        // Without finishing launch, some event categories (notably mouse moved / scroll wheel) can be unreliable
        // in a manual NSApplication event loop.
        if (!_finishedLaunching)
        {
            ObjC.MsgSend_void(_nsApp, SelFinishLaunching);
            _finishedLaunching = true;
        }

        // Build a reusable "wake" event.
        _wakeEvent = CreateWakeEvent();
        _defaultRunLoopMode = ObjC.CreateNSString("kCFRunLoopDefaultMode");
        _trackingRunLoopMode = ObjC.CreateNSString("NSEventTrackingRunLoopMode");
        _appleInterfaceStyleKey = ObjC.CreateNSString("AppleInterfaceStyle");
        _pasteboardTypeString = ObjC.CreateNSString("public.utf8-plain-text");
    }

    public static bool TrySetClipboardText(string text)
    {
        EnsureApplicationInitialized();
        if (ClsNSPasteboard == 0 || SelGeneralPasteboard == 0 || SelClearContents == 0 || SelSetStringForType == 0)
        {
            return false;
        }

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        using var pool = new AutoReleasePool();
        var pb = ObjC.MsgSend_nint(ClsNSPasteboard, SelGeneralPasteboard);
        if (pb == 0)
        {
            return false;
        }

        ObjC.MsgSend_void(pb, SelClearContents);
        var nsText = ObjC.CreateNSString(text);
        if (nsText == 0)
        {
            return false;
        }

        var type = _pasteboardTypeString != 0 ? _pasteboardTypeString : ObjC.CreateNSString("public.utf8-plain-text");
        return ObjC.MsgSend_bool_nint_nint(pb, SelSetStringForType, nsText, type);
    }

    public static bool TryGetClipboardText(out string text)
    {
        EnsureApplicationInitialized();
        text = string.Empty;

        if (ClsNSPasteboard == 0 || SelGeneralPasteboard == 0 || SelStringForType == 0 || SelUTF8String == 0)
        {
            return false;
        }

        using var pool = new AutoReleasePool();
        var pb = ObjC.MsgSend_nint(ClsNSPasteboard, SelGeneralPasteboard);
        if (pb == 0)
        {
            return false;
        }

        var type = _pasteboardTypeString != 0 ? _pasteboardTypeString : ObjC.CreateNSString("public.utf8-plain-text");
        var nsString = ObjC.MsgSend_nint_nint(pb, SelStringForType, type);
        if (nsString == 0)
        {
            return false;
        }

        var utf8 = ObjC.MsgSend_nint(nsString, SelUTF8String);
        if (utf8 == 0)
        {
            return false;
        }

        text = Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
        return text.Length != 0;
    }

    public static bool IsWindowVisible(nint nsWindow)
    {
        EnsureApplicationInitialized();
        if (nsWindow == 0 || SelIsVisible == 0)
        {
            return false;
        }

        return ObjC.MsgSend_bool(nsWindow, SelIsVisible);
    }

    public static bool IsWindowInLiveResize(nint nsWindow)
    {
        EnsureApplicationInitialized();
        if (nsWindow == 0 || SelInLiveResize == 0)
        {
            return false;
        }

        return ObjC.MsgSend_bool(nsWindow, SelInLiveResize);
    }

    public static string? GetUserDefaultString(string key)
    {
        EnsureApplicationInitialized();
        if (ClsNSUserDefaults == 0)
        {
            return null;
        }

        var defaults = ObjC.MsgSend_nint(ClsNSUserDefaults, SelStandardUserDefaults);
        if (defaults == 0)
        {
            return null;
        }

        nint nsKey = key == "AppleInterfaceStyle" && _appleInterfaceStyleKey != 0
            ? _appleInterfaceStyleKey
            : ObjC.CreateNSString(key);

        var nsValue = ObjC.MsgSend_nint_nint(defaults, SelStringForKey, nsKey);
        if (nsValue == 0)
        {
            return null;
        }

        var utf8 = ObjC.MsgSend_nint(nsValue, SelUTF8String);
        if (utf8 == 0)
        {
            return null;
        }

        return Marshal.PtrToStringUTF8(utf8);
    }

    public static int GetEventType(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 0;
        }

        return (int)ObjC.MsgSend_long(ev, SelType);
    }

    public static nint GetEventWindow(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_nint(ev, SelWindow);
    }

    public static NSPoint GetEventLocationInWindow(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return default;
        }

        return ObjC.MsgSend_point(ev, SelLocationInWindow);
    }

    public static int GetEventButtonNumber(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 0;
        }

        return (int)ObjC.MsgSend_long(ev, SelButtonNumber);
    }

    public static int GetEventClickCount(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 1;
        }

        int n = (int)ObjC.MsgSend_long(ev, SelClickCount);
        return n <= 0 ? 1 : n;
    }

    public static ulong GetEventModifierFlags(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 0;
        }

        return (ulong)ObjC.MsgSend_ulong(ev, SelModifierFlags);
    }

    public static double GetEventScrollingDeltaX(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_double(ev, SelScrollingDeltaX);
    }

    public static double GetEventScrollingDeltaY(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_double(ev, SelScrollingDeltaY);
    }

    public static double GetEventDeltaX(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_double(ev, SelDeltaX);
    }

    public static double GetEventDeltaY(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_double(ev, SelDeltaY);
    }

    public static int GetEventKeyCode(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return 0;
        }

        return (int)ObjC.MsgSend_ulong(ev, SelKeyCode);
    }

    public static string? GetEventCharactersIgnoringModifiers(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return null;
        }

        var nsString = ObjC.MsgSend_nint(ev, SelCharactersIgnoringModifiers);
        if (nsString == 0)
        {
            return null;
        }

        var utf8 = ObjC.MsgSend_nint(nsString, SelUTF8String);
        if (utf8 == 0)
        {
            return null;
        }

        return Marshal.PtrToStringUTF8(utf8);
    }

    public static string? GetEventCharacters(nint ev)
    {
        EnsureApplicationInitialized();
        if (ev == 0)
        {
            return null;
        }

        var nsString = ObjC.MsgSend_nint(ev, SelCharacters);
        if (nsString == 0)
        {
            return null;
        }

        var utf8 = ObjC.MsgSend_nint(nsString, SelUTF8String);
        if (utf8 == 0)
        {
            return null;
        }

        return Marshal.PtrToStringUTF8(utf8);
    }

    public static string? GetUtf8TextFromNSStringOrAttributedString(nint obj)
    {
        EnsureApplicationInitialized();
        if (obj == 0 || SelUTF8String == 0)
        {
            return null;
        }

        // Never send UTF8String blindly: NSAttributedString/NSMutableAttributedString do not implement it
        // and will throw an Objective-C exception (crash).
        if (SelRespondsToSelector != 0 && ObjC.MsgSend_bool_nint(obj, SelRespondsToSelector, SelUTF8String))
        {
            var utf8Ptr = ObjC.MsgSend_nint(obj, SelUTF8String);
            if (utf8Ptr != 0)
            {
                return Marshal.PtrToStringUTF8(utf8Ptr);
            }
        }

        static bool ShouldLogImeUtf8Fallback()
        {
            var v = Environment.GetEnvironmentVariable("MEWUI_IME_DEBUG");
            if (!string.IsNullOrWhiteSpace(v))
            {
                if (string.Equals(v, "0", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

#if DEBUG
            return true;
#else
            return false;
#endif
        }

        // If obj is an NSAttributedString (or similar), fall back to -string then UTF8String.
        var selString = ObjC.Sel("string");
        if (selString == 0)
        {
            return null;
        }

        if (SelRespondsToSelector != 0 && !ObjC.MsgSend_bool_nint(obj, SelRespondsToSelector, selString))
        {
            return null;
        }

        var nsString = ObjC.MsgSend_nint(obj, selString);
        if (nsString == 0)
        {
            return null;
        }

        if (SelRespondsToSelector != 0 && !ObjC.MsgSend_bool_nint(nsString, SelRespondsToSelector, SelUTF8String))
        {
            return null;
        }

        if (ShouldLogImeUtf8Fallback())
        {
            try
            {
                Console.WriteLine($"[MacOS][IME] {DateTime.Now:HH:mm:ss.fff} UTF8String fallback: obj=0x{obj:x} -> -string -> UTF8String");
            }
            catch
            {
            }
        }

        var utf8Ptr2 = ObjC.MsgSend_nint(nsString, SelUTF8String);
        return utf8Ptr2 != 0 ? Marshal.PtrToStringUTF8(utf8Ptr2) : null;
    }

    public static double GetMainScreenScaleFactor()
    {
        EnsureApplicationInitialized();
        var screen = ObjC.MsgSend_nint(ClsNSScreen, SelMainScreen);
        if (screen == 0)
        {
            return 1.0;
        }

        var scale = ObjC.MsgSend_double(screen, SelBackingScaleFactor);
        return scale <= 0 ? 1.0 : scale;
    }

    public static double GetBackingScaleFactorForView(nint nsView)
    {
        EnsureApplicationInitialized();
        if (nsView == 0)
        {
            return GetMainScreenScaleFactor();
        }

        var window = ObjC.MsgSend_nint(nsView, SelWindow);
        if (window == 0)
        {
            return GetMainScreenScaleFactor();
        }

        var screen = ObjC.MsgSend_nint_nint(window, SelScreen, 0);
        if (screen == 0)
        {
            return GetMainScreenScaleFactor();
        }

        var scale = ObjC.MsgSend_double(screen, SelBackingScaleFactor);
        return scale <= 0 ? 1.0 : scale;
    }

    public static NSRect GetViewBounds(nint nsView)
    {
        EnsureApplicationInitialized();
        if (nsView == 0)
        {
            return default;
        }

        // NOTE: Prefer frame over bounds for live-resize correctness.
        // During window live-resize AppKit can keep bounds stable and visually scale the backing store,
        // while the view's frame size reflects the new geometry.
        return ObjC.MsgSend_rect(nsView, SelFrame);
    }

    private static void EnsureFrameworksLoaded()
    {
        if (_frameworksLoaded)
        {
            return;
        }

        // When running under dotnet/NativeAOT, AppKit/Foundation may not be linked by default because we only
        // import libobjc. objc_getClass will return 0 until the frameworks are loaded.
        //
        // We load frameworks explicitly so NSApplication/NSWindow/NSString classes are registered.
        // Note: NativeLibrary.TryLoad works with .framework binaries when given the full path.
        NativeLibrary.TryLoad("/System/Library/Frameworks/Foundation.framework/Foundation", out _);
        NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);
        // Used by CAMetalLayer/Metal backends (safe to ignore failure on older systems).
        NativeLibrary.TryLoad("/System/Library/Frameworks/QuartzCore.framework/QuartzCore", out _);
        NativeLibrary.TryLoad("/System/Library/Frameworks/Metal.framework/Metal", out _);

        _frameworksLoaded = true;
    }

    public static void RequestTerminate()
    {
        if (_nsApp != 0)
        {
            ObjC.MsgSend_void_nint_nint(_nsApp, SelTerminate, 0);
        }
    }

    public static void PostWakeEvent()
    {
        if (_nsApp == 0 || _wakeEvent == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_nint_bool(_nsApp, SelPostEventAtStart, _wakeEvent, false);
    }

    public static bool TrySetThemeChangedCallback(Action? callback)
    {
        EnsureApplicationInitialized();
        lock (_themeObserverGate)
        {
            _themeChangedCallback = callback;
            if (callback == null)
            {
                UnregisterThemeObserver();
                return true;
            }

            if (EnsureThemeObserver())
            {
                return true;
            }

            _themeChangedCallback = null;
            return false;
        }
    }

    public static bool TryDequeueEvent(out nint ev)
    {
        ev = 0;
        if (_nsApp == 0)
        {
            return false;
        }

        // mask = NSUIntegerMax (all events), untilDate = NSDate(0) (non-blocking poll), mode = default, dequeue = YES
        //
        // IMPORTANT:
        // In AppKit, passing nil for untilDate will block until an event arrives.
        // This method is used by the platform host's "DrainEvents" path which must not block.
        var untilDate = ObjC.MsgSend_nint_double(ClsNSDate, SelDateWithTimeIntervalSinceNow, 0);
        ev = ObjC.MsgSend_nint_nint_nint_nint_bool(_nsApp, SelNextEvent, unchecked((nint)(-1)), untilDate, _defaultRunLoopMode, true);
        if (ev == 0 && _trackingRunLoopMode != 0)
        {
            // During mouse tracking (e.g. scroll/drag), AppKit delivers events in NSEventTrackingRunLoopMode.
            ev = ObjC.MsgSend_nint_nint_nint_nint_bool(_nsApp, SelNextEvent, unchecked((nint)(-1)), untilDate, _trackingRunLoopMode, true);
        }
        return ev != 0;
    }

    public static void WaitForNextEvent(int timeoutMs)
        => WaitForNextEvent(timeoutMs, updateWindows: true);

    public static void WaitForNextEvent(int timeoutMs, bool updateWindows)
    {
        if (_nsApp == 0)
        {
            return;
        }

        using var pool = new AutoReleasePool();

        nint untilDate = 0;
        if (timeoutMs == 0)
        {
            // Non-blocking poll.
            untilDate = ObjC.MsgSend_nint_double(ClsNSDate, SelDateWithTimeIntervalSinceNow, 0);
        }
        else if (timeoutMs > 0)
        {
            double seconds = timeoutMs / 1000.0;
            untilDate = ObjC.MsgSend_nint_double(ClsNSDate, SelDateWithTimeIntervalSinceNow, seconds);
        }
        // timeoutMs < 0 => untilDate = nil => wait indefinitely

        // Wait for any event or until date, but DO NOT dequeue it here.
        // The main loop will dequeue & dispatch via TryDequeueEvent(). Dequeuing here would drop events.
        // Wait for any event or until date, but DO NOT dequeue it here.
        // The main loop will dequeue & dispatch via TryDequeueEvent(). Dequeuing here would drop events.
        _ = ObjC.MsgSend_nint_nint_nint_nint_bool(_nsApp, SelNextEvent, unchecked((nint)(-1)), untilDate, _defaultRunLoopMode, false);
        if (_trackingRunLoopMode != 0)
        {
            _ = ObjC.MsgSend_nint_nint_nint_nint_bool(_nsApp, SelNextEvent, unchecked((nint)(-1)), untilDate, _trackingRunLoopMode, false);
        }

        if (updateWindows)
        {
            // Keep windows responsive (layout, redraw scheduling).
            ObjC.MsgSend_void(_nsApp, SelUpdateWindows);
        }
    }

    public static bool WaitForNextEventDequeue(int timeoutMs, bool updateWindows, out nint ev)
    {
        ev = 0;
        if (_nsApp == 0)
        {
            return false;
        }

        nint untilDate = 0;
        if (timeoutMs == 0)
        {
            // Non-blocking poll.
            untilDate = ObjC.MsgSend_nint_double(ClsNSDate, SelDateWithTimeIntervalSinceNow, 0);
        }
        else if (timeoutMs > 0)
        {
            double seconds = timeoutMs / 1000.0;
            untilDate = ObjC.MsgSend_nint_double(ClsNSDate, SelDateWithTimeIntervalSinceNow, seconds);
        }
        else
        {
            // timeoutMs < 0 => wait indefinitely
            if (SelDistantFuture != 0)
            {
                untilDate = ObjC.MsgSend_nint(ClsNSDate, SelDistantFuture);
            }
        }

        // Dequeue one event to avoid a busy loop when events are perpetually pending.
        ev = ObjC.MsgSend_nint_nint_nint_nint_bool(_nsApp, SelNextEvent, unchecked((nint)(-1)), untilDate, _defaultRunLoopMode, true);
        if (ev == 0 && _trackingRunLoopMode != 0)
        {
            ev = ObjC.MsgSend_nint_nint_nint_nint_bool(_nsApp, SelNextEvent, unchecked((nint)(-1)), untilDate, _trackingRunLoopMode, true);
        }

        if (updateWindows)
        {
            ObjC.MsgSend_void(_nsApp, SelUpdateWindows);
        }

        return ev != 0;
    }

    public static void SendEvent(nint ev)
    {
        if (_nsApp == 0 || ev == 0)
        {
            return;
        }

        using var pool = new AutoReleasePool();
        ObjC.MsgSend_void_nint_nint(_nsApp, SelSendEvent, ev);
        ObjC.MsgSend_void(_nsApp, SelUpdateWindows);
    }

    public static nint GetKeyWindow()
    {
        EnsureApplicationInitialized();
        if (_nsApp == 0 || SelKeyWindow == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_nint(_nsApp, SelKeyWindow);
    }

    private static nint CreateWakeEvent()
    {
        // NSEventTypeApplicationDefined = 15 (historical), but for "other" events we can use subtype and data.
        // We'll use type = 15.
        const int NSEventTypeApplicationDefined = 15;

        // location = {0,0}, modifiers = 0, timestamp = 0, windowNumber = 0, context = nil, subtype = 0, data1 = 0, data2 = 0
        return ObjC.MsgSend_nint_int_point_ulongdouble_int_nint_int_int_int(
            ClsNSEvent,
            SelOtherEventWithType,
            NSEventTypeApplicationDefined,
            0.0,
            0.0,
            0,
            0.0,
            0,
            0,
            0,
            0,
            0);
    }

    private static bool EnsureThemeObserver()
    {
        if (_themeObserver != 0)
        {
            return true;
        }

        if (ClsNSDistributedNotificationCenter == 0 || ClsNSObject == 0 || SelDefaultCenter == 0 ||
            SelAddObserverSelectorNameObject == 0 || SelAlloc == 0 || SelInit == 0)
        {
            return false;
        }

        if (SelThemeChanged == 0)
        {
            SelThemeChanged = ObjC.Sel("handleThemeChanged:");
        }

        if (ClsMewUIThemeObserver == 0)
        {
            const string className = "MewUIThemeObserver";
            var cls = ObjC.GetClass(className);
            if (cls == 0)
            {
                cls = ObjC.AllocateClassPair(ClsNSObject, className);
                if (cls != 0)
                {
                    var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&ThemeChangedHandler;
                    _ = ObjC.AddMethod(cls, SelThemeChanged, imp, "v@:@");
                    ObjC.RegisterClassPair(cls);
                }
            }

            ClsMewUIThemeObserver = cls;
        }

        if (ClsMewUIThemeObserver == 0 || SelThemeChanged == 0)
        {
            return false;
        }

        var observer = ObjC.MsgSend_nint(ClsMewUIThemeObserver, SelAlloc);
        observer = observer != 0 ? ObjC.MsgSend_nint(observer, SelInit) : 0;
        if (observer == 0)
        {
            return false;
        }

        var center = ObjC.MsgSend_nint(ClsNSDistributedNotificationCenter, SelDefaultCenter);
        if (center == 0)
        {
            return false;
        }

        if (_themeNotificationName == 0)
        {
            _themeNotificationName = ObjC.CreateNSString("AppleInterfaceThemeChangedNotification");
        }

        ObjC.MsgSend_void_nint_nint_nint_nint(center, SelAddObserverSelectorNameObject, observer, SelThemeChanged, _themeNotificationName, 0);
        _themeObserver = observer;
        return true;
    }

    private static void UnregisterThemeObserver()
    {
        if (_themeObserver == 0)
        {
            return;
        }

        if (ClsNSDistributedNotificationCenter != 0 && SelDefaultCenter != 0 && SelRemoveObserverNameObject != 0)
        {
            var center = ObjC.MsgSend_nint(ClsNSDistributedNotificationCenter, SelDefaultCenter);
            if (center != 0)
            {
                ObjC.MsgSend_void_nint_nint_nint(center, SelRemoveObserverNameObject, _themeObserver, _themeNotificationName, 0);
            }
        }

        _themeObserver = 0;
    }

    [UnmanagedCallersOnly]
    private static void ThemeChangedHandler(nint self, nint _cmd, nint notification)
    {
        var callback = Volatile.Read(ref _themeChangedCallback);
        callback?.Invoke();
    }


    internal readonly struct AutoReleasePool : IDisposable
    {
        private readonly nint _pool;
        public AutoReleasePool()
        {
            EnsureApplicationInitialized();
            _pool = ObjC.MsgSend_nint(ClsNSAutoreleasePool, SelNewAutoreleasePool);
        }

        public void Dispose()
        {
            if (_pool != 0)
            {
                ObjC.MsgSend_void(_pool, SelDrainAutoreleasePool);
            }
        }
    }
}

internal static unsafe partial class ObjC
{
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static partial nint objc_getClass(byte* name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static partial nint sel_registerName(byte* name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_allocateClassPair")]
    private static partial nint objc_allocateClassPair(nint superclass, byte* name, nuint extraBytes);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_registerClassPair")]
    private static partial void objc_registerClassPair(nint cls);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_addMethod")]
    private static partial byte class_addMethod(nint cls, nint name, nint imp, byte* types);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getProtocol")]
    private static partial nint objc_getProtocol(byte* name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_addProtocol")]
    private static partial byte class_addProtocol(nint cls, nint protocol);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
    private static partial void objc_msgSendSuper_void(ref objc_super super, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
    private static partial void objc_msgSendSuper_void_rect(ref objc_super super, nint selector, NSRect rect);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte objc_msgSend_byte(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte objc_msgSend_byte_nint(nint receiver, nint selector, nint a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_nint(nint receiver, nint selector, nint a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_ulong(nint receiver, nint selector, ulong a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial double objc_msgSend_double(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial long objc_msgSend_long(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial ulong objc_msgSend_ulong(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial NSPoint objc_msgSend_point(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial NSPoint objc_msgSend_point_point(nint receiver, nint selector, NSPoint point);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial NSRect objc_msgSend_NSRect(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_bytePtr(nint receiver, nint selector, byte* a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_nint_nint_nint_byte(nint receiver, nint selector, nint mask, nint untilDate, nint mode, byte dequeue);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_double(nint receiver, nint selector, double seconds);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_double(nint receiver, nint selector, double a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_nint(nint receiver, nint selector, nint a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_intPtr_int(nint receiver, nint selector, int* value, int parameter);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte objc_msgSend_byte_nint_nint(nint receiver, nint selector, nint a0, nint a1);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_nint_byte(nint receiver, nint selector, nint a0, byte a1);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_rect_ulong_int_byte(nint receiver, nint selector, NSRect rect, ulong styleMask, int backing, byte defer);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_intPtr(nint receiver, nint selector, int* a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_rect_nint(nint receiver, nint selector, NSRect rect, nint a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_size(nint receiver, nint selector, NSSize size);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_point(nint receiver, nint selector, NSPoint point);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_size(nint receiver, nint selector, NSSize size);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_int(nint receiver, nint selector, int a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_ulong(nint receiver, nint selector, ulong a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_rect(nint receiver, nint selector, NSRect rect);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_byte(nint receiver, nint selector, byte a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_nint_nint_nint(nint receiver, nint selector, nint a0, nint a1, nint a2);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_nint_nint_nint_nint(nint receiver, nint selector, nint a0, nint a1, nint a2, nint a3);


    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_int_double_double_ulong_double_int_nint_int_int_int(
        nint receiver,
        nint selector,
        int type,
        double x,
        double y,
        ulong modifierFlags,
        double timestamp,
        int windowNumber,
        nint graphicsContext,
        int subtype,
        int data1,
        int data2);

    public static nint GetClass(string name)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
        {
            return objc_getClass(p);
        }
    }

    public static nint Sel(string name)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
        {
            return sel_registerName(p);
        }
    }

    public static nint AllocateClassPair(nint superclass, string name, nuint extraBytes = 0)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
        {
            return objc_allocateClassPair(superclass, p, extraBytes);
        }
    }

    public static void RegisterClassPair(nint cls)
        => objc_registerClassPair(cls);

    public static bool AddMethod(nint cls, nint selector, nint imp, string types)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(types + "\0");
        fixed (byte* p = utf8)
        {
            return class_addMethod(cls, selector, imp, p) != 0;
        }
    }

    public static nint GetProtocol(string name)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
        {
            return objc_getProtocol(p);
        }
    }

    public static bool AddProtocol(nint cls, string protocolName)
    {
        if (cls == 0)
        {
            return false;
        }

        var proto = GetProtocol(protocolName);
        if (proto == 0)
        {
            return false;
        }

        return class_addProtocol(cls, proto) != 0;
    }

    public static void MsgSendSuper_void(ref objc_super super, nint selector)
        => objc_msgSendSuper_void(ref super, selector);

    public static void MsgSendSuper_void_rect(ref objc_super super, nint selector, NSRect rect)
        => objc_msgSendSuper_void_rect(ref super, selector, rect);

    public static nint CreateNSString(string s)
    {
        // NSString stringWithUTF8String:
        nint cls = GetClass("NSString");
        nint sel = Sel("stringWithUTF8String:");
        var utf8 = System.Text.Encoding.UTF8.GetBytes(s + "\0");
        fixed (byte* p = utf8)
        {
            return objc_msgSend_nint_bytePtr(cls, sel, p);
        }
    }

    public static nint MsgSend_nint(nint receiver, nint selector)
    {
        return objc_msgSend_nint(receiver, selector);
    }

    public static bool MsgSend_bool(nint receiver, nint selector)
        => objc_msgSend_byte(receiver, selector) != 0;

    public static bool MsgSend_bool_nint(nint receiver, nint selector, nint a0)
        => objc_msgSend_byte_nint(receiver, selector, a0) != 0;

    public static nint MsgSend_nint_nint(nint receiver, nint selector, nint a0)
    {
        return objc_msgSend_nint_nint(receiver, selector, a0);
    }

    public static nint MsgSend_nint_ulong(nint receiver, nint selector, ulong a0)
        => objc_msgSend_nint_ulong(receiver, selector, a0);

    public static double MsgSend_double(nint receiver, nint selector)
        => objc_msgSend_double(receiver, selector);

    public static long MsgSend_long(nint receiver, nint selector)
        => objc_msgSend_long(receiver, selector);

    public static ulong MsgSend_ulong(nint receiver, nint selector)
        => objc_msgSend_ulong(receiver, selector);

    public static NSPoint MsgSend_point(nint receiver, nint selector)
        => objc_msgSend_point(receiver, selector);

    public static NSPoint MsgSend_point_nint_point(nint receiver, nint selector, NSPoint point)
        => objc_msgSend_point_point(receiver, selector, point);

    public static NSRect MsgSend_rect(nint receiver, nint selector)
    {
        // macOS 12+ environments (including Rosetta/x64 scenarios) may not export objc_msgSend_stret.
        // Use the regular objc_msgSend signature for NSRect returns across architectures.
        return objc_msgSend_NSRect(receiver, selector);
    }

    public static void MsgSend_void_nint_point(nint receiver, nint selector, NSPoint point)
        => objc_msgSend_void_point(receiver, selector, point);

    public static nint MsgSend_nint_nint_nint_nint_bool(nint receiver, nint selector, nint mask, nint untilDate, nint mode, bool dequeue)
    {
        return objc_msgSend_nint_nint_nint_nint_byte(receiver, selector, mask, untilDate, mode, dequeue ? (byte)1 : (byte)0);
    }

    public static nint MsgSend_nint_double(nint receiver, nint selector, double seconds)
    {
        return objc_msgSend_nint_double(receiver, selector, seconds);
    }

    public static void MsgSend_void_nint_double(nint receiver, nint selector, double a0)
    {
        objc_msgSend_void_double(receiver, selector, a0);
    }

    public static void MsgSend_void(nint receiver, nint selector)
    {
        objc_msgSend_void(receiver, selector);
    }

    public static void MsgSend_void_intPtr_int(nint receiver, nint selector, int* value, int parameter)
        => objc_msgSend_void_intPtr_int(receiver, selector, value, parameter);

    public static void MsgSend_void_nint_nint(nint receiver, nint selector, nint a0)
    {
        objc_msgSend_void_nint(receiver, selector, a0);
    }

    public static bool MsgSend_bool_nint_nint(nint receiver, nint selector, nint a0, nint a1)
        => objc_msgSend_byte_nint_nint(receiver, selector, a0, a1) != 0;

    public static nint MsgSend_nint_rect_ulong_int_bool(nint receiver, nint selector, NSRect rect, ulong styleMask, int backing, bool defer)
    {
        return objc_msgSend_nint_rect_ulong_int_byte(receiver, selector, rect, styleMask, backing, defer ? (byte)1 : (byte)0);
    }

    public static void MsgSend_void_nint_size(nint receiver, nint selector, NSSize size)
    {
        objc_msgSend_void_size(receiver, selector, size);
    }

    public static nint MsgSend_nint_size(nint receiver, nint selector, NSSize size)
    {
        return objc_msgSend_nint_size(receiver, selector, size);
    }

    public static void MsgSend_void_nint_int(nint receiver, nint selector, int a0)
    {
        objc_msgSend_void_int(receiver, selector, a0);
    }

    public static void MsgSend_void_nint_ulong(nint receiver, nint selector, ulong a0)
    {
        objc_msgSend_void_ulong(receiver, selector, a0);
    }

    public static void MsgSend_void_nint_bool(nint receiver, nint selector, bool a0)
    {
        objc_msgSend_void_byte(receiver, selector, a0 ? (byte)1 : (byte)0);
    }

    public static void MsgSend_void_nint_nint_bool(nint receiver, nint selector, nint a0, bool a1)
    {
        objc_msgSend_void_nint_byte(receiver, selector, a0, a1 ? (byte)1 : (byte)0);
    }


    public static void MsgSend_void_nint_nint_bool(nint receiver, nint selector, nint a0, byte a1)
    {
        objc_msgSend_void_nint_byte(receiver, selector, a0, a1);
    }

    public static void MsgSend_void_nint_nint_nint(nint receiver, nint selector, nint a0, nint a1, nint a2)
    {
        objc_msgSend_void_nint_nint_nint(receiver, selector, a0, a1, a2);
    }

    public static void MsgSend_void_nint_nint_nint_nint(nint receiver, nint selector, nint a0, nint a1, nint a2, nint a3)
    {
        objc_msgSend_void_nint_nint_nint_nint(receiver, selector, a0, a1, a2, a3);
    }

    public static nint MsgSend_nint_intPtr(nint receiver, nint selector, int* a0)
        => objc_msgSend_nint_intPtr(receiver, selector, a0);

    public static nint MsgSend_nint_rect_nint(nint receiver, nint selector, NSRect rect, nint a0)
        => objc_msgSend_nint_rect_nint(receiver, selector, rect, a0);

    public static nint MsgSend_nint_rect(nint receiver, nint selector, NSRect rect)
        => objc_msgSend_nint_rect(receiver, selector, rect);

    public static nint MsgSend_nint_int_point_ulongdouble_int_nint_int_int_int(
        nint receiver,
        nint selector,
        int type,
        double x,
        double y,
        ulong modifierFlags,
        double timestamp,
        int windowNumber,
        nint graphicsContext,
        int subtype,
        int data1,
        int data2)
    {
        return objc_msgSend_nint_int_double_double_ulong_double_int_nint_int_int_int(
            receiver,
            selector,
            type,
            x,
            y,
            modifierFlags,
            timestamp,
            windowNumber,
            graphicsContext,
            subtype,
            data1,
            data2);
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct objc_super
    {
        public readonly nint receiver;
        public readonly nint super_class;

        public objc_super(nint receiver, nint super_class)
        {
            this.receiver = receiver;
            this.super_class = super_class;
        }
    }
}

internal static partial class CGL
{
    // CGLLockContext / CGLUnlockContext are used to serialize access to a CGLContextObj.
    // This is important for correctness when the drawable is being resized while rendering.
    [LibraryImport("/System/Library/Frameworks/OpenGL.framework/OpenGL", EntryPoint = "CGLLockContext")]
    public static partial void LockContext(nint cglContext);

    [LibraryImport("/System/Library/Frameworks/OpenGL.framework/OpenGL", EntryPoint = "CGLUnlockContext")]
    public static partial void UnlockContext(nint cglContext);
}
