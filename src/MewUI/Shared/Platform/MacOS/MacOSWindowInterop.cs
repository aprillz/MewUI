using System.Runtime.InteropServices;

using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.MacOS;

internal static unsafe class MacOSWindowInterop
{
    // Titled | Closable | Miniaturizable | Resizable | FullSizeContentView
    // Visually borderless via titlebarAppearsTransparent + hidden buttons,
    // but retains miniaturize/zoom functionality and render surface stability.
    internal const ulong TransparentStyleMask = 1ul | 2ul | 4ul | 8ul | (1ul << 15);

    // NSWindowStyleMaskTitled | Closable | Miniaturizable | Resizable
    private const ulong DefaultStyleMask = 1ul | 2ul | 4ul | 8ul;

    // NSWindowStyleMaskTitled | Closable
    private const ulong DialogStyleMask = 1ul | 2ul;

    private const int NSBackingStoreBuffered = 2;
    private const ulong NSViewWidthSizable = 2;
    private const ulong NSViewHeightSizable = 16;
    private static readonly Dictionary<nint, WeakReference<MacOSWindowBackend>> _windowCloseTargets = new();
    private static readonly Dictionary<nint, WeakReference<MacOSWindowBackend>> _textInputTargets = new();
    private static readonly Dictionary<nint, WeakReference<MacOSWindowBackend>> _metalLayerTargets = new();
    private static bool _initialized;

    private static nint ClsNSWindow;
    private static nint ClsNSString;
    private static nint ClsNSAppearance;
    private static nint ClsNSColor;
    private static nint ClsNSArray;
    private static nint ClsMewUITextInputView;

    private static nint SelAlloc;
    private static nint SelInitWithContentRect;
    private static nint SelMakeKeyAndOrderFront;
    private static nint SelOrderFront;
    private static nint SelClose;
    private static nint SelPerformClose;
    private static nint SelSetTitle;
    private static nint SelSetContentSize;
    private static nint SelSetContentView;
    private static nint SelSetAcceptsMouseMovedEvents;
    private static nint SelSetAnimationBehavior;
    private static nint SelSetIgnoresMouseEvents;
    private static nint SelCenter;
    private static nint SelRelease;
    private static nint SelSetAppearance;
    private static nint SelSetAlphaValue;
    private static nint SelSetBackgroundColor;
    private static nint SelInit;
    private static nint SelInitWithFrame;
    private static nint SelSetLayer;
    private static nint SelSetDelegate;
    private static nint SelSetDrawableSize;
    private static nint SelSetContentsScale;
    private static nint SelSetPresentsWithTransaction;
    private static nint SelSetAllowsNextDrawableTimeout;
    private static nint SelSetDisplaySyncEnabled;
    private static nint SelSetContentMinSize;
    private static nint SelSetContentMaxSize;
    private static nint SelSetParentWindow;
    private static nint SelSetLevel;
    private static nint SelLevel;
    private static nint SelMakeFirstResponder;
    private static nint SelSetStyleMask;
    private static nint SelStyleMask;
    private static nint SelSetTitleVisibility;
    private static nint SelSetTitlebarAppearsTransparent;
    private static nint SelSetMovableByWindowBackground;
    private static nint SelSetFrameOrigin;
    private static nint SelSetFrame;
    private static nint SelFrame;
    private static nint SelScreen;
    private static nint SelStandardWindowButton;
    private static nint SelSetHidden;
    private static nint SelConvertPointToScreen;
    private static nint SelConvertPointFromScreen;
    private static nint SelWindow;
    private static nint SelWindowShouldClose;
    private static nint SelWindowWillClose;
    private static nint SelObject;
    private static nint SelInterpretKeyEvents;
    private static nint SelArrayWithObject;

    private static nint SelSetAutoresizingMask;
    private static nint SelSetOpaque;
    private static nint SelInLiveResize;
    private static nint SelSetNeedsDisplay;
    private static nint SelSetNeedsDisplayNoArgs;
    private static nint SelDisplayIfNeeded;
    private static nint SelDisplay;
    private static nint SelSetWantsLayer;
    private static nint SelSetLayerContentsRedrawPolicy;
    private static nint SelLayer;
    private static nint SelSetNeedsDisplayOnBoundsChange;
    private static nint SelClearColor;
    private static nint SelRegisterForDraggedTypes;
    private static nint SelDraggingPasteboard;
    private static nint SelDraggingLocation;
    private static nint SelPropertyListForType;
    private static nint SelCount;
    private static nint SelObjectAtIndex;
    private static nint SelUTF8String;
    private static nint SelAppearanceNamed;
    private static nint _appearanceNameAqua;
    private static nint _appearanceNameDarkAqua;

    private static nint ClsNSView;
    private static nint ClsCAMetalLayer;
    private static nint ClsNSObject;
    private static nint ClsMewUIMetalLayerDelegate;
    private static nint _sharedMetalLayerDelegate;
    private static nint ClsMewUIWindow;
    private static nint ClsMewUIWindowDelegate;
    private static nint ClsNSPanel;
    private static nint ClsMewUIPanel;
    private static nint _sharedWindowDelegate;

    // NSCursor
    private static nint ClsNSCursor;

    private static nint SelArrowCursor;
    private static nint SelIBeamCursor;
    private static nint SelCrosshairCursor;
    private static nint SelPointingHandCursor;
    private static nint SelResizeLeftRightCursor;
    private static nint SelResizeUpDownCursor;
    private static nint SelOperationNotAllowedCursor;
    private static nint SelOpenHandCursor;
    private static nint SelCursorSet;

    public static void SetCursor(CursorType cursorType)
    {
        EnsureInitialized();
        if (ClsNSCursor == 0 || SelCursorSet == 0)
        {
            return;
        }

        nint sel = cursorType switch
        {
            CursorType.IBeam => SelIBeamCursor,
            CursorType.Cross => SelCrosshairCursor,
            CursorType.Hand => SelPointingHandCursor,
            CursorType.SizeWE => SelResizeLeftRightCursor,
            CursorType.SizeNS => SelResizeUpDownCursor,
            CursorType.No => SelOperationNotAllowedCursor,
            CursorType.SizeAll => SelOpenHandCursor,
            // macOS has no direct equivalents for SizeNWSE, SizeNESW, UpArrow, Wait, Help.
            // Fall back to arrow cursor.
            _ => SelArrowCursor,
        };

        if (sel == 0)
        {
            return;
        }

        nint cursor = ObjC.MsgSend_nint(ClsNSCursor, sel);
        if (cursor != 0)
        {
            ObjC.MsgSend_void(cursor, SelCursorSet);
        }
    }

