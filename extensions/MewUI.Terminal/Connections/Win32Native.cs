using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aprillz.MewUI.Terminal;

/// <summary>The kernel calls that run a program on a ConPTY pseudo-console.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Win32Native
{
    private const string KERNEL32 = "kernel32.dll";

    public const nint INVALID_HANDLE_VALUE = -1;

    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint STARTF_USESTDHANDLES = 0x00000100;
    public const nuint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    public const int ERROR_BROKEN_PIPE = 109;

    [StructLayout(LayoutKind.Sequential)]
    public struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfoW
    {
        public uint Cb;
        public char* LpReserved;
        public char* LpDesktop;
        public char* LpTitle;
        public uint DwX;
        public uint DwY;
        public uint DwXSize;
        public uint DwYSize;
        public uint DwXCountChars;
        public uint DwYCountChars;
        public uint DwFillAttribute;
        public uint DwFlags;
        public ushort WShowWindow;
        public ushort CbReserved2;
        public byte* LpReserved2;
        public nint HStdInput;
        public nint HStdOutput;
        public nint HStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfoExW
    {
        public StartupInfoW StartupInfo;
        public nint LpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public nint HProcess;
        public nint HThread;
        public uint DwProcessId;
        public uint DwThreadId;
    }

    [LibraryImport(KERNEL32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreatePipe(out nint readPipe, out nint writePipe, nint pipeAttributes, uint size);

    [LibraryImport(KERNEL32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport(KERNEL32)]
    public static partial int CreatePseudoConsole(Coord size, nint input, nint output, uint flags, out nint pseudoConsole);

    [LibraryImport(KERNEL32)]
    public static partial int ResizePseudoConsole(nint pseudoConsole, Coord size);

    [LibraryImport(KERNEL32)]
    public static partial void ClosePseudoConsole(nint pseudoConsole);

    [LibraryImport(KERNEL32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InitializeProcThreadAttributeList(nint attributeList, uint attributeCount, uint flags, ref nuint size);

    [LibraryImport(KERNEL32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateProcThreadAttribute(
        nint attributeList, uint flags, nuint attribute, nint value, nuint size, nint previousValue, nint returnSize);

    [LibraryImport(KERNEL32)]
    public static partial void DeleteProcThreadAttributeList(nint attributeList);

    [LibraryImport(KERNEL32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcessW(
        char* applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        char* environment,
        char* currentDirectory,
        StartupInfoExW* startupInfo,
        ProcessInformation* processInformation);

    [LibraryImport(KERNEL32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetExitCodeProcess(nint process, out uint exitCode);

    [LibraryImport(KERNEL32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(nint file, byte* buffer, uint bytesToRead, uint* bytesRead, nint overlapped);

    [LibraryImport(KERNEL32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(nint file, byte* buffer, uint bytesToWrite, uint* bytesWritten, nint overlapped);

    [LibraryImport(KERNEL32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CancelIoEx(nint file, nint overlapped);
}
