using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

internal static partial class Kernel32
{
    private const string LibraryName = "kernel32.dll";

    #region Module

    [LibraryImport(LibraryName, EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? lpModuleName);

    [LibraryImport(LibraryName, EntryPoint = "LoadLibraryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadLibrary(string lpLibFileName);

    [LibraryImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeLibrary(nint hLibModule);

    [LibraryImport(LibraryName, EntryPoint = "GetProcAddress", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetProcAddress(nint hModule, string lpProcName);

    #endregion

    #region Memory

    [LibraryImport(LibraryName)]
    public static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport(LibraryName)]
    public static partial nint GlobalLock(nint hMem);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint hMem);

    [LibraryImport(LibraryName)]
    public static partial nint GlobalFree(nint hMem);

    [LibraryImport(LibraryName)]
    public static partial nuint GlobalSize(nint hMem);

    [LibraryImport(LibraryName)]
    public static partial nint LocalAlloc(uint uFlags, nuint uBytes);

    [LibraryImport(LibraryName)]
    public static partial nint LocalFree(nint hMem);

    #endregion

    #region Error

    [LibraryImport(LibraryName)]
    public static partial uint GetLastError();

    [LibraryImport(LibraryName)]
    public static partial void SetLastError(uint dwErrCode);

    #endregion

    #region Process and Thread

    [LibraryImport(LibraryName)]
    public static partial uint GetCurrentThreadId();

    [LibraryImport(LibraryName)]
    public static partial uint GetCurrentProcessId();

    [LibraryImport(LibraryName)]
    public static partial nint GetCurrentProcess();

    [LibraryImport(LibraryName)]
    public static partial nint GetCurrentThread();

    #endregion

    #region Synchronization

    [LibraryImport(LibraryName, EntryPoint = "CreateEventW", SetLastError = true)]
    public static partial nint CreateEvent(nint lpEventAttributes, [MarshalAs(UnmanagedType.Bool)] bool bManualReset, [MarshalAs(UnmanagedType.Bool)] bool bInitialState, nint lpName);

    [LibraryImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetEvent(nint hEvent);

    [LibraryImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ResetEvent(nint hEvent);

    [LibraryImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    #endregion

    #region Console

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllocConsole();

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeConsole();

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint dwProcessId);

    #endregion

    #region High Resolution Timer

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryPerformanceCounter(out long lpPerformanceCount);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryPerformanceFrequency(out long lpFrequency);

    #endregion
}
