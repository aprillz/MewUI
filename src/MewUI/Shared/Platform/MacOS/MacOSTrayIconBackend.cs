using System.Runtime.InteropServices;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// A menu bar status item. AppKit runs on the main thread, which is the UI thread on macOS, so the item is created
/// there and its callbacks arrive there. The item's menu is rebuilt from the icon's <see cref="ContextMenu"/> each time
/// it opens; without a menu, a click on the item raises <see cref="TrayIcon.Click"/>.
/// </summary>
internal sealed unsafe partial class MacOSTrayIconBackend : ITrayIconBackend
{
    private const string OBJC = "/usr/lib/libobjc.A.dylib";
    private const string CORE_FOUNDATION = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CORE_GRAPHICS = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string TARGET_CLASS = "MewUITrayIconTarget";
    private const string ITEM_CHOSEN = "trayMenuItemChosen:";
    private const string BUTTON_CLICKED = "trayButtonClicked:";
    private const string MENU_NEEDS_UPDATE = "menuNeedsUpdate:";
    private const double VARIABLE_LENGTH = -1;
    private const double STATUS_ICON_POINTS = 18;
    private const double MENU_ICON_POINTS = 16;
    private const nint STATE_ON = 1;
    private const nint STATE_OFF = 0;
    private const ulong RIGHT_MOUSE_DOWN = 3;
    private const ulong RIGHT_MOUSE_UP = 4;
    private const uint ALPHA_PREMULTIPLIED_FIRST = 2;
    private const uint ALPHA_FIRST = 4;
    private const uint BYTE_ORDER_32_LITTLE = 2 << 12;

    // The selectors are static entry points; each target object finds its backend here.
    private static readonly Dictionary<nint, MacOSTrayIconBackend> _byTarget = new();

    private readonly TrayIcon _trayIcon;
    private readonly List<NativeMenuEntry> _choosable = new();
    private NativeMenuSnapshot? _snapshot;
    private nint _statusItem;
    private nint _menu;
    private nint _target;
    private bool _disposed;

