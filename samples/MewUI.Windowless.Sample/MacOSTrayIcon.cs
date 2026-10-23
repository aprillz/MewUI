using System.Runtime.InteropServices;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Windowless.Sample;

/// <summary>
/// A menu bar status item through <c>NSStatusItem</c>. AppKit requires the main thread, which is MewUI's UI thread on
/// macOS, so <see cref="Start"/> runs there and the menu callbacks arrive there. The item's <c>NSMenu</c> is rebuilt
/// from the owner's <see cref="TrayIcon.ContextMenu"/> each time it opens.
/// </summary>
internal sealed unsafe class MacOSTrayIcon : ITrayIconBackend
{
    private const string OBJC = "/usr/lib/libobjc.A.dylib";
    private const string TARGET_CLASS = "MewUIWindowlessTrayTarget";
    private const string ACTION = "trayItemInvoked:";
    private const string MENU_NEEDS_UPDATE = "menuNeedsUpdate:";
    private const double VARIABLE_LENGTH = -1;
    private const double ICON_POINTS = 18;
    private const nint STATE_ON = 1;
    private const nint STATE_OFF = 0;

    // The action and delegate methods are static entry points; the sample has one status item at a time.
    private static MacOSTrayIcon? _current;

    private readonly List<MenuItem> _items = [];
    private nint _statusItem;
    private nint _menu;
    private nint _target;
    private TrayIcon? _owner;

    public string Name => "macOS NSStatusItem";

    public void Start(TrayIcon owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_statusItem != 0)
        {
            throw new InvalidOperationException("The status item is already shown.");
        }

        // A status item with a menu opens the menu on click, so the owner's Click is not raised here.
        _owner = owner;
        _current = this;

        nint statusBar = Send(Class("NSStatusBar"), Selector("systemStatusBar"));
        // The status bar does not keep the item alive; the item is released in Dispose.
        _statusItem = Send(Send(statusBar, Selector("statusItemWithLength:"), VARIABLE_LENGTH), Selector("retain"));
        if (_statusItem == 0)
        {
            throw new InvalidOperationException("NSStatusBar did not create a status item.");
        }

        nint button = Send(_statusItem, Selector("button"));
        nint image = CreateImage(owner.IconFile);
        if (image != 0)
        {
            SendVoid(button, Selector("setImage:"), image);
            Send(image, Selector("release"));
        }
        SendVoid(button, Selector("setToolTip:"), CreateString(owner.ToolTip));

        _target = Send(Send(TargetClass(), Selector("alloc")), Selector("init"));
        _menu = CreateMenu();
        // The delegate's menuNeedsUpdate: rebuilds the items just before the menu opens.
        SendVoid(_menu, Selector("setDelegate:"), _target);
        Rebuild();
        SendVoid(_statusItem, Selector("setMenu:"), _menu);