    public static void SetWindowOpacity(nint window, double opacity)
    {
        EnsureInitialized();
        if (window == 0 || SelSetAlphaValue == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_double(window, SelSetAlphaValue, opacity);
    }

    public static void SetMetalLayerDisplaySyncEnabled(nint metalLayer, bool enabled)
    {
        EnsureInitialized();
        if (metalLayer == 0 || SelSetDisplaySyncEnabled == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_bool(metalLayer, SelSetDisplaySyncEnabled, enabled);
    }

    public static void SetMetalLayerPresentsWithTransaction(nint metalLayer, bool enabled)
    {
        EnsureInitialized();
        if (metalLayer == 0 || SelSetPresentsWithTransaction == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_bool(metalLayer, SelSetPresentsWithTransaction, enabled);
    }

    public static void SetWindowEnabled(nint window, bool enabled)
    {
        EnsureInitialized();
        if (window == 0 || SelSetIgnoresMouseEvents == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_bool(window, SelSetIgnoresMouseEvents, !enabled);
    }

    public static void ActivateWindow(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelMakeKeyAndOrderFront == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_nint(window, SelMakeKeyAndOrderFront, 0);
    }

    public static void SetOwnerWindow(nint window, nint ownerWindow)
    {
        EnsureInitialized();
        if (window == 0 || SelSetParentWindow == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_nint(window, SelSetParentWindow, ownerWindow);
    }

    public static long GetWindowLevel(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelLevel == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_long(window, SelLevel);
    }

    public static void SetWindowLevel(nint window, long level)
    {
        EnsureInitialized();
        if (window == 0 || SelSetLevel == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_int(window, SelSetLevel, unchecked((int)level));
    }

    public static ulong GetWindowStyleMask(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelStyleMask == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_ulong(window, SelStyleMask);
    }

    public static void SetWindowStyleMask(nint window, ulong styleMask)
    {
        EnsureInitialized();
        if (window == 0 || SelSetStyleMask == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_ulong(window, SelSetStyleMask, styleMask);
    }

    public static void ApplyContentSizeConstraints(nint window, Window mewWindow)
    {
        EnsureInitialized();
        if (window == 0)
        {
            return;
        }

        var ws = mewWindow.WindowSize;
        double minW = ws.MinWidth;
        double minH = ws.MinHeight;
        double maxW = ws.MaxWidth;
        double maxH = ws.MaxHeight;

        if (SelSetContentMinSize != 0)
        {
            var minSize = new NSSize(
                minW > 0 ? minW : 0,
                minH > 0 ? minH : 0);
            ObjC.MsgSend_void_nint_size(window, SelSetContentMinSize, minSize);
        }

        if (SelSetContentMaxSize != 0)
        {
            var maxSize = new NSSize(
                !double.IsPositiveInfinity(maxW) ? maxW : 10000,
                !double.IsPositiveInfinity(maxH) ? maxH : 10000);
            ObjC.MsgSend_void_nint_size(window, SelSetContentMaxSize, maxSize);
        }
    }

    public static NSRect GetWindowFrame(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelFrame == 0)
        {
            return default;
        }

        return ObjC.MsgSend_rect(window, SelFrame);
    }

    public static NSRect GetScreenFrame(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelScreen == 0 || SelFrame == 0)
        {
            return default;
        }

        var screen = ObjC.MsgSend_nint(window, SelScreen);
        if (screen == 0)
        {
            return default;
        }

        return ObjC.MsgSend_rect(screen, SelFrame);
    }

    public static nint GetWindowScreen(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelScreen == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_nint(window, SelScreen);
    }

    public static void SetWindowPosition(nint window, double leftDip, double topDip)
    {
        EnsureInitialized();
        if (window == 0 || SelSetFrameOrigin == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_point(window, SelSetFrameOrigin, new NSPoint(leftDip, topDip));
    }

    public static void SetViewFrame(nint view, double widthDip, double heightDip)
    {
        EnsureInitialized();
        if (view == 0 || SelSetFrame == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_rect(view, SelSetFrame, new NSRect(0, 0, widthDip, heightDip));
    }

    public static NSPoint WindowConvertPointToScreen(nint window, NSPoint point)
    {
        EnsureInitialized();
        if (window == 0 || SelConvertPointToScreen == 0)
        {
            return point;
        }

        return ObjC.MsgSend_point_nint_point(window, SelConvertPointToScreen, point);
    }

    public static NSPoint WindowConvertPointFromScreen(nint window, NSPoint point)
    {
        EnsureInitialized();
        if (window == 0 || SelConvertPointFromScreen == 0)
        {
            return point;
        }

        return ObjC.MsgSend_point_nint_point(window, SelConvertPointFromScreen, point);
    }

    public static void SetTitlebarForTransparency(nint window, bool transparent)
    {
        EnsureInitialized();
        if (window == 0)
        {
            return;
        }

        // NSWindowTitleVisibilityVisible=0, Hidden=1
        if (SelSetTitleVisibility != 0)
        {
            ObjC.MsgSend_void_nint_int(window, SelSetTitleVisibility, transparent ? 1 : 0);
        }

        if (SelSetTitlebarAppearsTransparent != 0)
        {
            ObjC.MsgSend_void_nint_bool(window, SelSetTitlebarAppearsTransparent, transparent);
        }

        if (SelSetMovableByWindowBackground != 0)
        {
            // Keep window drag under explicit control (e.g., custom drag zones),
            // so transparent windows don't drag from arbitrary background clicks.
            ObjC.MsgSend_void_nint_bool(window, SelSetMovableByWindowBackground, false);
        }
    }

    public static nint GetWindowFromView(nint view)
    {
        EnsureInitialized();
        if (view == 0 || SelWindow == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_nint(view, SelWindow);
    }

    public static nint GetWindowFromNotification(nint notification)
    {
        EnsureInitialized();
        if (notification == 0 || SelObject == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_nint(notification, SelObject);
    }

    public static void SetWindowTransparency(nint window, nint view, bool allowsTransparency)
    {
        EnsureInitialized();
        if (window == 0)
        {
            return;
        }

        // NSWindow uses setOpaque + backgroundColor to control compositing.
        if (SelSetOpaque != 0)
        {
            ObjC.MsgSend_void_nint_bool(window, SelSetOpaque, !allowsTransparency);
        }

        if (allowsTransparency && ClsNSColor != 0 && SelClearColor != 0 && SelSetBackgroundColor != 0)
        {
            var clear = ObjC.MsgSend_nint(ClsNSColor, SelClearColor);
            if (clear != 0)
            {
                ObjC.MsgSend_void_nint_nint(window, SelSetBackgroundColor, clear);
            }
        }

        // Also ensure the content view is not treated as opaque.
        if (view != 0 && SelSetOpaque != 0)
        {
            ObjC.MsgSend_void_nint_bool(view, SelSetOpaque, !allowsTransparency);
        }
    }

    public static void SetLayerOpaque(nint layer, bool opaque)
    {
        EnsureInitialized();
        if (layer == 0 || SelSetOpaque == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_bool(layer, SelSetOpaque, opaque);
    }

    public static void SetFirstResponder(nint window, nint responder)
    {
        EnsureInitialized();
        if (window == 0 || responder == 0 || SelMakeFirstResponder == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_nint(window, SelMakeFirstResponder, responder);
    }

    public static void RegisterTextInputTarget(nint view, MacOSWindowBackend backend)
    {
        if (view == 0)
        {
            return;
        }

        lock (_textInputTargets)
        {
            _textInputTargets[view] = new WeakReference<MacOSWindowBackend>(backend);
        }
    }

    public static void RegisterForDragDrop(nint view)
    {
        EnsureInitialized();
        if (view == 0 || ClsNSArray == 0 || SelArrayWithObject == 0 || SelRegisterForDraggedTypes == 0)
        {
            return;
        }

        var fileNamesType = ObjC.CreateNSString("NSFilenamesPboardType");
        if (fileNamesType == 0)
        {
            return;
        }

        var array = ObjC.MsgSend_nint_nint(ClsNSArray, SelArrayWithObject, fileNamesType);
        if (array != 0)
        {
            ObjC.MsgSend_void_nint_nint(view, SelRegisterForDraggedTypes, array);
        }
    }

    public static void UnregisterFromDragDrop(nint view)
    {
        EnsureInitialized();
        if (view == 0 || ClsNSArray == 0 || SelRegisterForDraggedTypes == 0) return;

        // Re-registering with an empty types array effectively clears the dragged types on the NSView.
        var emptyArray = ObjC.MsgSend_nint(ClsNSArray, ObjC.Sel("array"));
        if (emptyArray != 0)
        {
            ObjC.MsgSend_void_nint_nint(view, SelRegisterForDraggedTypes, emptyArray);
        }
    }

    public static void UnregisterTextInputTarget(nint view)
    {
        if (view == 0)
        {
            return;
        }

        lock (_textInputTargets)
        {
            _textInputTargets.Remove(view);
        }
    }

    public static void InterpretKeyEvent(nint view, nint ev)
    {
        EnsureInitialized();
        EnsureTextInputViewSubclass();

        if (view == 0 || ev == 0 || ClsNSArray == 0 || SelArrayWithObject == 0 || SelInterpretKeyEvents == 0)
        {
            return;
        }

        var array = ObjC.MsgSend_nint_nint(ClsNSArray, SelArrayWithObject, ev);
        if (array != 0)
        {
            ObjC.MsgSend_void_nint_nint(view, SelInterpretKeyEvents, array);
        }
    }

    public static void RegisterMetalLayerTarget(nint metalLayer, MacOSWindowBackend backend)
    {
        if (metalLayer == 0)
        {
            return;
        }

        lock (_metalLayerTargets)
        {
            _metalLayerTargets[metalLayer] = new WeakReference<MacOSWindowBackend>(backend);
        }
    }

    public static void UnregisterMetalLayerTarget(nint metalLayer)
    {
        if (metalLayer == 0)
        {
            return;
        }

        lock (_metalLayerTargets)
        {
            _metalLayerTargets.Remove(metalLayer);
        }
    }

    public static nint CreateWindow(string title, double widthDip, double heightDip, bool allowsTransparency, bool isDialog, bool isToolWindow)
    {
        EnsureInitialized();

        nint windowClass;
        ulong styleMask;
        if (allowsTransparency)
        {
            // Borderless windows (AllowsTransparency) need MewUIWindow subclass for canBecomeKeyWindow.
            EnsureMewUIWindowClass();
            windowClass = ClsMewUIWindow != 0 ? ClsMewUIWindow : ClsNSWindow;
            styleMask = TransparentStyleMask;
        }
        else if (isToolWindow)
        {
            // Floating tool/utility window: an NSPanel with the utility-window mask (thin title bar, floats
            // above the app). Close button only — no miniaturizable. MewUIPanel subclass so the panel can
            // become key for text input. (UtilityWindow=1<<4 only takes effect on NSPanel, not NSWindow.)
            EnsureMewUIPanelClass();
            windowClass = ClsMewUIPanel != 0 ? ClsMewUIPanel : (ClsNSPanel != 0 ? ClsNSPanel : ClsNSWindow);
            const ulong NSWindowStyleMaskUtilityWindow = 1ul << 4;
            styleMask = NSWindowStyleMaskUtilityWindow | 1ul /*Titled*/ | 2ul /*Closable*/ | 8ul /*Resizable*/;
        }
        else
        {
            windowClass = ClsNSWindow;
            styleMask = isDialog ? DialogStyleMask : DefaultStyleMask;
        }

        var win = ObjC.MsgSend_nint(windowClass, SelAlloc);
        if (win == 0)
        {
            return 0;
        }

        var rect = new NSRect(0, 0, widthDip, heightDip);
        win = ObjC.MsgSend_nint_rect_ulong_int_bool(win, SelInitWithContentRect, rect, styleMask, NSBackingStoreBuffered, false);
        if (win == 0)
        {
            return 0;
        }

        SetTitle(win, title);
        if (isDialog || isToolWindow)
        {
            // Close-only chrome (hide miniaturize/zoom buttons).
            HideDialogChromeButtons(win);
        }
        // MouseMoved events are not delivered unless this is enabled.
        ObjC.MsgSend_void_nint_bool(win, SelSetAcceptsMouseMovedEvents, true);
        if (isToolWindow)
        {
            // A utility panel only becomes key "if needed" by default; force it so text fields receive input.
            ObjC.MsgSend_void_nint_bool(win, ObjC.Sel("setBecomesKeyOnlyIfNeeded:"), false);
        }
        return win;
    }

    public static void HideDialogChromeButtons(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelStandardWindowButton == 0 || SelSetHidden == 0)
        {
            return;
        }

        // NSWindowButtonMiniaturizeButton = 1, NSWindowButtonZoomButton = 2
        var miniaturizeButton = ObjC.MsgSend_nint_ulong(window, SelStandardWindowButton, 1ul);
        if (miniaturizeButton != 0)
        {
            ObjC.MsgSend_void_nint_bool(miniaturizeButton, SelSetHidden, true);
        }

        var zoomButton = ObjC.MsgSend_nint_ulong(window, SelStandardWindowButton, 2ul);
        if (zoomButton != 0)
        {
            ObjC.MsgSend_void_nint_bool(zoomButton, SelSetHidden, true);
        }
    }

    public static void HideCloseButton(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelStandardWindowButton == 0 || SelSetHidden == 0)
        {
            return;
        }

        // NSWindowCloseButton = 0
        var closeButton = ObjC.MsgSend_nint_ulong(window, SelStandardWindowButton, 0ul);
        if (closeButton != 0)
        {
            ObjC.MsgSend_void_nint_bool(closeButton, SelSetHidden, true);
        }
    }

    public static void SetAlertPanelAnimation(nint window)
    {
        EnsureInitialized();
        // NSAnimationBehaviorAlertPanel = 3
        ObjC.MsgSend_void_nint_nint(window, SelSetAnimationBehavior, 3);
    }

    public static void ShowWindow(nint window)
    {
        EnsureInitialized();
        ObjC.MsgSend_void_nint_nint(window, SelMakeKeyAndOrderFront, 0);
    }

    // Orders the window front WITHOUT making it key/main (no activation). Used for input-transparent overlays.
    public static void OrderFrontWindow(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelOrderFront == 0)
        {
            return;
        }
        ObjC.MsgSend_void_nint_nint(window, SelOrderFront, 0);
    }

    public static void CloseWindow(nint window)
    {
        EnsureInitialized();
        if (SelPerformClose != 0)
        {
            ObjC.MsgSend_void_nint_nint(window, SelPerformClose, 0);
        }
        else
        {
            ObjC.MsgSend_void(window, SelClose);
        }
    }

    // Closes directly via -[NSWindow close], bypassing performClose:. performClose: is a no-op for borderless
    // windows (they lack NSWindowStyleMaskClosable), so input-transparent overlays must close this way.
    public static void CloseWindowImmediate(nint window)
    {
        EnsureInitialized();
        if (SelClose != 0)
        {
            ObjC.MsgSend_void(window, SelClose);
        }
    }

    public static void SetTitle(nint window, string title)
    {
        EnsureInitialized();
        nint ns = ObjC.CreateNSString(title);
        ObjC.MsgSend_void_nint_nint(window, SelSetTitle, ns);
    }

    public static void SetClientSize(nint window, double widthDip, double heightDip)
    {
        EnsureInitialized();
        ObjC.MsgSend_void_nint_size(window, SelSetContentSize, new NSSize(widthDip, heightDip));
    }

    public static void CenterWindow(nint window)
    {
        EnsureInitialized();
        if (window == 0)
        {
            return;
        }
        if (SelCenter != 0)
        {
            ObjC.MsgSend_void(window, SelCenter);
        }
    }

    public static void ReleaseWindow(nint window)
    {
        EnsureInitialized();
        ObjC.MsgSend_void(window, SelRelease);
    }

    public static void SetWindowAppearance(nint window, bool isDark)
    {
        EnsureInitialized();

        var name = isDark ? _appearanceNameDarkAqua : _appearanceNameAqua;
        if (name == 0 || ClsNSAppearance == 0)
        {
            return;
        }

        var appearance = ObjC.MsgSend_nint_nint(ClsNSAppearance, SelAppearanceNamed, name);
        ObjC.MsgSend_void_nint_nint(window, SelSetAppearance, appearance);
    }

    public static void ClearWindowAppearance(nint window)
    {
        EnsureInitialized();
        ObjC.MsgSend_void_nint_nint(window, SelSetAppearance, 0);
    }

    public static bool IsViewInLiveResize(nint view)
    {
        EnsureInitialized();
        return view != 0 && ObjC.MsgSend_bool(view, SelInLiveResize);
    }

    public static void DisplayIfNeeded(nint nsView)
    {
        EnsureInitialized();
        if (nsView == 0)
        {
            return;
        }

        if (SelSetNeedsDisplay != 0)
        {
            ObjC.MsgSend_void_nint_bool(nsView, SelSetNeedsDisplay, true);
        }

        // During live-resize, AppKit can keep showing a scaled cached frame until it decides to redraw.
        // Calling display forces a synchronous draw cycle for this view.
        if (SelDisplay != 0)
        {
            ObjC.MsgSend_void(nsView, SelDisplay);
            return;
        }

        if (SelDisplayIfNeeded != 0)
        {
            ObjC.MsgSend_void(nsView, SelDisplayIfNeeded);
        }
    }

    public static void SetNeedsDisplay(nint nsView)
    {
        EnsureInitialized();
        if (nsView == 0 || SelSetNeedsDisplay == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_bool(nsView, SelSetNeedsDisplay, true);
    }

    public static void DisplayLayerIfNeeded(nint layer)
    {
        EnsureInitialized();
        if (layer == 0)
        {
            return;
        }

        // CALayer/CAMetalLayer does not implement setNeedsDisplay:(BOOL). It implements setNeedsDisplay (no args)
        // and display/displayIfNeeded (no args).
        if (SelSetNeedsDisplayNoArgs != 0)
        {
            ObjC.MsgSend_void(layer, SelSetNeedsDisplayNoArgs);
        }

        if (SelDisplay != 0)
        {
            ObjC.MsgSend_void(layer, SelDisplay);
            return;
        }

        if (SelDisplayIfNeeded != 0)
        {
            ObjC.MsgSend_void(layer, SelDisplayIfNeeded);
        }
    }

    public static void SetLayerNeedsDisplay(nint layer)
    {
        EnsureInitialized();
        if (layer == 0 || SelSetNeedsDisplayNoArgs == 0)
        {
            return;
        }

        ObjC.MsgSend_void(layer, SelSetNeedsDisplayNoArgs);
    }

    // Structs used in objc_msgSend signatures must be blittable and have the exact native layout.
    // Keep them internal so ObjC helper can reference them.
    public static (nint View, nint Layer) AttachMetalLayerView(nint window, double widthDip, double heightDip)
    {
        EnsureInitialized();
        EnsureTextInputViewSubclass();

        if (ClsNSView == 0 || ClsCAMetalLayer == 0)
        {
            return (0, 0);
        }

        var viewClass = ClsMewUITextInputView != 0 ? ClsMewUITextInputView : ClsNSView;
        var view = ObjC.MsgSend_nint(viewClass, SelAlloc);
        var rect = new NSRect(0, 0, widthDip, heightDip);
        view = view != 0 ? ObjC.MsgSend_nint_rect(view, SelInitWithFrame, rect) : 0;
        if (view == 0)
        {
            return (0, 0);
        }

        // View settings (recipe).
        if (SelSetWantsLayer != 0)
        {
            ObjC.MsgSend_void_nint_bool(view, SelSetWantsLayer, true);
        }

        // NSViewLayerContentsRedrawPolicy.DuringViewResize = 2
        const int NSViewLayerContentsRedrawDuringViewResize = 2;
        if (SelSetLayerContentsRedrawPolicy != 0)
        {
            ObjC.MsgSend_void_nint_int(view, SelSetLayerContentsRedrawPolicy, NSViewLayerContentsRedrawDuringViewResize);
        }

        ObjC.MsgSend_void_nint_ulong(view, SelSetAutoresizingMask, NSViewWidthSizable | NSViewHeightSizable);

        // Create CAMetalLayer.
        var layer = ObjC.MsgSend_nint(ClsCAMetalLayer, SelAlloc);
        layer = layer != 0 ? ObjC.MsgSend_nint(layer, SelInit) : 0;
        if (layer == 0)
        {
            return (view, 0);
        }

        // Layer settings (recipe).
        if (SelSetNeedsDisplayOnBoundsChange != 0)
        {
            ObjC.MsgSend_void_nint_bool(layer, SelSetNeedsDisplayOnBoundsChange, true);
        }

        // CALayerAutoresizingMask: kCALayerWidthSizable=2, kCALayerHeightSizable=16
        const ulong CALayerWidthSizable = 1ul << 1;
        const ulong CALayerHeightSizable = 1ul << 4;
        if (SelSetAutoresizingMask != 0)
        {
            ObjC.MsgSend_void_nint_ulong(layer, SelSetAutoresizingMask, CALayerWidthSizable | CALayerHeightSizable);
        }

        // presentsWithTransaction is managed dynamically by MacOSWindowBackend:
        // true only during live-resize (fixes jitter), false otherwise (preserves VSync pacing).
        if (SelSetPresentsWithTransaction != 0)
        {
            ObjC.MsgSend_void_nint_bool(layer, SelSetPresentsWithTransaction, false);
        }

        if (SelSetAllowsNextDrawableTimeout != 0)
        {
            ObjC.MsgSend_void_nint_bool(layer, SelSetAllowsNextDrawableTimeout, false);
        }

        // Use shared displayLayer: delegate so rendering happens aligned with AppKit transactions.
        if (_sharedMetalLayerDelegate != 0 && SelSetDelegate != 0)
        {
            ObjC.MsgSend_void_nint_nint(layer, SelSetDelegate, _sharedMetalLayerDelegate);
        }

        if (SelSetLayer != 0)
        {
            ObjC.MsgSend_void_nint_nint(view, SelSetLayer, layer);
        }

        ObjC.MsgSend_void_nint_nint(window, SelSetContentView, view);
        return (view, layer);
    }

    public static void UpdateMetalLayerDrawableSize(nint metalLayer, double widthDip, double heightDip, double dpiScale)
    {
        EnsureInitialized();

        if (metalLayer == 0)
        {
            return;
        }

        if (dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        double widthPx = Math.Max(1.0, Math.Ceiling(widthDip * dpiScale));
        double heightPx = Math.Max(1.0, Math.Ceiling(heightDip * dpiScale));

        if (SelSetDrawableSize != 0)
        {
            ObjC.MsgSend_void_nint_size(metalLayer, SelSetDrawableSize, new NSSize(widthPx, heightPx));
        }

        if (SelSetContentsScale != 0)
        {
            ObjC.MsgSend_void_nint_double(metalLayer, SelSetContentsScale, dpiScale);
        }
    }

    internal static void RegisterWindowCloseTarget(nint window, MacOSWindowBackend backend)
    {
        if (window == 0 || backend == null)
        {
            return;
        }

        EnsureWindowDelegate();
        if (_sharedWindowDelegate != 0 && SelSetDelegate != 0)
        {
            ObjC.MsgSend_void_nint_nint(window, SelSetDelegate, _sharedWindowDelegate);
        }

        lock (_windowCloseTargets)
        {
            _windowCloseTargets[window] = new WeakReference<MacOSWindowBackend>(backend);
        }
    }

    internal static void UnregisterWindowCloseTarget(nint window)
    {
        if (window == 0)
        {
            return;
        }

        lock (_windowCloseTargets)
        {
            _windowCloseTargets.Remove(window);
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        // Ensure frameworks are loaded and NSApplication is initialized before resolving classes/selectors.
        MacOSInterop.EnsureApplicationInitialized();

        ClsNSWindow = ObjC.GetClass("NSWindow");
        ClsNSString = ObjC.GetClass("NSString");
        ClsNSArray = ObjC.GetClass("NSArray");
        ClsNSAppearance = ObjC.GetClass("NSAppearance");
        ClsNSColor = ObjC.GetClass("NSColor");

        SelAlloc = ObjC.Sel("alloc");
        SelInitWithContentRect = ObjC.Sel("initWithContentRect:styleMask:backing:defer:");
        SelMakeKeyAndOrderFront = ObjC.Sel("makeKeyAndOrderFront:");
        SelOrderFront = ObjC.Sel("orderFront:");
        SelClose = ObjC.Sel("close");
        SelPerformClose = ObjC.Sel("performClose:");
        SelSetTitle = ObjC.Sel("setTitle:");
        SelSetContentSize = ObjC.Sel("setContentSize:");
        SelSetContentView = ObjC.Sel("setContentView:");
        SelSetAcceptsMouseMovedEvents = ObjC.Sel("setAcceptsMouseMovedEvents:");
        SelSetAnimationBehavior = ObjC.Sel("setAnimationBehavior:");
        SelSetIgnoresMouseEvents = ObjC.Sel("setIgnoresMouseEvents:");
        SelCenter = ObjC.Sel("center");
        SelRelease = ObjC.Sel("release");
        SelSetAppearance = ObjC.Sel("setAppearance:");
        SelSetAlphaValue = ObjC.Sel("setAlphaValue:");
        SelSetBackgroundColor = ObjC.Sel("setBackgroundColor:");
        SelInit = ObjC.Sel("init");
        SelInitWithFrame = ObjC.Sel("initWithFrame:");
        SelSetLayer = ObjC.Sel("setLayer:");
        SelSetDelegate = ObjC.Sel("setDelegate:");
        SelSetDrawableSize = ObjC.Sel("setDrawableSize:");
        SelSetContentsScale = ObjC.Sel("setContentsScale:");
        SelSetPresentsWithTransaction = ObjC.Sel("setPresentsWithTransaction:");
        SelSetAllowsNextDrawableTimeout = ObjC.Sel("setAllowsNextDrawableTimeout:");
        SelSetDisplaySyncEnabled = ObjC.Sel("setDisplaySyncEnabled:");
        SelSetContentMinSize = ObjC.Sel("setContentMinSize:");
        SelSetContentMaxSize = ObjC.Sel("setContentMaxSize:");
        SelSetParentWindow = ObjC.Sel("setParentWindow:");
        SelSetLevel = ObjC.Sel("setLevel:");
        SelLevel = ObjC.Sel("level");
        SelMakeFirstResponder = ObjC.Sel("makeFirstResponder:");
        SelSetStyleMask = ObjC.Sel("setStyleMask:");
        SelStyleMask = ObjC.Sel("styleMask");
        SelSetTitleVisibility = ObjC.Sel("setTitleVisibility:");
        SelSetTitlebarAppearsTransparent = ObjC.Sel("setTitlebarAppearsTransparent:");
        SelSetMovableByWindowBackground = ObjC.Sel("setMovableByWindowBackground:");
        SelSetFrameOrigin = ObjC.Sel("setFrameOrigin:");
        SelSetFrame = ObjC.Sel("setFrame:");
        SelFrame = ObjC.Sel("frame");
        SelScreen = ObjC.Sel("screen");
        SelStandardWindowButton = ObjC.Sel("standardWindowButton:");
        SelSetHidden = ObjC.Sel("setHidden:");
        SelConvertPointToScreen = ObjC.Sel("convertPointToScreen:");
        SelConvertPointFromScreen = ObjC.Sel("convertPointFromScreen:");
        SelWindow = ObjC.Sel("window");
        SelWindowShouldClose = ObjC.Sel("windowShouldClose:");
        SelWindowWillClose = ObjC.Sel("windowWillClose:");
        SelObject = ObjC.Sel("object");
        SelInterpretKeyEvents = ObjC.Sel("interpretKeyEvents:");
        SelArrayWithObject = ObjC.Sel("arrayWithObject:");

        SelSetAutoresizingMask = ObjC.Sel("setAutoresizingMask:");
        SelSetOpaque = ObjC.Sel("setOpaque:");
        SelInLiveResize = ObjC.Sel("inLiveResize");
        SelSetNeedsDisplay = ObjC.Sel("setNeedsDisplay:");
        SelSetNeedsDisplayNoArgs = ObjC.Sel("setNeedsDisplay");
        SelDisplayIfNeeded = ObjC.Sel("displayIfNeeded");
        SelDisplay = ObjC.Sel("display");
        SelSetWantsLayer = ObjC.Sel("setWantsLayer:");
        SelSetLayerContentsRedrawPolicy = ObjC.Sel("setLayerContentsRedrawPolicy:");
        SelLayer = ObjC.Sel("layer");
        SelSetNeedsDisplayOnBoundsChange = ObjC.Sel("setNeedsDisplayOnBoundsChange:");
        SelClearColor = ObjC.Sel("clearColor");
        SelRegisterForDraggedTypes = ObjC.Sel("registerForDraggedTypes:");
        SelDraggingPasteboard = ObjC.Sel("draggingPasteboard");
        SelDraggingLocation = ObjC.Sel("draggingLocation");
        SelPropertyListForType = ObjC.Sel("propertyListForType:");
        SelCount = ObjC.Sel("count");
        SelObjectAtIndex = ObjC.Sel("objectAtIndex:");
        SelUTF8String = ObjC.Sel("UTF8String");

        SelAppearanceNamed = ObjC.Sel("appearanceNamed:");
        _appearanceNameAqua = ObjC.CreateNSString("NSAppearanceNameAqua");
        _appearanceNameDarkAqua = ObjC.CreateNSString("NSAppearanceNameDarkAqua");

        ClsNSView = ObjC.GetClass("NSView");
        ClsCAMetalLayer = ObjC.GetClass("CAMetalLayer");
        ClsNSObject = ObjC.GetClass("NSObject");

        // NSCursor
        ClsNSCursor = ObjC.GetClass("NSCursor");
        SelArrowCursor = ObjC.Sel("arrowCursor");
        SelIBeamCursor = ObjC.Sel("IBeamCursor");
        SelCrosshairCursor = ObjC.Sel("crosshairCursor");
        SelPointingHandCursor = ObjC.Sel("pointingHandCursor");
        SelResizeLeftRightCursor = ObjC.Sel("resizeLeftRightCursor");
        SelResizeUpDownCursor = ObjC.Sel("resizeUpDownCursor");
        SelOperationNotAllowedCursor = ObjC.Sel("operationNotAllowedCursor");
        SelOpenHandCursor = ObjC.Sel("openHandCursor");
        SelCursorSet = ObjC.Sel("set");

        EnsureMetalLayerDelegate();

        _initialized = true;
    }

    private static bool TryGetTextInputTarget(nint view, out MacOSWindowBackend backend)
    {
        lock (_textInputTargets)
        {
            if (_textInputTargets.TryGetValue(view, out var wr))
            {
                if (wr.TryGetTarget(out var target) && target != null)
                {
                    backend = target;
                    return true;
                }

                _textInputTargets.Remove(view);
            }
        }

        backend = null!;
        return false;
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_insertText(nint self, nint _cmd, nint text, NSRange replacementRange)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc insertText:replacementRange: view=0x{self:x} textObj=0x{text:x} repl=({replacementRange.location},{replacementRange.length}) imeHasMarked={backend.ImeHasMarkedText}");
            var s = MacOSInterop.GetUtf8TextFromNSStringOrAttributedString(text);
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> text len={(s?.Length ?? -1)} '{MacOSWindowBackend.TruncateForImeLog(s)}'");
            backend.ImeInsertText(s, replacementRange);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_insertTextLegacy(nint self, nint _cmd, nint text)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc insertText: (legacy) view=0x{self:x} textObj=0x{text:x} imeHasMarked={backend.ImeHasMarkedText}");
            var s = MacOSInterop.GetUtf8TextFromNSStringOrAttributedString(text);
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> text len={(s?.Length ?? -1)} '{MacOSWindowBackend.TruncateForImeLog(s)}'");
            backend.ImeInsertText(s);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_setMarkedText(nint self, nint _cmd, nint text, NSRange selectedRange, NSRange replacementRange)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc setMarkedText:selectedRange:replacementRange: view=0x{self:x} textObj=0x{text:x} sel=({selectedRange.location},{selectedRange.length}) repl=({replacementRange.location},{replacementRange.length}) imeHasMarked={backend.ImeHasMarkedText}");
            var s = MacOSInterop.GetUtf8TextFromNSStringOrAttributedString(text) ?? string.Empty;
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> marked len={s.Length} '{MacOSWindowBackend.TruncateForImeLog(s)}'");
            backend.ImeSetMarkedText(s, replacementRange);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_setMarkedTextLegacy(nint self, nint _cmd, nint text, NSRange selectedRange)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc setMarkedText:selectedRange: (legacy) view=0x{self:x} textObj=0x{text:x} sel=({selectedRange.location},{selectedRange.length}) imeHasMarked={backend.ImeHasMarkedText}");
            var s = MacOSInterop.GetUtf8TextFromNSStringOrAttributedString(text) ?? string.Empty;
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> marked len={s.Length} '{MacOSWindowBackend.TruncateForImeLog(s)}'");
            backend.ImeSetMarkedText(s);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_unmarkText(nint self, nint _cmd)
    {
        try
        {
            if (TryGetTextInputTarget(self, out var backend))
            {
                MacOSWindowBackend.ImeNativeLogger.Write($"objc unmarkText view=0x{self:x} imeHasMarked={backend.ImeHasMarkedText}");
                backend.ImeUnmarkText();
            }
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static byte MewUITextInputView_hasMarkedText(nint self, nint _cmd)
    {
        try
        {
            var result = TryGetTextInputTarget(self, out var backend) && backend.ImeHasMarkedText ? (byte)1 : (byte)0;
            MacOSWindowBackend.ImeNativeLogger.Write($"objc hasMarkedText view=0x{self:x} -> {result}");
            return result;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static NSRange MewUITextInputView_markedRange(nint self, nint _cmd)
    {
        try
        {
            const ulong NSNotFound = (ulong)long.MaxValue;
            if (!TryGetTextInputTarget(self, out var backend) || !backend.ImeHasMarkedText)
            {
                MacOSWindowBackend.ImeNativeLogger.Write($"objc markedRange view=0x{self:x} -> (NSNotFound,0)");
                return new NSRange(NSNotFound, 0);
            }

            if (backend.Window.FocusManager.FocusedElement is Controls.TextBase tb)
            {
                // NSTextInputClient expects ranges in the document's coordinates.
                // TextBase maintains the composition range inside the document.
                int start = Math.Max(0, tb.CompositionStartIndex);
                int len = Math.Max(0, tb.CompositionLength);
                if (len == 0)
                {
                    // Composition started but TextBase may not have received the first update yet.
                    // Report the current marked string length so IME treats the range as active.
                    len = backend.ImeMarkedText?.Length ?? 0;
                }
                var r = new NSRange((ulong)start, (ulong)len);
                MacOSWindowBackend.ImeNativeLogger.Write($"objc markedRange view=0x{self:x} -> ({r.location},{r.length}) [TextBase]");
                return r;
            }

            // Fallback to a minimal "active marked range" when there is no focused TextBase.
            var rr = new NSRange(0, (ulong)(backend.ImeMarkedText?.Length ?? 0));
            MacOSWindowBackend.ImeNativeLogger.Write($"objc markedRange view=0x{self:x} -> ({rr.location},{rr.length}) [fallback]");
            return rr;
        }
        catch
        {
            MacOSWindowBackend.ImeNativeLogger.Write($"objc markedRange view=0x{self:x} -> (NSNotFound,0) [exception]");
            return new NSRange(ulong.MaxValue, 0);
        }
    }

    [UnmanagedCallersOnly]
    private static NSRange MewUITextInputView_selectedRange(nint self, nint _cmd)
    {
        // Without a full text-store bridge, keep this minimal but coherent for IME:
        // - When composing, report the caret at the end of the marked text.
        // - Otherwise, report an empty selection at 0.
        try
        {
            if (TryGetTextInputTarget(self, out var backend))
            {
                if (backend.Window.FocusManager.FocusedElement is Controls.TextBase tb)
                {
                    var (s, e) = tb.SelectionRange;
                    int start = Math.Min(s, e);
                    int end = Math.Max(s, e);
                    var r = new NSRange((ulong)Math.Max(0, start), (ulong)Math.Max(0, end - start));
                    MacOSWindowBackend.ImeNativeLogger.Write($"objc selectedRange view=0x{self:x} -> ({r.location},{r.length}) [TextBase]");
                    return r;
                }

                if (backend.ImeHasMarkedText)
                {
                    ulong len = (ulong)(backend.ImeMarkedText?.Length ?? 0);
                    var rr = new NSRange(len, 0);
                    MacOSWindowBackend.ImeNativeLogger.Write($"objc selectedRange view=0x{self:x} -> ({rr.location},{rr.length}) [marked]");
                    return rr;
                }
            }
        }
        catch
        {
        }

        MacOSWindowBackend.ImeNativeLogger.Write($"objc selectedRange view=0x{self:x} -> (0,0) [fallback]");
        return new NSRange(0, 0);
    }

    [UnmanagedCallersOnly]
    private static nint MewUITextInputView_validAttributesForMarkedText(nint self, nint _cmd)
    {
        try
        {
            // Return an empty NSArray rather than nil; some IMEs are stricter about this.
            EnsureInitialized();
            if (ObjC.GetClass("NSArray") is var cls && cls != 0)
            {
                var selArray = ObjC.Sel("array");
                if (selArray != 0)
                {
                    var arr = ObjC.MsgSend_nint(cls, selArray);
                    MacOSWindowBackend.ImeNativeLogger.Write($"objc validAttributesForMarkedText view=0x{self:x} -> 0x{arr:x}");
                    return arr;
                }
            }
        }
        catch
        {
        }

        MacOSWindowBackend.ImeNativeLogger.Write($"objc validAttributesForMarkedText view=0x{self:x} -> 0");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static long MewUITextInputView_conversationIdentifier(nint self, nint _cmd)
    {
        // Required by NSTextInputClient. Use the view pointer as a stable per-instance identifier.
        MacOSWindowBackend.ImeNativeLogger.Write($"objc conversationIdentifier view=0x{self:x} -> 0x{self:x}");
        return self;
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_updateTextAttributes(nint self, nint _cmd, nint attributes)
    {
        // Optional NSTextInputClient method; ignored for now (we don't expose attribute runs).
        MacOSWindowBackend.ImeNativeLogger.Write($"objc updateTextAttributes: view=0x{self:x} attrs=0x{attributes:x}");
    }

    [UnmanagedCallersOnly]
    private static nint MewUITextInputView_attributedSubstringForProposedRange(nint self, nint _cmd, NSRange proposedRange, nint actualRange)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return 0;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc attributedSubstringForProposedRange view=0x{self:x} proposed=({proposedRange.location},{proposedRange.length}) actualRangePtr=0x{actualRange:x}");
            string text;
            int textLen;
            if (backend.Window.FocusManager.FocusedElement is Controls.TextBase tb)
            {
                textLen = tb.TextLengthInternal;
                text = textLen > 0 ? tb.GetTextSubstringInternal(0, textLen) : string.Empty;
            }
            else
            {
                text = backend.ImeMarkedText ?? string.Empty;
                textLen = text.Length;
            }

            ulong totalLen = (ulong)Math.Max(0, textLen);
            ulong start = proposedRange.location > totalLen ? totalLen : proposedRange.location;
            ulong len = proposedRange.length;
            if (start + len > totalLen)
            {
                len = totalLen - start;
            }

            unsafe
            {
                if (actualRange != 0)
                {
                    *(NSRange*)actualRange = new NSRange(start, len);
                }
            }

            string slice = len == 0 ? string.Empty : text.Substring((int)start, (int)len);
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> sliceStart={start} sliceLen={len} '{MacOSWindowBackend.TruncateForImeLog(slice)}'");
            nint nsString = ObjC.CreateNSString(slice);
            if (nsString == 0)
            {
                return 0;
            }

            // Return an NSAttributedString so IME can query attributes if it wants.
            nint clsAttr = ObjC.GetClass("NSAttributedString");
            nint selAlloc = ObjC.Sel("alloc");
            nint selInitWithString = ObjC.Sel("initWithString:");
            if (clsAttr == 0 || selAlloc == 0 || selInitWithString == 0)
            {
                return 0;
            }

            nint attr = ObjC.MsgSend_nint(clsAttr, selAlloc);
            attr = attr != 0 ? ObjC.MsgSend_nint_nint(attr, selInitWithString, nsString) : 0;
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> returning NSAttributedString 0x{attr:x}");
            return attr;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static ulong MewUITextInputView_characterIndexForPoint(nint self, nint _cmd, NSPoint point)
    {
        MacOSWindowBackend.ImeNativeLogger.Write($"objc characterIndexForPoint view=0x{self:x} pt=({point.x},{point.y}) -> 0");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static NSRect MewUITextInputView_firstRectForCharacterRange(nint self, nint _cmd, NSRange range, nint actualRange)
    {
        try
        {
            EnsureInitialized();
            if (SelWindow != 0 && SelFrame != 0)
            {
                var window = ObjC.MsgSend_nint(self, SelWindow);
                if (window != 0)
                {
                    var frame = ObjC.MsgSend_rect(window, SelFrame);

                    // Try to get the caret position from the focused text element.
                    if (TryGetTextInputTarget(self, out var backend) &&
                        backend.Window.FocusManager.FocusedElement is ITextCompositionClient client)
                    {
                        var caretRect = client.GetCharRectInWindow(client.CompositionStartIndex);

                        // frame = window outer frame (includes title bar), screen coords (y-up).
                        // caretRect = content area coords (y-down from top of content).
                        // Need: title bar height = frame.height - contentView.frame.height.
                        var contentView = ObjC.MsgSend_nint(window, ObjC.Sel("contentView"));
                        double contentHeight = frame.size.height;
                        if (contentView != 0)
                        {
                            var cvFrame = ObjC.MsgSend_rect(contentView, SelFrame);
                            contentHeight = cvFrame.size.height;
                        }
                        double titleBarHeight = frame.size.height - contentHeight;

                        // Convert y-down content coords to y-up screen coords.
                        double screenX = frame.origin.x + caretRect.X;
                        double screenY = frame.origin.y + (contentHeight - caretRect.Y - caretRect.Height);
                        var r = new NSRect(screenX, screenY, caretRect.Width, caretRect.Height);
                        MacOSWindowBackend.ImeNativeLogger.Write($"objc firstRectForCharacterRange view=0x{self:x} range=({range.location},{range.length}) actualRangePtr=0x{actualRange:x} -> ({r.origin.x},{r.origin.y},{r.size.width},{r.size.height}) titleBar={titleBarHeight}");
                        return r;
                    }

                    // Fallback: top-left of window.
                    var fallback = new NSRect(frame.origin.x + 10, frame.origin.y + frame.size.height - 30, 0, 0);
                    MacOSWindowBackend.ImeNativeLogger.Write($"objc firstRectForCharacterRange view=0x{self:x} range=({range.location},{range.length}) -> fallback");
                    return fallback;
                }
            }
        }
        catch
        {
        }

        MacOSWindowBackend.ImeNativeLogger.Write($"objc firstRectForCharacterRange view=0x{self:x} range=({range.location},{range.length}) -> default");
        return default;
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_doCommandBySelector(nint self, nint _cmd, nint selector)
    {
        MacOSWindowBackend.ImeNativeLogger.Write($"objc doCommandBySelector view=0x{self:x} selector=0x{selector:x}");
        try
        {
            if (TryGetTextInputTarget(self, out var backend))
            {
                backend.ImeDoCommandBySelector(selector);
            }
        }
        catch
        {
        }
    }

    private static string[] ExtractPathsFromDraggingInfo(nint draggingInfo)
    {
        EnsureInitialized();
        if (draggingInfo == 0 || SelDraggingPasteboard == 0 || SelPropertyListForType == 0 || SelCount == 0 || SelObjectAtIndex == 0 || SelUTF8String == 0)
        {
            return [];
        }

        var pasteboard = ObjC.MsgSend_nint(draggingInfo, SelDraggingPasteboard);
        if (pasteboard == 0)
        {
            return [];
        }

        var fileNamesType = ObjC.CreateNSString("NSFilenamesPboardType");
        if (fileNamesType == 0)
        {
            return [];
        }

        var array = ObjC.MsgSend_nint_nint(pasteboard, SelPropertyListForType, fileNamesType);
        if (array == 0)
        {
            return [];
        }

        ulong count = ObjC.MsgSend_ulong(array, SelCount);
        if (count == 0)
        {
            return [];
        }

        var paths = new List<string>((int)count);
        for (ulong i = 0; i < count; i++)
        {
            var nsString = ObjC.MsgSend_nint_ulong(array, SelObjectAtIndex, i);
            if (nsString == 0)
            {
                continue;
            }

            var utf8 = ObjC.MsgSend_nint(nsString, SelUTF8String);
            var path = utf8 != 0 ? Marshal.PtrToStringUTF8(utf8) : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths.ToArray();
    }

    [UnmanagedCallersOnly]
    private static ulong MewUIDragDestination_draggingEntered(nint self, nint _cmd, nint sender)
    {
        try
        {
            if (TryGetTextInputTarget(self, out var backend))
            {
                return backend.HandleNativeDragEnter(
                    ExtractPathsFromDraggingInfo(sender),
                    ObjC.MsgSend_point(sender, SelDraggingLocation));
            }
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    private static ulong MewUIDragDestination_draggingUpdated(nint self, nint _cmd, nint sender)
    {
        try
        {
            if (TryGetTextInputTarget(self, out var backend))
            {
                return backend.HandleNativeDragOver(
                    ExtractPathsFromDraggingInfo(sender),
                    ObjC.MsgSend_point(sender, SelDraggingLocation));
            }
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    private static void MewUIDragDestination_draggingExited(nint self, nint _cmd, nint sender)
    {
        try
        {
            if (TryGetTextInputTarget(self, out var backend))
            {
                backend.HandleNativeDragLeave();
            }
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static byte MewUIDragDestination_performDragOperation(nint self, nint _cmd, nint sender)
    {
        try
        {
            if (TryGetTextInputTarget(self, out var backend) &&
                backend.HandleNativeDrop(
                    ExtractPathsFromDraggingInfo(sender),
                    ObjC.MsgSend_point(sender, SelDraggingLocation)))
            {
                return 1;
            }
        }
        catch
        {
        }

        return 0;
    }

    private static void AddDragDestinationMethods(nint cls)
    {
        var selDraggingEntered = ObjC.Sel("draggingEntered:");
        var selDraggingUpdated = ObjC.Sel("draggingUpdated:");
        var selDraggingExited = ObjC.Sel("draggingExited:");
        var selPerformDragOperation = ObjC.Sel("performDragOperation:");

        if (selDraggingEntered != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, ulong>)&MewUIDragDestination_draggingEntered;
            _ = ObjC.AddMethod(cls, selDraggingEntered, imp, "Q@:@");
        }

        if (selDraggingUpdated != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, ulong>)&MewUIDragDestination_draggingUpdated;
            _ = ObjC.AddMethod(cls, selDraggingUpdated, imp, "Q@:@");
        }

        if (selDraggingExited != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIDragDestination_draggingExited;
            _ = ObjC.AddMethod(cls, selDraggingExited, imp, "v@:@");
        }

        if (selPerformDragOperation != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, byte>)&MewUIDragDestination_performDragOperation;
            _ = ObjC.AddMethod(cls, selPerformDragOperation, imp, "B@:@");
        }

        _ = ObjC.AddProtocol(cls, "NSDraggingDestination");
    }

    [UnmanagedCallersOnly]
    private static byte MewUITextInputView_acceptsFirstResponder(nint self, nint _cmd)
    {
        MacOSWindowBackend.ImeNativeLogger.Write($"objc acceptsFirstResponder view=0x{self:x} -> 1");
        return 1;
    }

    private static void AddTextInputClientMethods(nint cls)
    {
        var selInsertLegacy = ObjC.Sel("insertText:");
        var selInsert = ObjC.Sel("insertText:replacementRange:");
        var selSetMarkedLegacy = ObjC.Sel("setMarkedText:selectedRange:");
        var selSetMarked = ObjC.Sel("setMarkedText:selectedRange:replacementRange:");
        var selUnmark = ObjC.Sel("unmarkText");
        var selHasMarked = ObjC.Sel("hasMarkedText");
        var selMarkedRange = ObjC.Sel("markedRange");
        var selSelectedRange = ObjC.Sel("selectedRange");
        var selValidAttrs = ObjC.Sel("validAttributesForMarkedText");
        var selConversationId = ObjC.Sel("conversationIdentifier");
        var selUpdateTextAttrs = ObjC.Sel("updateTextAttributes:");
        var selAttrSub = ObjC.Sel("attributedSubstringForProposedRange:actualRange:");
        var selCharIndex = ObjC.Sel("characterIndexForPoint:");
        var selFirstRect = ObjC.Sel("firstRectForCharacterRange:actualRange:");
        var selDoCommand = ObjC.Sel("doCommandBySelector:");
        var selAcceptsFirstResponder = ObjC.Sel("acceptsFirstResponder");

        if (selInsertLegacy != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUITextInputView_insertTextLegacy;
            _ = ObjC.AddMethod(cls, selInsertLegacy, imp, "v@:@");
        }

        if (selInsert != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, NSRange, void>)&MewUITextInputView_insertText;
            _ = ObjC.AddMethod(cls, selInsert, imp, "v@:@{_NSRange=QQ}");
        }

        if (selSetMarkedLegacy != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, NSRange, void>)&MewUITextInputView_setMarkedTextLegacy;
            _ = ObjC.AddMethod(cls, selSetMarkedLegacy, imp, "v@:@{_NSRange=QQ}");
        }

        if (selSetMarked != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, NSRange, NSRange, void>)&MewUITextInputView_setMarkedText;
            _ = ObjC.AddMethod(cls, selSetMarked, imp, "v@:@{_NSRange=QQ}{_NSRange=QQ}");
        }

        if (selUnmark != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, void>)&MewUITextInputView_unmarkText;
            _ = ObjC.AddMethod(cls, selUnmark, imp, "v@:");
        }

        if (selHasMarked != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, byte>)&MewUITextInputView_hasMarkedText;
            _ = ObjC.AddMethod(cls, selHasMarked, imp, "B@:");
        }

        if (selMarkedRange != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSRange>)&MewUITextInputView_markedRange;
            _ = ObjC.AddMethod(cls, selMarkedRange, imp, "{_NSRange=QQ}@:");
        }

        if (selSelectedRange != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSRange>)&MewUITextInputView_selectedRange;
            _ = ObjC.AddMethod(cls, selSelectedRange, imp, "{_NSRange=QQ}@:");
        }

        if (selValidAttrs != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint>)&MewUITextInputView_validAttributesForMarkedText;
            _ = ObjC.AddMethod(cls, selValidAttrs, imp, "@@:");
        }

        if (selConversationId != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, long>)&MewUITextInputView_conversationIdentifier;
            _ = ObjC.AddMethod(cls, selConversationId, imp, "q@:");
        }

        if (selUpdateTextAttrs != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUITextInputView_updateTextAttributes;
            _ = ObjC.AddMethod(cls, selUpdateTextAttrs, imp, "v@:@");
        }

        if (selAttrSub != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSRange, nint, nint>)&MewUITextInputView_attributedSubstringForProposedRange;
            _ = ObjC.AddMethod(cls, selAttrSub, imp, "@@:{_NSRange=QQ}^{_NSRange=QQ}");
        }

        if (selCharIndex != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSPoint, ulong>)&MewUITextInputView_characterIndexForPoint;
            _ = ObjC.AddMethod(cls, selCharIndex, imp, "Q@:{CGPoint=dd}");
        }

        if (selFirstRect != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSRange, nint, NSRect>)&MewUITextInputView_firstRectForCharacterRange;
            _ = ObjC.AddMethod(cls, selFirstRect, imp, "{CGRect={CGPoint=dd}{CGSize=dd}}@:{_NSRange=QQ}^{_NSRange=QQ}");
        }

        if (selDoCommand != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUITextInputView_doCommandBySelector;
            _ = ObjC.AddMethod(cls, selDoCommand, imp, "v@::");
        }

        if (selAcceptsFirstResponder != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, byte>)&MewUITextInputView_acceptsFirstResponder;
            _ = ObjC.AddMethod(cls, selAcceptsFirstResponder, imp, "B@:");
        }
    }

    private static void EnsureTextInputViewSubclass()
    {
        if (ClsMewUITextInputView != 0 || ClsNSView == 0)
        {
            return;
        }

        const string className = "MewUITextInputView";
        var cls = ObjC.GetClass(className);
        bool needsRegister = false;
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ClsNSView, className);
            needsRegister = cls != 0;
        }

        if (cls != 0)
        {
            AddTextInputClientMethods(cls);
            _ = ObjC.AddProtocol(cls, "NSTextInputClient");
            AddDragDestinationMethods(cls);
            if (needsRegister)
            {
                ObjC.RegisterClassPair(cls);
            }
        }

        ClsMewUITextInputView = cls;
    }

    private static bool TryGetMetalLayerTarget(nint metalLayer, out MacOSWindowBackend backend)
    {
        lock (_metalLayerTargets)
        {
            if (_metalLayerTargets.TryGetValue(metalLayer, out var wr))
            {
                if (wr.TryGetTarget(out var target) && target != null)
                {
                    backend = target;
                    return true;
                }

                _metalLayerTargets.Remove(metalLayer);
            }
        }

        backend = null!;
        return false;
    }

    [UnmanagedCallersOnly]
    private static void MewUIMetalLayerDelegate_displayLayer(nint self, nint _cmd, nint layer)
    {
        try
        {
            if (TryGetMetalLayerTarget(layer, out var backend))
            {
                backend.RenderFromMetalLayerDisplay(layer);
            }
        }
        catch
        {
            // Never let an exception cross the unmanaged boundary.
        }
    }

    private static void EnsureMetalLayerDelegate()
    {
        if (_sharedMetalLayerDelegate != 0)
        {
            return;
        }

        if (ClsNSObject == 0)
        {
            ClsNSObject = ObjC.GetClass("NSObject");
        }

        if (ClsNSObject == 0)
        {
            return;
        }

        const string className = "MewUIMetalLayerDelegate";
        var cls = ObjC.GetClass(className);
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ClsNSObject, className);
            if (cls != 0)
            {
                var sel = ObjC.Sel("displayLayer:");
                var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIMetalLayerDelegate_displayLayer;
                _ = ObjC.AddMethod(cls, sel, imp, "v@:@");
                ObjC.RegisterClassPair(cls);
            }
        }

        ClsMewUIMetalLayerDelegate = cls;
        if (cls == 0 || SelAlloc == 0 || SelInit == 0)
        {
            return;
        }

        // Keep a single shared delegate instance alive for the lifetime of the process.
        var obj = ObjC.MsgSend_nint(cls, SelAlloc);
        obj = obj != 0 ? ObjC.MsgSend_nint(obj, SelInit) : 0;
        _sharedMetalLayerDelegate = obj;
    }

    [UnmanagedCallersOnly]
    private static byte MewUIWindow_canBecomeKeyWindow(nint self, nint sel) => 1;

    [UnmanagedCallersOnly]
    private static byte MewUIWindow_canBecomeMainWindow(nint self, nint sel) => 1;

    [UnmanagedCallersOnly]
    private static byte MewUIWindowDelegate_windowShouldClose(nint self, nint sel, nint sender)
    {
        try
        {
            if (sender == 0)
            {
                return 1;
            }

            if (TryGetWindowCloseTarget(sender, out var backend))
            {
                if (!backend.Window.RequestClose())
                {
                    return 0; // cancelled
                }
            }

            // RaiseClosed is handled by windowWillClose, not here.
            return 1;
        }
        catch
        {
            // Never let an exception cross the unmanaged boundary.
            return 1;
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUIWindowDelegate_windowDidBecomeKey(nint self, nint sel, nint notification)
    {
        try
        {
            if (notification == 0)
            {
                return;
            }

            nint window = GetWindowFromNotification(notification);
            if (window != 0 && TryGetWindowCloseTarget(window, out var backend))
            {
                backend.Window.SetIsActive(true);
                backend.Window.RaiseActivated();
            }
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    private static void MewUIWindowDelegate_windowDidMiniaturize(nint self, nint sel, nint notification)
    {
        try
        {
            if (notification == 0)
            {
                return;
            }

            nint window = GetWindowFromNotification(notification);
            if (window != 0 && TryGetWindowCloseTarget(window, out var backend))
            {
                backend.Window.SetWindowStateFromBackend(Controls.WindowState.Minimized);
            }
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    private static void MewUIWindowDelegate_windowDidDeminiaturize(nint self, nint sel, nint notification)
    {
        try
        {
            if (notification == 0)
            {
                return;
            }

            nint window = GetWindowFromNotification(notification);
            if (window != 0 && TryGetWindowCloseTarget(window, out var backend))
            {
                ObjC.MsgSend_void_nint_nint(window, ObjC.Sel("makeKeyAndOrderFront:"), 0);
                backend.Window.SetWindowStateFromBackend(Controls.WindowState.Normal);
                backend.Window.PerformLayout();
                backend.Invalidate(erase: true);
            }
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    private static void MewUIWindowDelegate_windowDidEnterFullScreen(nint self, nint sel, nint notification)
    {
        try
        {
            if (notification == 0)
            {
                return;
            }

            nint window = GetWindowFromNotification(notification);
            if (window != 0 && TryGetWindowCloseTarget(window, out var backend))
            {
                backend.Window.SetWindowStateFromBackend(Controls.WindowState.Maximized);
                // Re-apply extended client area after fullscreen transition completes.
                if (backend._extendTitleBarHeight > 0)
                {
                    const ulong ExtendedStyleMask = 1ul | 2ul | 4ul | 8ul | (1ul << 14) | (1ul << 15);
                    MacOSWindowInterop.SetWindowStyleMask(window, ExtendedStyleMask);
                    MacOSWindowInterop.SetTitlebarForTransparency(window, true);
                }
            }
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    private static void MewUIWindowDelegate_windowDidExitFullScreen(nint self, nint sel, nint notification)
    {
        try
        {
            if (notification == 0)
            {
                return;
            }

            nint window = GetWindowFromNotification(notification);
            if (window != 0 && TryGetWindowCloseTarget(window, out var backend))
            {
                backend.Window.SetWindowStateFromBackend(Controls.WindowState.Normal);
                // Re-apply extended client area after fullscreen transition completes.
                if (backend._extendTitleBarHeight > 0)
                {
                    const ulong ExtendedStyleMask = 1ul | 2ul | 4ul | 8ul | (1ul << 15);
                    MacOSWindowInterop.SetWindowStyleMask(window, ExtendedStyleMask);
                    MacOSWindowInterop.SetTitlebarForTransparency(window, true);
                }
            }
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    private static void MewUIWindowDelegate_windowDidResignKey(nint self, nint sel, nint notification)
    {
        try
        {
            if (notification == 0)
            {
                return;
            }

            nint window = GetWindowFromNotification(notification);
            if (window != 0 && TryGetWindowCloseTarget(window, out var backend))
            {
                backend.Window.SetIsActive(false);
                backend.Window.RaiseDeactivated();
            }
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    private static void MewUIWindowDelegate_windowWillClose(nint self, nint sel, nint notification)
    {
        try
        {
            if (notification == 0)
            {
                return;
            }

            nint window = GetWindowFromNotification(notification);
            if (window == 0)
            {
                return;
            }

            if (TryGetWindowCloseTarget(window, out var backend))
            {
                backend.RaiseClosedOnce();
            }
        }
        catch
        {
            // Never let an exception cross the unmanaged boundary.
        }
    }

    private static void EnsureMewUIWindowClass()
    {
        if (ClsMewUIWindow != 0)
        {
            return;
        }

        if (ClsNSWindow == 0)
        {
            return;
        }

        const string className = "MewUIWindow";
        var cls = ObjC.GetClass(className);
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ClsNSWindow, className);
            if (cls != 0)
            {
                var imp = (nint)(delegate* unmanaged<nint, nint, byte>)&MewUIWindow_canBecomeKeyWindow;
                _ = ObjC.AddMethod(cls, ObjC.Sel("canBecomeKeyWindow"), imp, "c@:");

                var mainImp = (nint)(delegate* unmanaged<nint, nint, byte>)&MewUIWindow_canBecomeMainWindow;
                _ = ObjC.AddMethod(cls, ObjC.Sel("canBecomeMainWindow"), mainImp, "c@:");

                ObjC.RegisterClassPair(cls);
            }
        }

        ClsMewUIWindow = cls;
    }

    // NSPanel subclass for tool/utility windows, mirroring MewUIWindow's key/main overrides so a utility
    // panel can become the key window and receive text input.
    private static unsafe void EnsureMewUIPanelClass()
    {
        if (ClsMewUIPanel != 0)
        {
            return;
        }

        if (ClsNSPanel == 0)
        {
            ClsNSPanel = ObjC.GetClass("NSPanel");
        }

        if (ClsNSPanel == 0)
        {
            return;
        }

        const string className = "MewUIPanel";
        var cls = ObjC.GetClass(className);
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ClsNSPanel, className);
            if (cls != 0)
            {
                var imp = (nint)(delegate* unmanaged<nint, nint, byte>)&MewUIWindow_canBecomeKeyWindow;
                _ = ObjC.AddMethod(cls, ObjC.Sel("canBecomeKeyWindow"), imp, "c@:");

                var mainImp = (nint)(delegate* unmanaged<nint, nint, byte>)&MewUIWindow_canBecomeMainWindow;
                _ = ObjC.AddMethod(cls, ObjC.Sel("canBecomeMainWindow"), mainImp, "c@:");

                ObjC.RegisterClassPair(cls);
            }
        }

        ClsMewUIPanel = cls;
    }

    private static void EnsureWindowDelegate()
    {
        if (_sharedWindowDelegate != 0)
        {
            return;
        }

        if (ClsNSObject == 0)
        {
            ClsNSObject = ObjC.GetClass("NSObject");
        }

        if (ClsNSObject == 0)
        {
            return;
        }

        const string className = "MewUIWindowDelegate";
        var cls = ObjC.GetClass(className);
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ClsNSObject, className);
            if (cls != 0)
            {
                var shouldCloseImp = (nint)(delegate* unmanaged<nint, nint, nint, byte>)&MewUIWindowDelegate_windowShouldClose;
                _ = ObjC.AddMethod(cls, SelWindowShouldClose, shouldCloseImp, "c@:@");

                var willCloseImp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIWindowDelegate_windowWillClose;
                _ = ObjC.AddMethod(cls, SelWindowWillClose, willCloseImp, "v@:@");

                var becomeKeyImp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIWindowDelegate_windowDidBecomeKey;
                _ = ObjC.AddMethod(cls, ObjC.Sel("windowDidBecomeKey:"), becomeKeyImp, "v@:@");

                var resignKeyImp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIWindowDelegate_windowDidResignKey;
                _ = ObjC.AddMethod(cls, ObjC.Sel("windowDidResignKey:"), resignKeyImp, "v@:@");

                var miniaturizeImp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIWindowDelegate_windowDidMiniaturize;
                _ = ObjC.AddMethod(cls, ObjC.Sel("windowDidMiniaturize:"), miniaturizeImp, "v@:@");

                var deminiaturizeImp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIWindowDelegate_windowDidDeminiaturize;
                _ = ObjC.AddMethod(cls, ObjC.Sel("windowDidDeminiaturize:"), deminiaturizeImp, "v@:@");

                var enterFullScreenImp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIWindowDelegate_windowDidEnterFullScreen;
                _ = ObjC.AddMethod(cls, ObjC.Sel("windowDidEnterFullScreen:"), enterFullScreenImp, "v@:@");

                var exitFullScreenImp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIWindowDelegate_windowDidExitFullScreen;
                _ = ObjC.AddMethod(cls, ObjC.Sel("windowDidExitFullScreen:"), exitFullScreenImp, "v@:@");

                ObjC.RegisterClassPair(cls);
            }
        }

        ClsMewUIWindowDelegate = cls;
        if (cls == 0 || SelAlloc == 0 || SelInit == 0)
        {
            return;
        }

        var obj = ObjC.MsgSend_nint(cls, SelAlloc);
        obj = obj != 0 ? ObjC.MsgSend_nint(obj, SelInit) : 0;
        _sharedWindowDelegate = obj;
    }

    private static bool TryGetWindowCloseTarget(nint window, out MacOSWindowBackend backend)
    {
        lock (_windowCloseTargets)
        {
            if (_windowCloseTargets.TryGetValue(window, out var weak))
            {
                if (weak.TryGetTarget(out var target))
                {
                    backend = target;
                    return true;
                }
            }
        }

        backend = null!;
        return false;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NSRange
{
    public readonly ulong location;
    public readonly ulong length;

    public NSRange(ulong location, ulong length)
    {
        this.location = location;
        this.length = length;
    }
}
