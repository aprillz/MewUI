using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Aprillz.MewUI.Platform.Win32.Automation;

/// <summary>A rectangle in screen pixels as UI Automation passes it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

/// <summary>A point in screen pixels as UI Automation passes it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaPoint
{
    public double X;
    public double Y;
}

/// <summary>
/// A VARIANT holding one of the value kinds UI Automation properties use. A string allocates a BSTR, which
/// <see cref="Dispose"/> frees unless the VARIANT was handed to its new owner.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaVariant : IDisposable
{
    private const ushort VT_I4 = 3;
    private const ushort VT_R8 = 5;
    private const ushort VT_BSTR = 8;
    private const ushort VT_BOOL = 11;
    private const ushort VT_UNKNOWN = 13;

    private ushort _type;
    private ushort _reserved1;
    private ushort _reserved2;
    private ushort _reserved3;
    private nint _data;
    // The record arm of the union holds a second pointer, which sets the size of the VARIANT.
    private nint _record;

    internal static UiaVariant Create(int value)
    {
        var variant = new UiaVariant { _type = VT_I4 };
        Unsafe.As<nint, int>(ref variant._data) = value;
        return variant;
    }

    internal static UiaVariant Create(bool value)
    {
        var variant = new UiaVariant { _type = VT_BOOL };
        Unsafe.As<nint, short>(ref variant._data) = value ? (short)-1 : (short)0;
        return variant;
    }

    internal static UiaVariant Create(double value)
    {
        var variant = new UiaVariant { _type = VT_R8 };
        Unsafe.As<nint, double>(ref variant._data) = value;
        return variant;
    }

    internal static UiaVariant Create(string value)
        => new() { _type = VT_BSTR, _data = Marshal.StringToBSTR(value) };

    /// <summary>The value UI Automation reads as "this attribute is not supported".</summary>
    internal static UiaVariant NotSupported()
    {
        _ = UiaNative.UiaGetReservedNotSupportedValue(out nint value);
        return new UiaVariant { _type = VT_UNKNOWN, _data = value };
    }

    public void Dispose()
    {
        if (_type == VT_BSTR && _data != 0)
        {
            Marshal.FreeBSTR(_data);
        }

        this = default;
    }
}

/// <summary>The UI Automation core functions and identifiers the provider uses.</summary>
internal static unsafe partial class UiaNative
{
    internal const int S_OK = 0;
    internal const int E_INVALIDARG = unchecked((int)0x80070057);
    internal const int UIA_E_ELEMENTNOTENABLED = unchecked((int)0x80040200);
    internal const int UIA_E_ELEMENTNOTAVAILABLE = unchecked((int)0x80040201);
    internal const int UIA_E_INVALIDOPERATION = unchecked((int)0x80131509);
    internal const int UIA_E_TIMEOUT = unchecked((int)0x80131505);

    internal const int UIA_ROOT_OBJECT_ID = -25;
    internal const int UIA_APPEND_RUNTIME_ID = 3;

    internal const int PROVIDER_OPTIONS_SERVER_SIDE = 0x2;

    internal const int NAVIGATE_PARENT = 0;
    internal const int NAVIGATE_NEXT_SIBLING = 1;
    internal const int NAVIGATE_PREVIOUS_SIBLING = 2;
    internal const int NAVIGATE_FIRST_CHILD = 3;
    internal const int NAVIGATE_LAST_CHILD = 4;

    internal const int STRUCTURE_CHILDREN_INVALIDATED = 2;

    internal const int UIA_INVOKE_PATTERN_ID = 10000;
    internal const int UIA_SELECTION_PATTERN_ID = 10001;
    internal const int UIA_VALUE_PATTERN_ID = 10002;
    internal const int UIA_RANGE_VALUE_PATTERN_ID = 10003;
    internal const int UIA_SCROLL_PATTERN_ID = 10004;
    internal const int UIA_EXPAND_COLLAPSE_PATTERN_ID = 10005;
    internal const int UIA_SELECTION_ITEM_PATTERN_ID = 10010;
    internal const int UIA_TEXT_PATTERN_ID = 10014;
    internal const int UIA_TOGGLE_PATTERN_ID = 10015;
    internal const int UIA_SCROLL_ITEM_PATTERN_ID = 10017;

