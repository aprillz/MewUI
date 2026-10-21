// Taken from Mewtty PosixNative: the C library calls and per-platform constants for a pty on Linux and macOS.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aprillz.MewUI.Terminal;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe partial class PosixNative
{
    private const string LIBC = "libc";

    private static readonly bool _macOS = OperatingSystem.IsMacOS();

    // Apple's arm64 calling convention passes variadic arguments on the stack, unlike every other target here.
    private static readonly bool _variadicOnStack = _macOS && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    // Room for the opaque C types below on every supported C library (the largest is 336 bytes).
    public const int SPAWNATTR_SIZE = 512;
    public const int FILE_ACTIONS_SIZE = 256;
    public const int SIGSET_SIZE = 128;
    public const int TERMIOS_SIZE = 128;

    public const int TERMIOS_IFLAG = 0;

    public const int O_RDWR = 2;
    public static readonly int O_NONBLOCK = _macOS ? 0x0004 : 0x0800;
    public static readonly int O_NOCTTY = _macOS ? 0x20000 : 0x0100;
    public static readonly int O_CLOEXEC = _macOS ? 0x1000000 : 0x80000;

    public const int F_SETFD = 2;
    public const int F_GETFL = 3;
    public const int F_SETFL = 4;
    public const int FD_CLOEXEC = 1;

    public const int EINTR = 4;
    public const int EIO = 5;
    public static readonly int EAGAIN = _macOS ? 35 : 11;

    public const short POLLIN = 0x0001;
    public const short POLLOUT = 0x0004;
    public const short POLLERR = 0x0008;
    public const short POLLHUP = 0x0010;
    public const short POLLNVAL = 0x0020;

    // macOS sys/ttycom.h _IOW('t', 103, struct winsize); Linux asm-generic/ioctls.h.
    public static readonly nuint TIOCSWINSZ = _macOS ? 0x80087467 : (nuint)0x5414;

    public const short POSIX_SPAWN_SETSIGDEF = 0x04;
    public const short POSIX_SPAWN_SETSIGMASK = 0x08;
    public static readonly short POSIX_SPAWN_SETSID = _macOS ? (short)0x0400 : (short)0x80;

    public const int SIGHUP = 1;
    public const int SIGINT = 2;
    public const int SIGQUIT = 3;
    public const int SIGPIPE = 13;

    public const int TCSANOW = 0;

    public static readonly int VERASE = _macOS ? 3 : 2;

    public const ulong IUTF8 = 0x4000;

    // Linux: four 32-bit flag words, c_line, then c_cc. macOS: four 64-bit flag words, then c_cc.
    private static readonly int _termiosFlagSize = _macOS ? 8 : 4;
    private static readonly int _termiosCcOffset = _macOS ? 32 : 17;

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Winsize
    {
        public ushort WsRow;
        public ushort WsCol;
        public ushort WsXpixel;
        public ushort WsYpixel;
    }

    [LibraryImport(LIBC, EntryPoint = "posix_openpt", SetLastError = true)]
    public static partial int PosixOpenpt(int flags);

    [LibraryImport(LIBC, EntryPoint = "grantpt", SetLastError = true)]
    public static partial int Grantpt(int fd);

    [LibraryImport(LIBC, EntryPoint = "unlockpt", SetLastError = true)]
    public static partial int Unlockpt(int fd);

    [LibraryImport(LIBC, EntryPoint = "ptsname_r", SetLastError = true)]
    private static partial int PtsnameR(int fd, byte* buffer, nuint length);

    [LibraryImport(LIBC, EntryPoint = "ptsname", SetLastError = true)]
    private static partial byte* PtsnameStatic(int fd);

    [LibraryImport(LIBC, EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Open(string path, int flags);

    [LibraryImport(LIBC, EntryPoint = "close", SetLastError = true)]
    public static partial int Close(int fd);

    [LibraryImport(LIBC, EntryPoint = "read", SetLastError = true)]
    public static partial nint Read(int fd, byte* buffer, nuint count);

    [LibraryImport(LIBC, EntryPoint = "write", SetLastError = true)]
    public static partial nint Write(int fd, byte* buffer, nuint count);

    [LibraryImport(LIBC, EntryPoint = "pipe", SetLastError = true)]
    public static partial int Pipe(int* fds);

    [LibraryImport(LIBC, EntryPoint = "poll", SetLastError = true)]
    public static partial int Poll(PollFd* fds, nuint count, int timeout);

    [LibraryImport(LIBC, EntryPoint = "kill", SetLastError = true)]
    public static partial int Kill(int pid, int signal);

    [LibraryImport(LIBC, EntryPoint = "waitpid", SetLastError = true)]
    public static partial int Waitpid(int pid, int* status, int options);

    [LibraryImport(LIBC, EntryPoint = "fcntl", SetLastError = true)]
    private static partial int FcntlRegister(int fd, int command, nint argument);

    [LibraryImport(LIBC, EntryPoint = "fcntl", SetLastError = true)]
    private static partial int FcntlStack(
        int fd, int command, nint unused2, nint unused3, nint unused4, nint unused5, nint unused6, nint unused7, nint argument);

    [LibraryImport(LIBC, EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlRegister(int fd, nuint request, void* argument);

    [LibraryImport(LIBC, EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlStack(
        int fd, nuint request, nint unused2, nint unused3, nint unused4, nint unused5, nint unused6, nint unused7, void* argument);

    [LibraryImport(LIBC, EntryPoint = "tcgetattr", SetLastError = true)]
    public static partial int Tcgetattr(int fd, byte* termios);

    [LibraryImport(LIBC, EntryPoint = "tcsetattr", SetLastError = true)]
    public static partial int Tcsetattr(int fd, int optionalActions, byte* termios);

    [LibraryImport(LIBC, EntryPoint = "posix_spawn")]
    public static partial int PosixSpawn(int* pid, byte* path, byte* fileActions, byte* attributes, byte** argv, byte** envp);

    [LibraryImport(LIBC, EntryPoint = "posix_spawnp")]
    public static partial int PosixSpawnp(int* pid, byte* file, byte* fileActions, byte* attributes, byte** argv, byte** envp);

    [LibraryImport(LIBC, EntryPoint = "posix_spawn_file_actions_init")]
    public static partial int PosixSpawnFileActionsInit(byte* fileActions);

    [LibraryImport(LIBC, EntryPoint = "posix_spawn_file_actions_destroy")]
    public static partial int PosixSpawnFileActionsDestroy(byte* fileActions);

    [LibraryImport(LIBC, EntryPoint = "posix_spawn_file_actions_addopen")]
    public static partial int PosixSpawnFileActionsAddopen(byte* fileActions, int fd, byte* path, int flags, uint mode);

    [LibraryImport(LIBC, EntryPoint = "posix_spawn_file_actions_adddup2")]
    public static partial int PosixSpawnFileActionsAdddup2(byte* fileActions, int fd, int newFd);

    [LibraryImport(LIBC, EntryPoint = "posix_spawn_file_actions_addclose")]
    public static partial int PosixSpawnFileActionsAddclose(byte* fileActions, int fd);

    [LibraryImport(LIBC, EntryPoint = "posix_spawnattr_init")]
    public static partial int PosixSpawnattrInit(byte* attributes);

    [LibraryImport(LIBC, EntryPoint = "posix_spawnattr_destroy")]
    public static partial int PosixSpawnattrDestroy(byte* attributes);

    [LibraryImport(LIBC, EntryPoint = "posix_spawnattr_setflags")]
    public static partial int PosixSpawnattrSetflags(byte* attributes, short flags);

    [LibraryImport(LIBC, EntryPoint = "posix_spawnattr_setsigdefault")]
    public static partial int PosixSpawnattrSetsigdefault(byte* attributes, byte* signals);

    [LibraryImport(LIBC, EntryPoint = "posix_spawnattr_setsigmask")]
    public static partial int PosixSpawnattrSetsigmask(byte* attributes, byte* signals);

    [LibraryImport(LIBC, EntryPoint = "sigemptyset")]
    public static partial int Sigemptyset(byte* signals);

    [LibraryImport(LIBC, EntryPoint = "sigaddset")]
    public static partial int Sigaddset(byte* signals, int signal);

    /// <summary>Calls fcntl with one integer argument.</summary>
    public static int Fcntl(int fd, int command, int argument)
    {
        if (_variadicOnStack)
        {
            return FcntlStack(fd, command, 0, 0, 0, 0, 0, 0, argument);
        }
        else
        {
            return FcntlRegister(fd, command, argument);
        }
    }

    /// <summary>Calls ioctl with one pointer argument.</summary>
    public static int Ioctl(int fd, nuint request, void* argument)
    {
        if (_variadicOnStack)
        {
            return IoctlStack(fd, request, 0, 0, 0, 0, 0, 0, argument);
        }
        else
        {
            return IoctlRegister(fd, request, argument);
        }
    }

    public static void Cloexec(int fd) => Fcntl(fd, F_SETFD, FD_CLOEXEC);

    public static void Nonblock(int fd)
    {
        int flags = Fcntl(fd, F_GETFL, 0);
        if (flags >= 0)
        {
            Fcntl(fd, F_SETFL, flags | O_NONBLOCK);
        }
    }

    /// <summary>The path of the terminal device paired with a pty master, or null on failure.</summary>
    public static string? Ptsname(int fd)
    {
        if (_macOS)
        {
            byte* name = PtsnameStatic(fd);
            return name == null ? null : Marshal.PtrToStringUTF8((nint)name);
        }
        else
        {
            const int NAME_SIZE = 4096;
            byte* buffer = stackalloc byte[NAME_SIZE];
            return PtsnameR(fd, buffer, NAME_SIZE) != 0 ? null : Marshal.PtrToStringUTF8((nint)buffer);
        }
    }

    public static string Strerror(int error) => Marshal.GetPInvokeErrorMessage(error);

    public static ulong TermiosGetFlags(byte* termios, int field)
    {
        byte* location = termios + (field * _termiosFlagSize);
        if (_termiosFlagSize == 8)
        {
            return *(ulong*)location;
        }
        else
        {
            return *(uint*)location;
        }
    }

    public static void TermiosSetFlags(byte* termios, int field, ulong value)
    {
        byte* location = termios + (field * _termiosFlagSize);
        if (_termiosFlagSize == 8)
        {
            *(ulong*)location = value;
        }
        else
        {
            *(uint*)location = (uint)value;
        }
    }

    public static void TermiosSetCc(byte* termios, int index, byte value) => termios[_termiosCcOffset + index] = value;
}
