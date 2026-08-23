using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Platform.Win32;

/// <summary>Reads the process memory counters through the process status API.</summary>
internal static unsafe partial class Win32ProcessMemory
{
    // PROCESS_MEMORY_COUNTERS_EX2 on 64-bit: the EX layout (80 bytes) plus PrivateWorkingSetSize and
    // SharedCommitUsage. Older systems reject the larger size, so the EX size is the fallback.
    private const uint COUNTERS_EX2_SIZE = 96;
    private const uint COUNTERS_EX_SIZE = 80;
    private const int WORKING_SET_OFFSET = 16;
    private const int PRIVATE_USAGE_OFFSET = 72;
    private const int PRIVATE_WORKING_SET_OFFSET = 80;

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(nint process, byte* counters, uint size);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    public static ProcessMemory Read()
    {
        byte* counters = stackalloc byte[(int)COUNTERS_EX2_SIZE];
        new Span<byte>(counters, (int)COUNTERS_EX2_SIZE).Clear();
        nint process = GetCurrentProcess();

        *(uint*)counters = COUNTERS_EX2_SIZE;
        bool hasPrivateWorkingSet = GetProcessMemoryInfo(process, counters, COUNTERS_EX2_SIZE);
        if (!hasPrivateWorkingSet)
        {
            *(uint*)counters = COUNTERS_EX_SIZE;
            if (!GetProcessMemoryInfo(process, counters, COUNTERS_EX_SIZE))
            {
                return new ProcessMemory(0, Environment.WorkingSet, 0);
            }
        }

        long workingSet = (long)*(nuint*)(counters + WORKING_SET_OFFSET);
        long privateUsage = (long)*(nuint*)(counters + PRIVATE_USAGE_OFFSET);
        long privateWorkingSet = hasPrivateWorkingSet ? (long)*(nuint*)(counters + PRIVATE_WORKING_SET_OFFSET) : 0;
        return new ProcessMemory(privateUsage, workingSet, privateWorkingSet);
    }
}