    internal const int UIA_TOOL_TIP_OPENED_EVENT_ID = 20000;
    internal const int UIA_TOOL_TIP_CLOSED_EVENT_ID = 20001;
    internal const int UIA_MENU_OPENED_EVENT_ID = 20003;
    internal const int UIA_AUTOMATION_FOCUS_CHANGED_EVENT_ID = 20005;
    internal const int UIA_MENU_CLOSED_EVENT_ID = 20007;
    internal const int UIA_ELEMENT_ADDED_TO_SELECTION_EVENT_ID = 20010;
    internal const int UIA_ELEMENT_REMOVED_FROM_SELECTION_EVENT_ID = 20011;
    internal const int UIA_ELEMENT_SELECTED_EVENT_ID = 20012;
    internal const int UIA_TEXT_SELECTION_CHANGED_EVENT_ID = 20014;
    internal const int UIA_TEXT_CHANGED_EVENT_ID = 20015;

    internal const int UIA_PROCESS_ID_PROPERTY_ID = 30002;
    internal const int UIA_CONTROL_TYPE_PROPERTY_ID = 30003;
    internal const int UIA_NAME_PROPERTY_ID = 30005;
    internal const int UIA_HAS_KEYBOARD_FOCUS_PROPERTY_ID = 30008;
    internal const int UIA_IS_KEYBOARD_FOCUSABLE_PROPERTY_ID = 30009;
    internal const int UIA_IS_ENABLED_PROPERTY_ID = 30010;
    internal const int UIA_AUTOMATION_ID_PROPERTY_ID = 30011;
    internal const int UIA_CLASS_NAME_PROPERTY_ID = 30012;
    internal const int UIA_HELP_TEXT_PROPERTY_ID = 30013;
    internal const int UIA_IS_CONTROL_ELEMENT_PROPERTY_ID = 30016;
    internal const int UIA_IS_CONTENT_ELEMENT_PROPERTY_ID = 30017;
    internal const int UIA_IS_PASSWORD_PROPERTY_ID = 30019;
    internal const int UIA_IS_OFFSCREEN_PROPERTY_ID = 30022;
    internal const int UIA_FRAMEWORK_ID_PROPERTY_ID = 30024;
    internal const int UIA_VALUE_VALUE_PROPERTY_ID = 30045;
    internal const int UIA_VALUE_IS_READ_ONLY_PROPERTY_ID = 30046;
    internal const int UIA_RANGE_VALUE_VALUE_PROPERTY_ID = 30047;
    internal const int UIA_RANGE_VALUE_IS_READ_ONLY_PROPERTY_ID = 30048;
    internal const int UIA_RANGE_VALUE_MINIMUM_PROPERTY_ID = 30049;
    internal const int UIA_RANGE_VALUE_MAXIMUM_PROPERTY_ID = 30050;
    internal const int UIA_SCROLL_HORIZONTAL_SCROLL_PERCENT_PROPERTY_ID = 30053;
    internal const int UIA_SCROLL_VERTICAL_SCROLL_PERCENT_PROPERTY_ID = 30055;
    internal const int UIA_EXPAND_COLLAPSE_STATE_PROPERTY_ID = 30070;
    internal const int UIA_SELECTION_ITEM_IS_SELECTED_PROPERTY_ID = 30079;
    internal const int UIA_TOGGLE_STATE_PROPERTY_ID = 30086;

    internal const int UIA_BUTTON_CONTROL_TYPE_ID = 50000;
    internal const int UIA_CALENDAR_CONTROL_TYPE_ID = 50001;
    internal const int UIA_CHECK_BOX_CONTROL_TYPE_ID = 50002;
    internal const int UIA_COMBO_BOX_CONTROL_TYPE_ID = 50003;
    internal const int UIA_EDIT_CONTROL_TYPE_ID = 50004;
    internal const int UIA_HYPERLINK_CONTROL_TYPE_ID = 50005;
    internal const int UIA_IMAGE_CONTROL_TYPE_ID = 50006;
    internal const int UIA_LIST_ITEM_CONTROL_TYPE_ID = 50007;
    internal const int UIA_LIST_CONTROL_TYPE_ID = 50008;
    internal const int UIA_MENU_CONTROL_TYPE_ID = 50009;
    internal const int UIA_MENU_BAR_CONTROL_TYPE_ID = 50010;
    internal const int UIA_MENU_ITEM_CONTROL_TYPE_ID = 50011;
    internal const int UIA_PROGRESS_BAR_CONTROL_TYPE_ID = 50012;
    internal const int UIA_RADIO_BUTTON_CONTROL_TYPE_ID = 50013;
    internal const int UIA_SCROLL_BAR_CONTROL_TYPE_ID = 50014;
    internal const int UIA_SLIDER_CONTROL_TYPE_ID = 50015;
    internal const int UIA_SPINNER_CONTROL_TYPE_ID = 50016;
    internal const int UIA_TAB_CONTROL_TYPE_ID = 50018;
    internal const int UIA_TAB_ITEM_CONTROL_TYPE_ID = 50019;
    internal const int UIA_TEXT_CONTROL_TYPE_ID = 50020;
    internal const int UIA_TOOL_BAR_CONTROL_TYPE_ID = 50021;
    internal const int UIA_TOOL_TIP_CONTROL_TYPE_ID = 50022;
    internal const int UIA_TREE_CONTROL_TYPE_ID = 50023;
    internal const int UIA_TREE_ITEM_CONTROL_TYPE_ID = 50024;
    internal const int UIA_CUSTOM_CONTROL_TYPE_ID = 50025;
    internal const int UIA_GROUP_CONTROL_TYPE_ID = 50026;
    internal const int UIA_DATA_GRID_CONTROL_TYPE_ID = 50028;
    internal const int UIA_DATA_ITEM_CONTROL_TYPE_ID = 50029;
    internal const int UIA_WINDOW_CONTROL_TYPE_ID = 50032;
    internal const int UIA_PANE_CONTROL_TYPE_ID = 50033;
    internal const int UIA_HEADER_CONTROL_TYPE_ID = 50034;
    internal const int UIA_HEADER_ITEM_CONTROL_TYPE_ID = 50035;
    internal const int UIA_SEPARATOR_CONTROL_TYPE_ID = 50038;

