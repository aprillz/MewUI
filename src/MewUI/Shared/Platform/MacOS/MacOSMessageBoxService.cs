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
    private static nint SelRelease;

    public bool? Show(nint owner, string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
    {
        MacOSInterop.EnsureApplicationInitialized();
        EnsureInitialized();

        using var pool = new MacOSInterop.AutoReleasePool();

        nint alert = ObjC.MsgSend_nint(ClsNSAlert, SelAlloc);
        alert = ObjC.MsgSend_nint(alert, SelInit);
        if (alert == 0)
        {
            return true;
        }

        ObjC.MsgSend_void_nint_nint(alert, SelSetMessageText, ObjC.CreateNSString(caption ?? string.Empty));
        ObjC.MsgSend_void_nint_nint(alert, SelSetInformativeText, ObjC.CreateNSString(text ?? string.Empty));
        ObjC.MsgSend_void_nint_int(alert, SelSetAlertStyle, GetAlertStyle(icon));

        var iconImage = CreateIconImage(icon, out bool ownsIconImage);
        if (iconImage != 0)
        {
            ObjC.MsgSend_void_nint_nint(alert, SelSetIcon, iconImage);
            if (ownsIconImage)
            {
                // setIcon: retains; drop our alloc/init +1 so the alert is the sole owner.
                ObjC.MsgSend_void(iconImage, SelRelease);
            }
        }

        // Button ordering matters: NSAlert returns 1000 + index.
        AddButtons(alert, buttons);

        long response = ObjC.MsgSend_long(alert, SelRunModal);
        // Balance the alloc/init +1; the alert is not reused across calls.
        ObjC.MsgSend_void(alert, SelRelease);
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
        SelRelease = ObjC.Sel("release");

        _initialized = true;
    }

    // ownsImage is true only for the alloc/init path; imageNamed: results are not owned by the caller.
    private static nint CreateIconImage(NativeMessageBoxIcon icon, out bool ownsImage)
    {
        ownsImage = false;

        // On macOS, NSAlert defaults to the app icon. If the app has no icon, this can look like a generic "folder/app" icon.
        // For NativeMessageBoxIcon.None we prefer not to show an icon at all, so we set an empty 1x1 NSImage.
        if (ClsNSImage == 0)
        {
            return 0;
        }

        nint named = 0;
        if (icon == NativeMessageBoxIcon.Warning)
        {
            named = ObjC.MsgSend_nint_nint(ClsNSImage, SelImageNamed, ObjC.CreateNSString("NSImageNameCaution"));
        }
        else if (icon == NativeMessageBoxIcon.Error)
        {
            named = ObjC.MsgSend_nint_nint(ClsNSImage, SelImageNamed, ObjC.CreateNSString("NSImageNameStopProgressTemplate"));
            if (named == 0)
            {
                named = ObjC.MsgSend_nint_nint(ClsNSImage, SelImageNamed, ObjC.CreateNSString("NSImageNameCaution"));
            }
        }
        else if (icon == NativeMessageBoxIcon.Information)
        {
            named = ObjC.MsgSend_nint_nint(ClsNSImage, SelImageNamed, ObjC.CreateNSString("NSImageNameInfo"));
        }
        else if (icon == NativeMessageBoxIcon.Question)
        {
            // macOS has no dedicated question icon; use Info as closest match.
            named = ObjC.MsgSend_nint_nint(ClsNSImage, SelImageNamed, ObjC.CreateNSString("NSImageNameInfo"));
        }

        if (named != 0)
        {
            return named;
        }

        if (icon != NativeMessageBoxIcon.None)
        {
            // Unknown icon type - let NSAlert use default (app icon).
            return 0;
        }

        // Empty image to suppress the default app icon.
        nint img = ObjC.MsgSend_nint(ClsNSImage, SelAlloc);
        img = ObjC.MsgSend_nint_size(img, SelInitWithSize, new NSSize(1, 1));
        ownsImage = img != 0;
        return img;
    }

    private static void AddButtons(nint alert, NativeMessageBoxButtons buttons)
    {
        switch (buttons)
        {
            case NativeMessageBoxButtons.Ok:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, GetNSString(MewUIStrings.CommonOK.Value));
                break;

            case NativeMessageBoxButtons.OkCancel:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, GetNSString(MewUIStrings.CommonOK.Value));
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, GetNSString(MewUIStrings.CommonCancel.Value));
                break;

            case NativeMessageBoxButtons.YesNo:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, GetNSString(MewUIStrings.CommonYes.Value));
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, GetNSString(MewUIStrings.CommonNo.Value));
                break;

            case NativeMessageBoxButtons.YesNoCancel:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, GetNSString(MewUIStrings.CommonYes.Value));
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, GetNSString(MewUIStrings.CommonNo.Value));
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, GetNSString(MewUIStrings.CommonCancel.Value));
                break;

            default:
                ObjC.MsgSend_nint_nint(alert, SelAddButtonWithTitle, GetNSString(MewUIStrings.CommonOK.Value));
                break;
        }
    }

    private static nint GetNSString(string value)
    {
        value = value.Replace("_", string.Empty);

        return ObjC.CreateNSString(value);
    }

    private static bool? MapResult(NativeMessageBoxButtons buttons, int buttonIndex)
    {
        // buttonIndex is 0-based in the order we added buttons.
        return buttons switch
        {
            NativeMessageBoxButtons.Ok => true,
            NativeMessageBoxButtons.OkCancel => buttonIndex == 0 ? true : false,
            NativeMessageBoxButtons.YesNo => buttonIndex == 0 ? true : null,
            NativeMessageBoxButtons.YesNoCancel => buttonIndex switch
            {
                0 => true,
                1 => (bool?)null,
                _ => false
            },

            _ => true
        };
    }

    private static int GetAlertStyle(NativeMessageBoxIcon icon)
    {
        // NSAlertStyleInformational = 0, Warning = 1, Critical = 2
        return icon switch
        {
            NativeMessageBoxIcon.Error => 2,
            NativeMessageBoxIcon.Warning => 1,
            _ => 0
        };
    }
}
