namespace Aprillz.MewUI.Platform.MacOS;

internal sealed class MacOSMessageBoxService : IMessageBoxService
{
    private static bool _initialized;

    private static nint ClsNSAlert;
    private static nint ClsNSImage;

    private static nint SelAlloc;
    private static nint SelInit;
    private static nint SelSetMessageText;
    private static nint SelSetInformativeText;
    private static nint SelAddButtonWithTitle;
    private static nint SelSetAlertStyle;
    private static nint SelRunModal;
    private static nint SelSetIcon;
    private static nint SelImageNamed;
    private static nint SelInitWithSize;

    public MessageBoxResult Show(nint owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        MacOSInterop.EnsureApplicationInitialized();
        EnsureInitialized();

        using var pool = new MacOSInterop.AutoReleasePool();

        nint alert = ObjC.MsgSend_nint(ClsNSAlert, SelAlloc);
        alert = ObjC.MsgSend_nint(alert, SelInit);
        if (alert == 0)
        {
            return MessageBoxResult.Ok;
        }

        ObjC.MsgSend_void_nint_nint(alert, SelSetMessageText, ObjC.CreateNSString(caption ?? string.Empty));
        ObjC.MsgSend_void_nint_nint(alert, SelSetInformativeText, ObjC.CreateNSString(text ?? string.Empty));
        ObjC.MsgSend_void_nint_int(alert, SelSetAlertStyle, GetAlertStyle(icon));

        var iconImage = CreateIconImage(icon);
        if (iconImage != 0)
        {
            ObjC.MsgSend_void_nint_nint(alert, SelSetIcon, iconImage);
        }

        // Button ordering matters: NSAlert returns 1000 + index.
        AddButtons(alert, buttons);

        long response = ObjC.MsgSend_long(alert, SelRunModal);
        int index = (int)(response - 1000);
        return MapResult(buttons, index);
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        MacOSInterop.EnsureApplicationInitialized();

        ClsNSAlert = ObjC.GetClass("NSAlert");
        ClsNSImage = ObjC.GetClass("NSImage");

        SelAlloc = ObjC.Sel("alloc");
        SelInit = ObjC.Sel("init");
        SelSetMessageText = ObjC.Sel("setMessageText:");
        SelSetInformativeText = ObjC.Sel("setInformativeText:");
        SelAddButtonWithTitle = ObjC.Sel("addButtonWithTitle:");
        SelSetAlertStyle = ObjC.Sel("setAlertStyle:");
        SelRunModal = ObjC.Sel("runModal");
        SelSetIcon = ObjC.Sel("setIcon:");
        SelImageNamed = ObjC.Sel("imageNamed:");
        SelInitWithSize = ObjC.Sel("initWithSize:");

        _initialized = true;
    }

    private static nint CreateIconImage(MessageBoxIcon icon)
    {
        // On macOS, NSAlert defaults to the app icon. If the app has no icon, this can look like a generic "folder/app" icon.
        // For MessageBoxIcon.None we prefer not to show an icon at all, so we set an empty 1x1 NSImage.
        if (ClsNSImage == 0)
        {
            return 0;
        }

        nint named = 0;
        if (icon == MessageBoxIcon.Warning)
        {
            named = ObjC.MsgSend_nint_nint(ClsNSImage, SelImageNamed, ObjC.CreateNSString("NSImageNameCaution"));
        }
        else if (icon == MessageBoxIcon.Error)
        {
            // "Stop" variants differ by macOS version; this name exists on modern macOS.
            named = ObjC.MsgSend_nint_nint(ClsNSImage, SelImageNamed, ObjC.CreateNSString("NSImageNameStopProgressTemplate"));
            if (named == 0)
            {
                named = ObjC.MsgSend_nint_nint(ClsNSImage, SelImageNamed, ObjC.CreateNSString("NSImageNameCaution"));
            }
        }

        if (named != 0)
        {
            return named;
        }

        if (icon != MessageBoxIcon.None)
        {
            return 0;
        }

        // Empty image to suppress the default app icon.
        nint img = ObjC.MsgSend_nint(ClsNSImage, SelAlloc);
        img = ObjC.MsgSend_nint_size(img, SelInitWithSize, new NSSize(1, 1));
        return img;
    }

    private static void AddButtons(nint alert, MessageBoxButtons buttons)
    {
        switch (buttons)
        {
            case MessageBoxButtons.Ok:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, ObjC.CreateNSString("OK"));
                break;

            case MessageBoxButtons.OkCancel:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, ObjC.CreateNSString("OK"));
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, ObjC.CreateNSString("Cancel"));
                break;

            case MessageBoxButtons.YesNo:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, ObjC.CreateNSString("Yes"));
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, ObjC.CreateNSString("No"));
                break;

            case MessageBoxButtons.YesNoCancel:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, ObjC.CreateNSString("Yes"));
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, ObjC.CreateNSString("No"));
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, ObjC.CreateNSString("Cancel"));
                break;

            default:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, ObjC.CreateNSString("OK"));
                break;
        }
    }

    private static MessageBoxResult MapResult(MessageBoxButtons buttons, int buttonIndex)
    {
        // buttonIndex is 0-based in the order we added buttons.
        return buttons switch
        {
            MessageBoxButtons.Ok => MessageBoxResult.Ok,

            MessageBoxButtons.OkCancel => buttonIndex == 0 ? MessageBoxResult.Ok : MessageBoxResult.Cancel,

            MessageBoxButtons.YesNo => buttonIndex == 0 ? MessageBoxResult.Yes : MessageBoxResult.No,

            MessageBoxButtons.YesNoCancel => buttonIndex switch
            {
                0 => MessageBoxResult.Yes,
                1 => MessageBoxResult.No,
                _ => MessageBoxResult.Cancel
            },

            _ => MessageBoxResult.Ok
        };
    }

    private static int GetAlertStyle(MessageBoxIcon icon)
    {
        // NSAlertStyleInformational = 0, Warning = 1, Critical = 2
        return icon switch
        {
            MessageBoxIcon.Error => 2,
            MessageBoxIcon.Warning => 1,
            _ => 0
        };
    }
}