    private const ushort VT_I4 = 3;
    private const ushort VT_R8 = 5;
    private const ushort VT_UNKNOWN = 13;

    [LibraryImport("uiautomationcore.dll")]
    internal static partial nint UiaReturnRawElementProvider(nint hwnd, nint wParam, nint lParam, IRawElementProviderSimple? provider);

    [LibraryImport("uiautomationcore.dll")]
    internal static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);

    [LibraryImport("uiautomationcore.dll")]
    internal static partial int UiaRaiseAutomationEvent(IRawElementProviderSimple provider, int eventId);

    [LibraryImport("uiautomationcore.dll")]
    internal static partial int UiaRaiseAutomationPropertyChangedEvent(IRawElementProviderSimple provider, int propertyId, UiaVariant oldValue, UiaVariant newValue);

    [LibraryImport("uiautomationcore.dll")]
    internal static partial int UiaRaiseStructureChangedEvent(IRawElementProviderSimple provider, int structureChangeType, int* runtimeId, int runtimeIdLength);

    [LibraryImport("uiautomationcore.dll")]
    internal static partial int UiaClientsAreListening();

    [LibraryImport("uiautomationcore.dll")]
    internal static partial int UiaDisconnectProvider(IRawElementProviderSimple provider);

    [LibraryImport("uiautomationcore.dll")]
    internal static partial int UiaGetReservedNotSupportedValue(out nint value);

    [LibraryImport("oleaut32.dll")]
    private static partial nint SafeArrayCreateVector(ushort variantType, int lowerBound, uint elementCount);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayPutElement(nint array, int* indices, void* value);

    /// <summary>A one-dimensional SAFEARRAY of 32-bit integers, owned by the caller.</summary>
    internal static nint CreateInt32Array(ReadOnlySpan<int> values)
    {
        nint array = SafeArrayCreateVector(VT_I4, 0, (uint)values.Length);
        for (int index = 0; index < values.Length; index++)
        {
            int value = values[index];
            _ = SafeArrayPutElement(array, &index, &value);
        }

        return array;
    }

    /// <summary>A one-dimensional SAFEARRAY of doubles, owned by the caller.</summary>
    internal static nint CreateDoubleArray(ReadOnlySpan<double> values)
    {
        nint array = SafeArrayCreateVector(VT_R8, 0, (uint)values.Length);
        for (int index = 0; index < values.Length; index++)
        {
            double value = values[index];
            _ = SafeArrayPutElement(array, &index, &value);
        }

        return array;
    }

    /// <summary>A one-dimensional SAFEARRAY of the objects' IUnknown pointers, owned by the caller.</summary>
    internal static nint CreateInterfaceArray<T>(IReadOnlyList<T> objects)
    {
        nint array = SafeArrayCreateVector(VT_UNKNOWN, 0, (uint)objects.Count);
        for (int index = 0; index < objects.Count; index++)
        {
            // SafeArrayPutElement takes its own reference, so the one the marshaller made is released after.
            void* pointer = ComInterfaceMarshaller<T>.ConvertToUnmanaged(objects[index]);
            _ = SafeArrayPutElement(array, &index, pointer);
            ComInterfaceMarshaller<T>.Free(pointer);
        }

        return array;
    }
}