        // A status item that made it into the menu bar has a window for its button.
        nint buttonWindow = Send(button, Selector("window"));
        Console.Error.WriteLine(buttonWindow != 0
            ? "[windowless] status item placed in the menu bar"
            : "[windowless] status item has no menu bar window");
    }

    private void Rebuild()
    {
        _items.Clear();
        Send(_menu, Selector("removeAllItems"));
        var model = _owner?.ContextMenu;
        if (_owner != null && model != null)
        {
            AppendEntries(_menu, model.Items, _owner);
        }
    }

    private void AppendEntries(nint menu, IList<MenuEntry> entries, TrayIcon owner)
    {
        foreach (var entry in entries)
        {
            if (entry is not MenuItem item)
            {
                SendVoid(menu, Selector("addItem:"), Send(Class("NSMenuItem"), Selector("separatorItem")));
                continue;
            }

            nint nativeItem = Send(Send(Class("NSMenuItem"), Selector("alloc")), Selector("initWithTitle:action:keyEquivalent:"),
                CreateString(TrayMenuText.ForMacOS(item)), 0, CreateString(string.Empty));
            if (item.SubMenu != null)
            {
                nint subMenu = CreateMenu();
                AppendEntries(subMenu, item.SubMenu.Items, owner);
                SendVoid(nativeItem, Selector("setSubmenu:"), subMenu);
                Send(subMenu, Selector("release"));
                SendVoid(nativeItem, Selector("setEnabled:"), item.IsEnabled ? 1 : 0);
            }
            else
            {
                _items.Add(item);
                SendVoid(nativeItem, Selector("setTarget:"), _target);
                SendVoid(nativeItem, Selector("setAction:"), Selector(ACTION));
                SendVoid(nativeItem, Selector("setTag:"), _items.Count - 1);
                SendVoid(nativeItem, Selector("setEnabled:"), owner.CanInvoke(item) ? 1 : 0);
                SendVoid(nativeItem, Selector("setState:"), item.IsCheckable && item.IsChecked ? STATE_ON : STATE_OFF);
            }

            SendVoid(menu, Selector("addItem:"), nativeItem);
            Send(nativeItem, Selector("release"));
        }
    }

    private void Invoke(nint tag)
    {
        if (tag >= 0 && tag < _items.Count)
        {
            _owner?.Invoke(_items[(int)tag]);
        }
    }

    /// <summary>A menu whose items take their enabled state from the model, not from AppKit's validation.</summary>
    private static nint CreateMenu()
    {
        nint menu = Send(Send(Class("NSMenu"), Selector("alloc")), Selector("init"));
        SendVoid(menu, Selector("setAutoenablesItems:"), 0);
        return menu;
    }

    /// <summary>An image from the .ico file; ImageIO reads the format, and AppKit picks the representation to draw.</summary>
    private static nint CreateImage(byte[] iconFile)
    {
        nint data;
        fixed (byte* bytes = iconFile)
        {
            data = Send(Class("NSData"), Selector("dataWithBytes:length:"), (nint)bytes, iconFile.Length);
        }

        nint image = Send(Send(Class("NSImage"), Selector("alloc")), Selector("initWithData:"), data);
        if (image != 0)
        {
            SendVoid(image, Selector("setSize:"), ICON_POINTS, ICON_POINTS);
        }

        return image;
    }

    private static nint TargetClass()
    {
        nint existing = Class(TARGET_CLASS);
        if (existing != 0)
        {
            return existing;
        }

        nint targetClass = objc_allocateClassPair(Class("NSObject"), TARGET_CLASS, 0);
        class_addMethod(targetClass, Selector(ACTION), &ItemInvoked, "v@:@");
        class_addMethod(targetClass, Selector(MENU_NEEDS_UPDATE), &MenuNeedsUpdate, "v@:@");
        objc_registerClassPair(targetClass);
        return targetClass;
    }

    [UnmanagedCallersOnly]
    private static void ItemInvoked(nint self, nint selector, nint sender)
    {
        try
        {
            _current?.Invoke(Send(sender, Selector("tag")));
        }
        catch (Exception exception)
        {
            // An exception must not unwind into AppKit's frames.
            Console.Error.WriteLine($"[windowless] tray menu action failed: {exception}");
        }
    }

    [UnmanagedCallersOnly]
    private static void MenuNeedsUpdate(nint self, nint selector, nint menu)
    {
        try
        {
            var current = _current;
            if (current != null && menu == current._menu)
            {
                current.Rebuild();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[windowless] tray menu update failed: {exception}");
        }
    }

    public void Dispose()
    {
        if (_statusItem != 0)
        {
            SendVoid(Send(Class("NSStatusBar"), Selector("systemStatusBar")), Selector("removeStatusItem:"), _statusItem);
            Send(_statusItem, Selector("release"));
            _statusItem = 0;
        }
        if (_menu != 0)
        {
            SendVoid(_menu, Selector("setDelegate:"), 0);
            Send(_menu, Selector("release"));
            _menu = 0;
        }
        if (_target != 0)
        {
            Send(_target, Selector("release"));
            _target = 0;
        }
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
        _items.Clear();
        _owner = null;
    }

    private static nint Class(string name) => objc_getClass(name);

    private static nint Selector(string name) => sel_registerName(name);

    private static nint CreateString(string text) => SendString(Class("NSString"), Selector("stringWithUTF8String:"), text);

    [DllImport(OBJC)]
    private static extern nint objc_getClass(string name);

    [DllImport(OBJC)]
    private static extern nint sel_registerName(string name);

    [DllImport(OBJC)]
    private static extern nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);

    [DllImport(OBJC)]
    private static extern void objc_registerClassPair(nint objectClass);

    [DllImport(OBJC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(nint objectClass, nint selector,
        delegate* unmanaged<nint, nint, nint, void> implementation, string types);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, double argument);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint first, nint second);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint first, nint second, nint third);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector, nint argument);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector, double width, double height);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern nint SendString(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
}