    public MacOSTrayIconBackend(TrayIcon trayIcon)
    {
        _trayIcon = trayIcon;
        try
        {
            nint statusBar = ObjC.MsgSend_nint(ObjC.GetClass("NSStatusBar"), ObjC.Sel("systemStatusBar"));
            // The status bar does not keep the item alive.
            _statusItem = ObjC.Retain(ObjC.MsgSend_nint_double(statusBar, ObjC.Sel("statusItemWithLength:"), VARIABLE_LENGTH));
            if (_statusItem == 0)
            {
                throw new InvalidOperationException("The status bar did not create an item.");
            }

            _target = ObjC.MsgSend_nint(ObjC.MsgSend_nint(TargetClass(), ObjC.Sel("alloc")), ObjC.Sel("init"));
            _byTarget[_target] = this;

            nint button = ObjC.MsgSend_nint(_statusItem, ObjC.Sel("button"));
            ObjC.MsgSend_void_nint_nint(button, ObjC.Sel("setTarget:"), _target);
            ObjC.MsgSend_void_nint_nint(button, ObjC.Sel("setAction:"), ObjC.Sel(BUTTON_CLICKED));

            _menu = CreateMenu();
            // The delegate's menuNeedsUpdate: rebuilds the items just before the menu opens.
            ObjC.MsgSend_void_nint_nint(_menu, ObjC.Sel("setDelegate:"), _target);

            Update();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        nint button = ObjC.MsgSend_nint(_statusItem, ObjC.Sel("button"));
        nint image = CreateStatusImage();
        ObjC.MsgSend_void_nint_nint(button, ObjC.Sel("setImage:"), image);
        if (image != 0)
        {
            ObjC.MsgSend_void(image, ObjC.Sel("release"));
        }

        ObjC.MsgSend_void_nint_nint(button, ObjC.Sel("setToolTip:"), ObjC.CreateNSString(_trayIcon.ToolTipText));

        // A status item with a menu opens it on a click; without one the click reaches the button's action.
        ObjC.MsgSend_void_nint_nint(_statusItem, ObjC.Sel("setMenu:"), _trayIcon.ContextMenu != null ? _menu : 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_statusItem != 0)
        {
            nint statusBar = ObjC.MsgSend_nint(ObjC.GetClass("NSStatusBar"), ObjC.Sel("systemStatusBar"));
            ObjC.MsgSend_void_nint_nint(statusBar, ObjC.Sel("removeStatusItem:"), _statusItem);
            ObjC.MsgSend_void(_statusItem, ObjC.Sel("release"));
            _statusItem = 0;
        }

        if (_menu != 0)
        {
            ObjC.MsgSend_void_nint_nint(_menu, ObjC.Sel("setDelegate:"), 0);
            ObjC.MsgSend_void(_menu, ObjC.Sel("release"));
            _menu = 0;
        }

        if (_target != 0)
        {
            _byTarget.Remove(_target);
            ObjC.MsgSend_void(_target, ObjC.Sel("release"));
            _target = 0;
        }

        _choosable.Clear();
        _snapshot = null;
    }

    private void Rebuild()
    {
        _choosable.Clear();
        ObjC.MsgSend_void(_menu, ObjC.Sel("removeAllItems"));

        var model = _trayIcon.PrepareMenu();
        if (_disposed || model == null)
        {
            return;
        }

        _snapshot = new NativeMenuSnapshot(Application.Current.CommandRouter, _trayIcon.CommandTarget, gestureWindow: null,
            MENU_ICON_POINTS, MacOSInterop.GetScreenCoordinateScale());
        AppendEntries(_menu, _snapshot.Read(model.Menu));
    }

    private void AppendEntries(nint menu, IReadOnlyList<NativeMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Kind == NativeMenuEntryKind.Separator)
            {
                ObjC.MsgSend_void_nint_nint(menu, ObjC.Sel("addItem:"), ObjC.MsgSend_nint(ObjC.GetClass("NSMenuItem"), ObjC.Sel("separatorItem")));
                continue;
            }

            string keyEquivalent = string.Empty;
            ulong modifierMask = 0;
            if (entry.Gesture is KeyGesture gesture && !MacOSKeyEquivalents.TryGet(gesture, out keyEquivalent, out modifierMask))
            {
                keyEquivalent = string.Empty;
                modifierMask = 0;
            }

            bool opensSubMenu = entry.Kind == NativeMenuEntryKind.SubMenu;
            nint item = InitMenuItem(
                ObjC.MsgSend_nint(ObjC.GetClass("NSMenuItem"), ObjC.Sel("alloc")),
                ObjC.Sel("initWithTitle:action:keyEquivalent:"),
                ObjC.CreateNSString(entry.FormatText('\0')),
                opensSubMenu ? 0 : ObjC.Sel(ITEM_CHOSEN),
                ObjC.CreateNSString(keyEquivalent));
            if (keyEquivalent.Length > 0)
            {
                ObjC.MsgSend_void_nint_ulong(item, ObjC.Sel("setKeyEquivalentModifierMask:"), modifierMask);
            }

            ObjC.MsgSend_void_nint_bool(item, ObjC.Sel("setEnabled:"), entry.IsEnabled);
            if (entry.Icon is NativeMenuIcon icon)
            {
                nint image = CreateImage(icon.PremultipliedBgra, icon.PixelSize, premultiplied: true, icon.PixelSize / icon.Scale);
                if (image != 0)
                {
                    ObjC.MsgSend_void_nint_bool(image, ObjC.Sel("setTemplate:"), icon.IsGlyph);
                    ObjC.MsgSend_void_nint_nint(item, ObjC.Sel("setImage:"), image);
                    ObjC.MsgSend_void(image, ObjC.Sel("release"));
                }
            }

            if (opensSubMenu)
            {
                nint subMenu = CreateMenu();
                AppendEntries(subMenu, entry.Children);
                ObjC.MsgSend_void_nint_nint(item, ObjC.Sel("setSubmenu:"), subMenu);
                ObjC.MsgSend_void(subMenu, ObjC.Sel("release"));
            }
            else
            {
                ObjC.MsgSend_void_nint_nint(item, ObjC.Sel("setTarget:"), _target);
                ObjC.MsgSend_void_nint_nint(item, ObjC.Sel("setTag:"), _choosable.Count);
                ObjC.MsgSend_void_nint_nint(item, ObjC.Sel("setState:"), entry.IsChecked ? STATE_ON : STATE_OFF);
                _choosable.Add(entry);
            }

            ObjC.MsgSend_void_nint_nint(menu, ObjC.Sel("addItem:"), item);
            ObjC.MsgSend_void(item, ObjC.Sel("release"));
        }
    }

    private void OnItemChosen(nint sender)
    {
        long tag = ObjC.MsgSend_long(sender, ObjC.Sel("tag"));
        if (_snapshot != null && tag >= 0 && tag < _choosable.Count)
        {
            _snapshot.Invoke(_choosable[(int)tag]);
        }
    }

    private void OnButtonClicked()
    {
        nint application = ObjC.MsgSend_nint(ObjC.GetClass("NSApplication"), ObjC.Sel("sharedApplication"));
        nint currentEvent = ObjC.MsgSend_nint(application, ObjC.Sel("currentEvent"));
        ulong eventType = currentEvent != 0 ? ObjC.MsgSend_ulong(currentEvent, ObjC.Sel("type")) : 0;
        var button = eventType is RIGHT_MOUSE_DOWN or RIGHT_MOUSE_UP ? MouseButton.Right : MouseButton.Left;
        _trayIcon.RaiseClick(Application.Current.PlatformHost.GetCursorScreenPosition(), button);
    }

    /// <summary>The status item image, retained; the application icon when the tray icon has none.</summary>
    private nint CreateStatusImage()
    {
        nint image = 0;
        double scale = MacOSInterop.GetScreenCoordinateScale();
        if (_trayIcon.Icon is IconSource source && source.TryGetPixels((int)Math.Round(STATUS_ICON_POINTS * scale), out var pixels))
        {
            image = CreateImage(pixels.Data, pixels.WidthPx, premultiplied: false, STATUS_ICON_POINTS, pixels.HeightPx);
        }

        if (image == 0)
        {
            nint application = ObjC.MsgSend_nint(ObjC.GetClass("NSApplication"), ObjC.Sel("sharedApplication"));
            nint applicationIcon = ObjC.MsgSend_nint(application, ObjC.Sel("applicationIconImage"));
            image = applicationIcon != 0 ? ObjC.MsgSend_nint(applicationIcon, ObjC.Sel("copy")) : 0;
        }

        if (image != 0)
        {
            ObjC.MsgSend_void_nint_size(image, ObjC.Sel("setSize:"), new NSSize(STATUS_ICON_POINTS, STATUS_ICON_POINTS));
            ObjC.MsgSend_void_nint_bool(image, ObjC.Sel("setTemplate:"), _trayIcon.IsMonochromeIcon);
        }

        return image;
    }

    /// <summary>A retained image of BGRA pixels shown at <paramref name="points"/> points wide.</summary>
    private static nint CreateImage(byte[] bgra, int width, bool premultiplied, double points, int height = 0)
    {
        if (height <= 0)
        {
            height = width;
        }

        nint colorSpace = CGColorSpaceCreateDeviceRGB();
        nint data;
        fixed (byte* bytes = bgra)
        {
            data = CFDataCreate(0, bytes, width * height * 4);
        }

        nint provider = CGDataProviderCreateWithCFData(data);
        uint bitmapInfo = (premultiplied ? ALPHA_PREMULTIPLIED_FIRST : ALPHA_FIRST) | BYTE_ORDER_32_LITTLE;
        nint cgImage = CGImageCreate((nuint)width, (nuint)height, 8, 32, (nuint)(width * 4), colorSpace, bitmapInfo,
            provider, 0, false, 0);
        CGDataProviderRelease(provider);
        CFRelease(data);
        CGColorSpaceRelease(colorSpace);
        if (cgImage == 0)
        {
            return 0;
        }

        nint image = InitWithCGImage(ObjC.MsgSend_nint(ObjC.GetClass("NSImage"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithCGImage:size:"), cgImage, new NSSize(points, points * height / width));
        CGImageRelease(cgImage);
        return image;
    }

    /// <summary>A retained menu whose items take their enabled state from the model, not from AppKit's validation.</summary>
    private static nint CreateMenu()
    {
        nint menu = ObjC.MsgSend_nint(ObjC.MsgSend_nint(ObjC.GetClass("NSMenu"), ObjC.Sel("alloc")), ObjC.Sel("init"));
        ObjC.MsgSend_void_nint_bool(menu, ObjC.Sel("setAutoenablesItems:"), false);
        return menu;
    }

    private static nint TargetClass()
    {
        nint existing = ObjC.GetClass(TARGET_CLASS);
        if (existing != 0)
        {
            return existing;
        }

        nint targetClass = ObjC.AllocateClassPair(ObjC.GetClass("NSObject"), TARGET_CLASS);
        _ = ObjC.AddMethod(targetClass, ObjC.Sel(ITEM_CHOSEN), (nint)(delegate* unmanaged<nint, nint, nint, void>)&ItemChosen, "v@:@");
        _ = ObjC.AddMethod(targetClass, ObjC.Sel(BUTTON_CLICKED), (nint)(delegate* unmanaged<nint, nint, nint, void>)&ButtonClicked, "v@:@");
        _ = ObjC.AddMethod(targetClass, ObjC.Sel(MENU_NEEDS_UPDATE), (nint)(delegate* unmanaged<nint, nint, nint, void>)&MenuNeedsUpdate, "v@:@");
        ObjC.RegisterClassPair(targetClass);
        return targetClass;
    }

    [UnmanagedCallersOnly]
    private static void ItemChosen(nint self, nint selector, nint sender)
    {
        try
        {
            if (_byTarget.TryGetValue(self, out var backend))
            {
                backend.OnItemChosen(sender);
            }
        }
        catch (Exception ex)
        {
            // An exception must not unwind into AppKit's frames.
            Application.RouteLifecycleException(ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void ButtonClicked(nint self, nint selector, nint sender)
    {
        try
        {
            if (_byTarget.TryGetValue(self, out var backend))
            {
                backend.OnButtonClicked();
            }
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void MenuNeedsUpdate(nint self, nint selector, nint menu)
    {
        try
        {
            if (_byTarget.TryGetValue(self, out var backend) && menu == backend._menu)
            {
                backend.Rebuild();
            }
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
        }
    }

    [LibraryImport(OBJC, EntryPoint = "objc_msgSend")]
    private static partial nint InitMenuItem(nint receiver, nint selector, nint title, nint action, nint keyEquivalent);

    [LibraryImport(OBJC, EntryPoint = "objc_msgSend")]
    private static partial nint InitWithCGImage(nint receiver, nint selector, nint cgImage, NSSize size);

    [LibraryImport(CORE_GRAPHICS)]
    private static partial nint CGColorSpaceCreateDeviceRGB();

    [LibraryImport(CORE_GRAPHICS)]
    private static partial void CGColorSpaceRelease(nint colorSpace);

    [LibraryImport(CORE_FOUNDATION)]
    private static partial nint CFDataCreate(nint allocator, byte* bytes, nint length);

    [LibraryImport(CORE_FOUNDATION)]
    private static partial void CFRelease(nint value);

    [LibraryImport(CORE_GRAPHICS)]
    private static partial nint CGDataProviderCreateWithCFData(nint data);

    [LibraryImport(CORE_GRAPHICS)]
    private static partial void CGDataProviderRelease(nint provider);

    [LibraryImport(CORE_GRAPHICS)]
    private static partial nint CGImageCreate(nuint width, nuint height, nuint bitsPerComponent, nuint bitsPerPixel,
        nuint bytesPerRow, nint colorSpace, uint bitmapInfo, nint provider, nint decode,
        [MarshalAs(UnmanagedType.U1)] bool shouldInterpolate, int intent);

    [LibraryImport(CORE_GRAPHICS)]
    private static partial void CGImageRelease(nint image);
}
