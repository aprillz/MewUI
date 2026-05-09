using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// macOS-specific native interop for process memory and Metal GPU stats.
/// All methods are no-ops on non-macOS platforms.
/// </summary>
internal static partial class MacOsNative
{
    // -------------------------------------------------------------------------
    // task_info — process physical footprint (≈ Activity Monitor "Memory")
    // -------------------------------------------------------------------------

    private const uint TaskVmInfo = 22;

    // Minimum count to include phys_footprint (field at byte offset 144).
    // TASK_VM_INFO_REV1_COUNT = 38 natural_t units = 152 bytes.
    private const uint TaskVmInfoRev1Count = 38;

    // Byte offset of phys_footprint inside task_vm_info_data_t:
    //   virtual_size(8) + region_count(4) + page_size(4) + resident_size(8)
    //   + resident_size_peak(8) + device(8) + device_peak(8) + internal(8)
    //   + internal_peak(8) + external(8) + external_peak(8) + reusable(8)
    //   + reusable_peak(8) + purgeable_volatile_pmap(8)
    //   + purgeable_volatile_resident(8) + purgeable_volatile_virtual(8)
    //   + compressed(8) + compressed_peak(8) + compressed_lifetime(8) = 144
    private const int PhysFootprintOffset = 144;

    // mach_task_self_ in libsystem_kernel.dylib is an exported VARIABLE (mach_port_t), not a
    // function — calling it via P/Invoke jumps into __DATA_DIRTY and SIGBUSes. task_self_trap()
    // is the trap-style function that returns the same value (the current task's mach port).
    [LibraryImport("libSystem.dylib")]
    private static partial uint task_self_trap();

    [LibraryImport("libSystem.dylib")]
    private static unsafe partial int task_info(
        uint targetTask, uint flavor, void* taskInfoOut, ref uint taskInfoOutCount);

    /// <summary>
    /// Returns the process physical footprint — the value macOS Activity Monitor
    /// shows as "Memory". Equivalent to Windows "Private Bytes".
    /// Returns false on any failure (non-macOS, kernel error).
    /// </summary>
    public static bool TryGetPhysFootprint(out ulong physFootprint)
    {
        physFootprint = 0;

        if (!OperatingSystem.IsMacOS()) return false;

        Span<byte> buffer = stackalloc byte[256];
        uint count = TaskVmInfoRev1Count;
        int result;

        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                result = task_info(task_self_trap(), TaskVmInfo, ptr, ref count);
                if (result == 0 && count >= TaskVmInfoRev1Count)
                {
                    physFootprint = *(ulong*)(ptr + PhysFootprintOffset);
                }
            }
        }

        return result == 0;
    }

    // -------------------------------------------------------------------------
    // objc_msgSend — MTLDevice.currentAllocatedSize
    // -------------------------------------------------------------------------

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName",
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    // NSUInteger return (= uint64 on 64-bit) via standard objc_msgSend.
    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static partial ulong objc_msgSend_u64(nint receiver, nint selector);

    private static nint _selCurrentAllocatedSize;

    /// <summary>
    /// Returns the total GPU memory currently allocated on the given
    /// <c>id&lt;MTLDevice&gt;</c> handle by this process (Metal textures,
    /// buffers, etc.). Returns 0 when the device handle is invalid or on
    /// non-macOS platforms.
    /// </summary>
    public static ulong GetMetalAllocatedSize(nint metalDevice)
    {
        if (!OperatingSystem.IsMacOS() || metalDevice == 0) return 0;

        if (_selCurrentAllocatedSize == 0)
        {
            _selCurrentAllocatedSize = sel_registerName("currentAllocatedSize");
        }

        return objc_msgSend_u64(metalDevice, _selCurrentAllocatedSize);
    }
}
