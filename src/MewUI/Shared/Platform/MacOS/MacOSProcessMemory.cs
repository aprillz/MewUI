using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>Reads the process memory counters from the task's virtual memory statistics.</summary>
internal static unsafe partial class MacOSProcessMemory
{
    // task_vm_info field offsets: resident_size, internal (resident anonymous pages), and
    // phys_footprint, which is the figure the system reports as the process's memory.
    private const int TASK_VM_INFO = 22;
    private const int INFO_BUFFER_BYTES = 512;
    private const int RESIDENT_SIZE_OFFSET = 16;
    private const int INTERNAL_OFFSET = 48;
    private const int PHYS_FOOTPRINT_OFFSET = 144;
    private const uint MINIMUM_COUNT = (PHYS_FOOTPRINT_OFFSET + 8) / 4;

    [LibraryImport("/usr/lib/libSystem.B.dylib")]
    private static partial uint task_self_trap();

    [LibraryImport("/usr/lib/libSystem.B.dylib")]
    private static partial int task_info(uint task, int flavor, byte* info, uint* count);

    public static ProcessMemory Read()
    {
        byte* info = stackalloc byte[INFO_BUFFER_BYTES];
        new Span<byte>(info, INFO_BUFFER_BYTES).Clear();
        uint count = INFO_BUFFER_BYTES / 4;
        if (task_info(task_self_trap(), TASK_VM_INFO, info, &count) != 0 || count < MINIMUM_COUNT)
        {
            return new ProcessMemory(0, Environment.WorkingSet, 0);
        }

        long residentSize = (long)*(ulong*)(info + RESIDENT_SIZE_OFFSET);
        long internalSize = (long)*(ulong*)(info + INTERNAL_OFFSET);
        long physFootprint = (long)*(ulong*)(info + PHYS_FOOTPRINT_OFFSET);
        return new ProcessMemory(physFootprint, residentSize, internalSize);
    }
}
