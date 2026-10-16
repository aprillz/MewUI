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

    public const int O_RDWR = 0x0002;
    public const int O_NONBLOCK = 0x0800;
    public const int O_CLOEXEC = 0x80000;
    public const int PROT_READ = 0x1;
    public const int PROT_WRITE = 0x2;
    public const int MAP_SHARED = 0x01;

    // open(2)
    [LibraryImport(LibraryName, EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int Open(string path, int flags);

    // open(2) for a path handed over as a C string.
    [LibraryImport(LibraryName, EntryPoint = "open", SetLastError = true)]
    public static unsafe partial int Open(byte* path, int flags);

    // ioctl(2) with a pointer argument.
    [LibraryImport(LibraryName, EntryPoint = "ioctl", SetLastError = true)]
    public static unsafe partial int Ioctl(int fd, nuint request, void* argument);

    // mmap(2)
    [LibraryImport(LibraryName, EntryPoint = "mmap", SetLastError = true)]
    public static unsafe partial void* Mmap(void* address, nuint length, int protection, int flags, int fd, nint offset);

    // munmap(2)
    [LibraryImport(LibraryName, EntryPoint = "munmap")]
    public static unsafe partial int Munmap(void* address, nuint length);
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
