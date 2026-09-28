using System.Runtime.InteropServices;
using System.Text;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>sysctl, IOKit registry and CoreFoundation lookups the macOS readers share.</summary>
internal static class MacNative
{
    private const string SYSTEM = "/usr/lib/libSystem.B.dylib";
    private const string IO_KIT = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CORE_FOUNDATION = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint UTF8_ENCODING = 0x08000100;
    private const int NUMBER_SINT64 = 4;
    private const int NUMBER_DOUBLE = 13;

    public static long? SysctlInt64(string name)
    {
        nuint length = sizeof(long);
        long value = 0;
        if (sysctlbyname(name, ref value, ref length, 0, 0) != 0) return null;
        // Some keys are 32-bit; the upper half then stays zero.
        return length == sizeof(int) ? (int)value : value;
    }

    public static string? SysctlString(string name)
    {
        nuint length = 0;
        if (sysctlbyname(name, 0, ref length, 0, 0) != 0 || length == 0) return null;
        var buffer = new byte[(int)length];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            if (sysctlbyname(name, handle.AddrOfPinnedObject(), ref length, 0, 0) != 0) return null;
            return Encoding.UTF8.GetString(buffer, 0, (int)length).TrimEnd('\0');
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Visits every registry entry of an IOKit class, handing over its properties dictionary.</summary>
    public static void VisitServices(string className, Action<uint, nint> visit)
    {
        nint matching = IOServiceMatching(className);
        if (matching == 0) return;
        // IOServiceGetMatchingServices consumes the matching dictionary.
        if (IOServiceGetMatchingServices(0, matching, out uint iterator) != 0) return;
        try
        {
            uint service;
            while ((service = IOIteratorNext(iterator)) != 0)
            {
                try
                {
                    if (IORegistryEntryCreateCFProperties(service, out nint properties, 0, 0) == 0 && properties != 0)
                    {
                        try { visit(service, properties); }
                        finally { CFRelease(properties); }
                    }
                }
                finally
                {
                    IOObjectRelease(service);
                }
            }
        }
        finally
        {
            IOObjectRelease(iterator);
        }
    }

    /// <summary>The properties of the first child in the service plane, or 0.</summary>
    public static nint ChildProperties(uint entry)
    {
        if (IORegistryEntryGetChildEntry(entry, "IOService", out uint child) != 0) return 0;
        try
        {
            return IORegistryEntryCreateCFProperties(child, out nint properties, 0, 0) == 0 ? properties : 0;
        }
        finally
        {
            IOObjectRelease(child);
        }
    }

    /// <summary>The properties of the parent in the service plane, or 0.</summary>
    public static nint ParentProperties(uint entry)
    {
        if (IORegistryEntryGetParentEntry(entry, "IOService", out uint parent) != 0) return 0;
        try
        {
            return IORegistryEntryCreateCFProperties(parent, out nint properties, 0, 0) == 0 ? properties : 0;
        }
        finally
        {
            IOObjectRelease(parent);
        }
    }

    public static nint Value(nint dictionary, string key)
    {
        if (dictionary == 0) return 0;
        nint cfKey = CFStringCreateWithCString(0, key, UTF8_ENCODING);
        try { return CFDictionaryGetValue(dictionary, cfKey); }
        finally { CFRelease(cfKey); }
    }

    public static nint Dictionary(nint dictionary, string key)
    {
        nint value = Value(dictionary, key);
        return value != 0 && CFGetTypeID(value) == CFDictionaryGetTypeID() ? value : 0;
    }

    public static long? Int64(nint dictionary, string key)
    {
        nint value = Value(dictionary, key);
        if (value == 0 || CFGetTypeID(value) != CFNumberGetTypeID()) return null;
        return CFNumberGetValue(value, NUMBER_SINT64, out long number) ? number : null;
    }

    public static double? Double(nint dictionary, string key)
    {
        nint value = Value(dictionary, key);
        if (value == 0 || CFGetTypeID(value) != CFNumberGetTypeID()) return null;
        return CFNumberGetValue(value, NUMBER_DOUBLE, out double number) ? number : null;
    }

    public static bool? Boolean(nint dictionary, string key)
    {
        nint value = Value(dictionary, key);
        if (value == 0 || CFGetTypeID(value) != CFBooleanGetTypeID()) return null;
        return CFBooleanGetValue(value);
    }

    public static string? String(nint dictionary, string key)
    {
        nint value = Value(dictionary, key);
        if (value == 0 || CFGetTypeID(value) != CFStringGetTypeID()) return null;
        var buffer = new byte[1024];
        if (!CFStringGetCString(value, buffer, buffer.Length, UTF8_ENCODING)) return null;
        int end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end >= 0 ? end : buffer.Length);
    }

    public static void Release(nint value)
    {
        if (value != 0) CFRelease(value);
    }

    [DllImport(SYSTEM)]
    private static extern int sysctlbyname(string name, ref long oldValue, ref nuint oldLength, nint newValue, nuint newLength);

    [DllImport(SYSTEM)]
    private static extern int sysctlbyname(string name, nint oldValue, ref nuint oldLength, nint newValue, nuint newLength);

    [DllImport(IO_KIT)]
    private static extern nint IOServiceMatching(string name);

    [DllImport(IO_KIT)]
    private static extern int IOServiceGetMatchingServices(uint mainPort, nint matching, out uint iterator);

    [DllImport(IO_KIT)]
    private static extern uint IOIteratorNext(uint iterator);

    [DllImport(IO_KIT)]
    private static extern int IOObjectRelease(uint entry);

    [DllImport(IO_KIT)]
    private static extern int IORegistryEntryCreateCFProperties(uint entry, out nint properties, nint allocator, uint options);

    [DllImport(IO_KIT)]
    private static extern int IORegistryEntryGetChildEntry(uint entry, string plane, out uint child);

    [DllImport(IO_KIT)]
    private static extern int IORegistryEntryGetParentEntry(uint entry, string plane, out uint parent);

    [DllImport(CORE_FOUNDATION)]
    private static extern nint CFStringCreateWithCString(nint allocator, string value, uint encoding);

    [DllImport(CORE_FOUNDATION)]
    private static extern nint CFDictionaryGetValue(nint dictionary, nint key);

    [DllImport(CORE_FOUNDATION)]
    private static extern nuint CFGetTypeID(nint value);

    [DllImport(CORE_FOUNDATION)]
    private static extern nuint CFDictionaryGetTypeID();

    [DllImport(CORE_FOUNDATION)]
    private static extern nuint CFNumberGetTypeID();

    [DllImport(CORE_FOUNDATION)]
    private static extern nuint CFStringGetTypeID();

    [DllImport(CORE_FOUNDATION)]
    private static extern nuint CFBooleanGetTypeID();

    [DllImport(CORE_FOUNDATION)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(nint number, int type, out long value);

    [DllImport(CORE_FOUNDATION)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(nint number, int type, out double value);

    [DllImport(CORE_FOUNDATION)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFBooleanGetValue(nint boolean);

    [DllImport(CORE_FOUNDATION)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(nint value, byte[] buffer, nint bufferSize, uint encoding);

    [DllImport(CORE_FOUNDATION)]
    private static extern void CFRelease(nint value);
}
