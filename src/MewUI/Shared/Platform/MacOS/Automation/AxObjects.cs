using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Platform.MacOS.Automation;

/// <summary>The Foundation objects accessibility methods return and receive: strings, numbers, and arrays.</summary>
internal static unsafe partial class AxObjects
{
    private static readonly nint _stringClass = ObjC.GetClass("NSString");
    private static readonly nint _numberClass = ObjC.GetClass("NSNumber");
    private static readonly nint _mutableArrayClass = ObjC.GetClass("NSMutableArray");
    private static readonly nint _selNumberWithDouble = ObjC.Sel("numberWithDouble:");
    private static readonly nint _selNumberWithInteger = ObjC.Sel("numberWithInteger:");
    private static readonly nint _selArrayWithCapacity = ObjC.Sel("arrayWithCapacity:");
    private static readonly nint _selAddObject = ObjC.Sel("addObject:");
    private static readonly nint _selIsKindOfClass = ObjC.Sel("isKindOfClass:");
    private static readonly nint _selUtf8String = ObjC.Sel("UTF8String");
    private static readonly nint _selDoubleValue = ObjC.Sel("doubleValue");
    private static readonly nint _selWindow = ObjC.Sel("window");

    [LibraryImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static partial void NSAccessibilityPostNotification(nint element, nint notification);

    /// <summary>An autoreleased NSString.</summary>
    internal static nint String(string value) => ObjC.CreateNSString(value);

    /// <summary>An autoreleased NSNumber holding a double.</summary>
    internal static nint Number(double value) => ObjC.MsgSend_nint_double(_numberClass, _selNumberWithDouble, value);

    /// <summary>An autoreleased NSNumber holding a non-negative integer.</summary>
    internal static nint Integer(int value) => ObjC.MsgSend_nint_ulong(_numberClass, _selNumberWithInteger, (ulong)value);

    /// <summary>An autoreleased NSMutableArray of the given objects.</summary>
    internal static nint Array(IReadOnlyList<nint> objects)
    {
        nint array = ObjC.MsgSend_nint_ulong(_mutableArrayClass, _selArrayWithCapacity, (ulong)objects.Count);
        foreach (nint item in objects)
        {
            ObjC.MsgSend_void_nint_nint(array, _selAddObject, item);
        }

        return array;
    }

    internal static bool TryReadString(nint value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        text = null;
        if (value != 0 && ObjC.MsgSend_bool_nint(value, _selIsKindOfClass, _stringClass))
        {
            text = Marshal.PtrToStringUTF8(ObjC.MsgSend_nint(value, _selUtf8String)) ?? string.Empty;
        }

        return text != null;
    }

    internal static bool TryReadNumber(nint value, out double number)
    {
        if (value != 0 && ObjC.MsgSend_bool_nint(value, _selIsKindOfClass, _numberClass))
        {
            number = ObjC.MsgSend_double(value, _selDoubleValue);
            return true;
        }

        if (TryReadString(value, out string? text) && double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out number))
        {
            return true;
        }

        number = 0;
        return false;
    }

    /// <summary>The NSWindow that shows <paramref name="view"/>, or nil.</summary>
    internal static nint WindowOfView(nint view) => view != 0 ? ObjC.MsgSend_nint(view, _selWindow) : 0;

    internal static void Post(nint element, string notification)
    {
        if (element != 0)
        {
            NSAccessibilityPostNotification(element, String(notification));
        }
    }
}
