using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

internal static partial class LibC
{
    private const string LibraryName = "libc";

    // poll(2)
    [LibraryImport(LibraryName, EntryPoint = "poll")]
    public static unsafe partial int Poll(PollFd* fds, nuint nfds, int timeoutMs);

    // pipe(2)
    [LibraryImport(LibraryName, EntryPoint = "pipe")]
    public static unsafe partial int Pipe(int* fds);

    // read(2)
    [LibraryImport(LibraryName, EntryPoint = "read")]
    public static unsafe partial nint Read(int fd, void* buf, nuint count);

    // write(2)
    [LibraryImport(LibraryName, EntryPoint = "write")]
    public static unsafe partial nint Write(int fd, void* buf, nuint count);

    // close(2)
    [LibraryImport(LibraryName, EntryPoint = "close")]
    public static partial int Close(int fd);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PollFd
{
    public int fd;
    public short events;
    public short revents;
}

internal static class PollEvents
{
    public const short POLLIN = 0x0001;
}
