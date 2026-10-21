// Based on Mewtty PtyBackend, ported from PuTTY unix/pty.c and unix/utils/subprocess_waiter.c (MIT).

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aprillz.MewUI.Terminal;

/// <summary>
/// A program running on a pty of Linux or macOS: one thread polls the master for output and input room, another waits for
/// the program's exit status.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed unsafe class UnixPtyProcess : IPseudoTerminalProcess
{
    private const string WRAPPER_SHELL = "/bin/sh";

    // macOS spawns open the terminal before the new session exists, so the child would have no controlling terminal; a
    // shell that reopens it as the session leader acquires it. $0 is the terminal, $1 the working directory or empty.
    private const string MACOS_WRAPPER_SCRIPT = "tty=\"$0\"; dir=\"$1\"; shift; if [ -n \"$dir\" ]; then cd \"$dir\" || exit 127; fi; exec \"$@\" 0<>\"$tty\" 1>&0 2>&0";

    // On Linux the spawn sets up the terminal itself; the wrapper only changes directory. $0 is the directory.
    private const string CHDIR_WRAPPER_SCRIPT = "cd \"$0\" || exit 127; exec \"$@\"";

    private const int READ_BUFFER_SIZE = 16384;

    // How long the end of output waits for the exit status, which the system reports a moment later.
    private const int EXIT_STATUS_GRACE_MS = 200;

    private readonly ProcessConnection _owner;

    // Guards the descriptors, the input queue and the exit status.
    private readonly object _stateLock = new();
    private int _masterFd = -1;
    private int _wakeReadFd = -1;
    private int _wakeWriteFd = -1;
    private readonly Queue<byte[]> _inputQueue = new();
    private int _inputOffset;
    private int _childPid;
    private bool _waitDone;
    private int _exitCode = -1;

    private readonly ManualResetEventSlim _waitDoneEvent = new(false);
    private volatile bool _closing;
    private Thread? _ioThread;
    private string _slaveName = string.Empty;
    private int _columns;
    private int _rows;

    public UnixPtyProcess(ProcessConnection owner)
    {
        _owner = owner;
    }

    public void Start()
    {
        _columns = Math.Clamp(_owner.InitialColumns, 1, ushort.MaxValue);
        _rows = Math.Clamp(_owner.InitialRows, 1, ushort.MaxValue);

        try
        {
            OpenMaster();

            int* pipeFds = stackalloc int[2];
            if (PosixNative.Pipe(pipeFds) < 0)
            {
                throw new IOException($"pipe: {PosixNative.Strerror(Marshal.GetLastPInvokeError())}");
            }

            _wakeReadFd = pipeFds[0];
            _wakeWriteFd = pipeFds[1];
            PosixNative.Cloexec(_wakeReadFd);
            PosixNative.Cloexec(_wakeWriteFd);

            // A full wake-up pipe already guarantees a wake-up, so a write to it must never wait.
            PosixNative.Nonblock(_wakeWriteFd);

            // The terminal settings are made here because the child runs none of our code before the program starts.
            int slaveFd = PosixNative.Open(_slaveName, PosixNative.O_RDWR | PosixNative.O_NOCTTY | PosixNative.O_CLOEXEC);
            if (slaveFd < 0)
            {
                throw new IOException($"open {_slaveName}: {PosixNative.Strerror(Marshal.GetLastPInvokeError())}");
            }

            try
            {
                ConfigureTerminal(slaveFd);
                SetWindowSize(slaveFd);
                SpawnChild();
            }
            finally
            {
                PosixNative.Close(slaveFd);
            }
        }
        catch
        {
            CloseDescriptors();
            throw;
        }

        new Thread(WaiterThread) { IsBackground = true, Name = "pty waiter" }.Start();
        _ioThread = new Thread(IoThread) { IsBackground = true, Name = "pty input/output" };
        _ioThread.Start();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_closing || data.IsEmpty)
        {
            return;
        }

        lock (_stateLock)
        {
            _inputQueue.Enqueue(data.ToArray());
        }

        Wake();
    }

    public void Resize(int columns, int rows)
    {
        _columns = Math.Clamp(columns, 1, ushort.MaxValue);
        _rows = Math.Clamp(rows, 1, ushort.MaxValue);
        lock (_stateLock)
        {
            if (_masterFd >= 0)
            {
                SetWindowSize(_masterFd);
            }
        }
    }

    public void Dispose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;

        // Closing the master hangs up the terminal, which ends the program's session (SIGHUP).
        if (_ioThread == null)
        {
            CloseDescriptors();
        }
        else
        {
            // The thread that polls the descriptors closes them, so that none is closed under a call in progress.
            Wake();
        }
    }

    private void OpenMaster()
    {
        int flags = PosixNative.O_RDWR | PosixNative.O_NOCTTY;

        // macOS does not let the master be made non-blocking afterwards, but accepts the flag here.
        if (OperatingSystem.IsMacOS())
        {
            flags |= PosixNative.O_NONBLOCK;
        }

        _masterFd = PosixNative.PosixOpenpt(flags);
        if (_masterFd < 0)
        {
            throw new IOException($"posix_openpt: {PosixNative.Strerror(Marshal.GetLastPInvokeError())}");
        }

        if (PosixNative.Grantpt(_masterFd) < 0)
        {
            throw new IOException($"grantpt: {PosixNative.Strerror(Marshal.GetLastPInvokeError())}");
        }

        if (PosixNative.Unlockpt(_masterFd) < 0)
        {
            throw new IOException($"unlockpt: {PosixNative.Strerror(Marshal.GetLastPInvokeError())}");
        }

        PosixNative.Cloexec(_masterFd);

        _slaveName = PosixNative.Ptsname(_masterFd)
            ?? throw new IOException($"ptsname: {PosixNative.Strerror(Marshal.GetLastPInvokeError())}");

        if (!OperatingSystem.IsMacOS())
        {
            PosixNative.Nonblock(_masterFd);
        }
    }

    private static void ConfigureTerminal(int slaveFd)
    {
        byte* attrs = stackalloc byte[PosixNative.TERMIOS_SIZE];
        new Span<byte>(attrs, PosixNative.TERMIOS_SIZE).Clear();

        if (PosixNative.Tcgetattr(slaveFd, attrs) == 0)
        {
            // Backspace sends DEL, so the line discipline must erase with it; input is UTF-8.
            PosixNative.TermiosSetCc(attrs, PosixNative.VERASE, 0x7F);
            ulong iflag = PosixNative.TermiosGetFlags(attrs, PosixNative.TERMIOS_IFLAG) | PosixNative.IUTF8;
            PosixNative.TermiosSetFlags(attrs, PosixNative.TERMIOS_IFLAG, iflag);
            PosixNative.Tcsetattr(slaveFd, PosixNative.TCSANOW, attrs);
        }
    }

    private void SetWindowSize(int fd)
    {
        PosixNative.Winsize size;
        size.WsRow = (ushort)_rows;
        size.WsCol = (ushort)_columns;
        size.WsXpixel = 0;
        size.WsYpixel = 0;
        PosixNative.Ioctl(fd, PosixNative.TIOCSWINSZ, &size);
    }

    private void SpawnChild()
    {
        var arguments = new List<string> { _owner.FileName };
        arguments.AddRange(_owner.Arguments);

        string program = _owner.FileName;
        string? workingDirectory = _owner.WorkingDirectory;
        if (OperatingSystem.IsMacOS())
        {
            arguments.InsertRange(0, [WRAPPER_SHELL, "-c", MACOS_WRAPPER_SCRIPT, _slaveName, workingDirectory ?? string.Empty]);
            program = WRAPPER_SHELL;
        }
        else if (!string.IsNullOrEmpty(workingDirectory))
        {
            arguments.InsertRange(0, [WRAPPER_SHELL, "-c", CHDIR_WRAPPER_SCRIPT, workingDirectory]);
            program = WRAPPER_SHELL;
        }

        var environmentStrings = new List<string>();
        foreach (var pair in _owner.BuildEnvironment(setTerm: true))
        {
            environmentStrings.Add(pair.Key + "=" + pair.Value);
        }

        byte* fileActions = stackalloc byte[PosixNative.FILE_ACTIONS_SIZE];
        byte* attributes = stackalloc byte[PosixNative.SPAWNATTR_SIZE];
        byte* defaultSignals = stackalloc byte[PosixNative.SIGSET_SIZE];
        byte* signalMask = stackalloc byte[PosixNative.SIGSET_SIZE];
        new Span<byte>(fileActions, PosixNative.FILE_ACTIONS_SIZE).Clear();
        new Span<byte>(attributes, PosixNative.SPAWNATTR_SIZE).Clear();

        byte** argv = BuildStringArray(arguments);
        byte** envp = BuildStringArray(environmentStrings);
        byte* path = (byte*)Marshal.StringToCoTaskMemUTF8(program);
        byte* slaveName = (byte*)Marshal.StringToCoTaskMemUTF8(_slaveName);

        PosixNative.PosixSpawnFileActionsInit(fileActions);
        PosixNative.PosixSpawnattrInit(attributes);
        try
        {
            // The child becomes a session leader first, so opening the terminal without O_NOCTTY makes it the controlling terminal.
            PosixNative.PosixSpawnFileActionsAddopen(fileActions, 0, slaveName, PosixNative.O_RDWR, 0);
            PosixNative.PosixSpawnFileActionsAdddup2(fileActions, 0, 1);
            PosixNative.PosixSpawnFileActionsAdddup2(fileActions, 0, 2);
            PosixNative.PosixSpawnFileActionsAddclose(fileActions, _masterFd);

            // SIGINT, SIGQUIT and SIGPIPE may be ignored by this process or the runtime; the child gets the defaults.
            PosixNative.Sigemptyset(defaultSignals);
            PosixNative.Sigaddset(defaultSignals, PosixNative.SIGINT);
            PosixNative.Sigaddset(defaultSignals, PosixNative.SIGQUIT);
            PosixNative.Sigaddset(defaultSignals, PosixNative.SIGPIPE);
            PosixNative.Sigemptyset(signalMask);
            PosixNative.PosixSpawnattrSetsigdefault(attributes, defaultSignals);
            PosixNative.PosixSpawnattrSetsigmask(attributes, signalMask);
            PosixNative.PosixSpawnattrSetflags(
                attributes,
                (short)(PosixNative.POSIX_SPAWN_SETSID | PosixNative.POSIX_SPAWN_SETSIGDEF | PosixNative.POSIX_SPAWN_SETSIGMASK));

            int pid = 0;
            int error = PosixNative.PosixSpawnp(&pid, path, fileActions, attributes, argv, envp);
            if (error != 0)
            {
                throw new IOException($"Unable to run '{_owner.FileName}': {PosixNative.Strerror(error)}");
            }

            _childPid = pid;
        }
        finally
        {
            PosixNative.PosixSpawnattrDestroy(attributes);
            PosixNative.PosixSpawnFileActionsDestroy(fileActions);
            Marshal.FreeCoTaskMem((nint)slaveName);
            Marshal.FreeCoTaskMem((nint)path);
            FreeStringArray(envp);
            FreeStringArray(argv);
        }
    }

    private static byte** BuildStringArray(List<string> values)
    {
        byte** array = (byte**)NativeMemory.AllocZeroed((nuint)(values.Count + 1), (nuint)sizeof(byte*));
        for (int index = 0; index < values.Count; index++)
        {
            array[index] = (byte*)Marshal.StringToCoTaskMemUTF8(values[index]);
        }

        return array;
    }

    private static void FreeStringArray(byte** array)
    {
        for (int index = 0; array[index] != null; index++)
        {
            Marshal.FreeCoTaskMem((nint)array[index]);
        }

        NativeMemory.Free(array);
    }

    private void Wake()
    {
        lock (_stateLock)
        {
            if (_wakeWriteFd >= 0)
            {
                byte value = 0;
                PosixNative.Write(_wakeWriteFd, &value, 1);
            }
        }
    }

    private void CloseDescriptors()
    {
        lock (_stateLock)
        {
            if (_masterFd >= 0)
            {
                PosixNative.Close(_masterFd);
                _masterFd = -1;
            }

            if (_wakeReadFd >= 0)
            {
                PosixNative.Close(_wakeReadFd);
                _wakeReadFd = -1;
            }

            if (_wakeWriteFd >= 0)
            {
                PosixNative.Close(_wakeWriteFd);
                _wakeWriteFd = -1;
            }

            _inputQueue.Clear();
        }
    }

    private void WaiterThread()
    {
        int status = 0;
        int result;
        do
        {
            result = PosixNative.Waitpid(_childPid, &status, 0);
        }
        while (result < 0 && Marshal.GetLastPInvokeError() == PosixNative.EINTR);

        if (result != _childPid)
        {
            return;
        }

        // A signal death reports 128 plus the signal, as shells do.
        int terminatingSignal = status & 0x7F;
        int exitCode = terminatingSignal == 0 ? (status >> 8) & 0xFF : 128 + terminatingSignal;

        lock (_stateLock)
        {
            _waitDone = true;
            _exitCode = exitCode;
        }

        _waitDoneEvent.Set();
    }

    private void IoThread()
    {
        int masterFd = _masterFd;
        int wakeReadFd = _wakeReadFd;
        byte[] buffer = new byte[READ_BUFFER_SIZE];
        PosixNative.PollFd* fds = stackalloc PosixNative.PollFd[2];
        byte* wakeBytes = stackalloc byte[64];
        bool endOfFile = false;

        while (!_closing)
        {
            short events = PosixNative.POLLIN;
            lock (_stateLock)
            {
                if (_inputQueue.Count > 0)
                {
                    events |= PosixNative.POLLOUT;
                }
            }

            fds[0].Fd = masterFd;
            fds[0].Events = events;
            fds[0].Revents = 0;
            fds[1].Fd = wakeReadFd;
            fds[1].Events = PosixNative.POLLIN;
            fds[1].Revents = 0;

            if (PosixNative.Poll(fds, 2, -1) < 0)
            {
                if (Marshal.GetLastPInvokeError() == PosixNative.EINTR)
                {
                    continue;
                }

                break;
            }

            if (fds[1].Revents != 0)
            {
                PosixNative.Read(wakeReadFd, wakeBytes, 64);
            }

            if (_closing)
            {
                break;
            }

            short revents = fds[0].Revents;
            if ((revents & (PosixNative.POLLIN | PosixNative.POLLHUP | PosixNative.POLLERR | PosixNative.POLLNVAL)) != 0)
            {
                // The pty hands out a line or a few at a time; reading on until it is empty passes the output on in
                // larger blocks, which the parser takes much faster than many small ones.
                int filled = 0;
                while (filled < READ_BUFFER_SIZE)
                {
                    nint read;
                    fixed (byte* bufferPointer = buffer)
                    {
                        read = PosixNative.Read(masterFd, bufferPointer + filled, (nuint)(READ_BUFFER_SIZE - filled));
                    }

                    int error = read < 0 ? Marshal.GetLastPInvokeError() : 0;

                    // EIO on a pty master is how the system reports that the last process on the other end went away.
                    if (read == 0 || (read < 0 && error == PosixNative.EIO))
                    {
                        endOfFile = true;
                        break;
                    }

                    if (read < 0)
                    {
                        if (error != PosixNative.EAGAIN && error != PosixNative.EINTR)
                        {
                            endOfFile = true;
                        }

                        break;
                    }

                    filled += (int)read;
                }

                if (filled > 0)
                {
                    _owner.RaiseOutput(buffer.AsSpan(0, filled));
                }

                if (endOfFile)
                {
                    break;
                }
            }

            if ((revents & PosixNative.POLLOUT) != 0 && !TryWrite(masterFd))
            {
                endOfFile = true;
                break;
            }
        }

        CloseDescriptors();

        if (!_closing && endOfFile)
        {
            _waitDoneEvent.Wait(EXIT_STATUS_GRACE_MS);
            int exitCode;
            lock (_stateLock)
            {
                exitCode = _waitDone ? _exitCode : -1;
            }

            _owner.RaiseClosed(exitCode);
        }
    }

    /// <summary>Writes queued input until the pty would block; false on a write error.</summary>
    private bool TryWrite(int masterFd)
    {
        while (true)
        {
            byte[] data;
            int offset;
            lock (_stateLock)
            {
                if (_inputQueue.Count == 0)
                {
                    return true;
                }

                data = _inputQueue.Peek();
                offset = _inputOffset;
            }

            nint written;
            fixed (byte* dataPointer = data)
            {
                written = PosixNative.Write(masterFd, dataPointer + offset, (nuint)(data.Length - offset));
            }

            if (written < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                return error == PosixNative.EAGAIN || error == PosixNative.EINTR;
            }

            lock (_stateLock)
            {
                // The queue is emptied when the connection closes while this write was under way.
                if (_inputQueue.Count == 0)
                {
                    return true;
                }

                _inputOffset += (int)written;
                if (_inputOffset >= data.Length)
                {
                    _inputQueue.Dequeue();
                    _inputOffset = 0;
                }
            }
        }
    }
}
