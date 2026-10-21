// Based on Mewtty ConPtyBackend (ported from PuTTY windows/conpty.c) and Windows Terminal
// src/cascadia/TerminalConnection/ConptyConnection.cpp (MIT).

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace Aprillz.MewUI.Terminal;

/// <summary>A program running on a Windows pseudo-console, with a thread reading its output and one writing its input.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class ConPtyProcess : IPseudoTerminalProcess
{
    private const int READ_BUFFER_SIZE = 16384;

    private readonly ProcessConnection _owner;

    // Guards the handles against use after another thread closed them.
    private readonly object _handleLock = new();
    private nint _pseudoConsole = Win32Native.INVALID_HANDLE_VALUE;
    private nint _inputWrite = Win32Native.INVALID_HANDLE_VALUE;
    private nint _outputRead = Win32Native.INVALID_HANDLE_VALUE;
    private nint _process = Win32Native.INVALID_HANDLE_VALUE;
    private ProcessWaitHandle? _processWaitHandle;
    private RegisteredWaitHandle? _processWait;

    private readonly object _writeGate = new();
    private readonly Queue<byte[]> _queuedData = new();

    private volatile bool _terminated;
    private int _closedRaised;

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(nint process)
        {
            SafeWaitHandle = new SafeWaitHandle(process, ownsHandle: false);
        }
    }

    public ConPtyProcess(ProcessConnection owner)
    {
        _owner = owner;
    }

    public void Start()
    {
        nint inputRead = Win32Native.INVALID_HANDLE_VALUE;
        nint inputWrite = Win32Native.INVALID_HANDLE_VALUE;
        nint outputRead = Win32Native.INVALID_HANDLE_VALUE;
        nint outputWrite = Win32Native.INVALID_HANDLE_VALUE;
        nint pseudoConsole = Win32Native.INVALID_HANDLE_VALUE;
        Win32Native.StartupInfoExW startupInfo = default;
        bool attributeListInitialized = false;

        try
        {
            if (!Win32Native.CreatePipe(out inputRead, out inputWrite, 0, 0) || !Win32Native.CreatePipe(out outputRead, out outputWrite, 0, 0))
            {
                throw new IOException($"CreatePipe: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
            }

            var size = new Win32Native.Coord { X = (short)Math.Clamp(_owner.InitialColumns, 1, short.MaxValue), Y = (short)Math.Clamp(_owner.InitialRows, 1, short.MaxValue) };
            int result = Win32Native.CreatePseudoConsole(size, inputRead, outputWrite, 0, out pseudoConsole);
            if (result < 0)
            {
                pseudoConsole = Win32Native.INVALID_HANDLE_VALUE;
                throw new IOException($"CreatePseudoConsole failed: HRESULT=0x{unchecked((uint)result):x8}");
            }

            // The pseudo-console holds its own ends of the pipes now.
            Win32Native.CloseHandle(inputRead);
            inputRead = Win32Native.INVALID_HANDLE_VALUE;
            Win32Native.CloseHandle(outputWrite);
            outputWrite = Win32Native.INVALID_HANDLE_VALUE;

            startupInfo.StartupInfo.Cb = (uint)sizeof(Win32Native.StartupInfoExW);

            // Without this, a child of a process whose standard handles are redirected writes there instead of to the pseudo-console.
            startupInfo.StartupInfo.DwFlags = Win32Native.STARTF_USESTDHANDLES;

            nuint attributeSize = 0;
            Win32Native.InitializeProcThreadAttributeList(0, 1, 0, ref attributeSize);
            startupInfo.LpAttributeList = (nint)NativeMemory.Alloc(attributeSize);
            if (!Win32Native.InitializeProcThreadAttributeList(startupInfo.LpAttributeList, 1, 0, ref attributeSize))
            {
                throw new IOException($"InitializeProcThreadAttributeList: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
            }

            attributeListInitialized = true;

            if (!Win32Native.UpdateProcThreadAttribute(
                    startupInfo.LpAttributeList, 0, Win32Native.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, pseudoConsole, (nuint)sizeof(nint), 0, 0))
            {
                throw new IOException($"UpdateProcThreadAttribute: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
            }

            // The command line buffer must be writable, so it is a private copy with its terminator.
            string commandLine = BuildCommandLine(_owner.FileName, _owner.Arguments);
            char[] commandBuffer = new char[commandLine.Length + 1];
            commandLine.CopyTo(commandBuffer);

            string environmentBlock = BuildEnvironmentBlock(_owner.BuildEnvironment(setTerm: false));
            string? workingDirectory = _owner.WorkingDirectory;
            string? startingTitle = _owner.StartingTitle;

            Win32Native.ProcessInformation processInformation = default;
            bool created;
            fixed (char* commandPointer = commandBuffer)
            fixed (char* environmentPointer = environmentBlock)
            fixed (char* directoryPointer = workingDirectory)
            fixed (char* titlePointer = startingTitle)
            {
                startupInfo.StartupInfo.LpTitle = titlePointer;
                created = Win32Native.CreateProcessW(
                    null, commandPointer, 0, 0, false,
                    Win32Native.EXTENDED_STARTUPINFO_PRESENT | Win32Native.CREATE_UNICODE_ENVIRONMENT,
                    environmentPointer, directoryPointer, &startupInfo, &processInformation);
            }

            if (!created)
            {
                throw new IOException($"Unable to run '{_owner.FileName}': {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
            }

            Win32Native.CloseHandle(processInformation.HThread);

            _pseudoConsole = pseudoConsole;
            pseudoConsole = Win32Native.INVALID_HANDLE_VALUE;
            _inputWrite = inputWrite;
            inputWrite = Win32Native.INVALID_HANDLE_VALUE;
            _outputRead = outputRead;
            outputRead = Win32Native.INVALID_HANDLE_VALUE;
            _process = processInformation.HProcess;

            new Thread(ReadThread) { IsBackground = true, Name = "ConPTY output" }.Start();
            new Thread(WriteThread) { IsBackground = true, Name = "ConPTY input" }.Start();

            _processWaitHandle = new ProcessWaitHandle(_process);
            _processWait = ThreadPool.RegisterWaitForSingleObject(_processWaitHandle, (_, _) => OnProcessExited(), null, Timeout.Infinite, executeOnlyOnce: true);
        }
        finally
        {
            CloseIfValid(inputRead);
            CloseIfValid(inputWrite);
            CloseIfValid(outputRead);
            CloseIfValid(outputWrite);
            if (pseudoConsole != Win32Native.INVALID_HANDLE_VALUE)
            {
                Win32Native.ClosePseudoConsole(pseudoConsole);
            }

            if (attributeListInitialized)
            {
                Win32Native.DeleteProcThreadAttributeList(startupInfo.LpAttributeList);
            }

            NativeMemory.Free((void*)startupInfo.LpAttributeList);
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_terminated || data.IsEmpty)
        {
            return;
        }

        lock (_writeGate)
        {
            _queuedData.Enqueue(data.ToArray());
            Monitor.PulseAll(_writeGate);
        }
    }

    public void Resize(int columns, int rows)
    {
        var size = new Win32Native.Coord { X = (short)Math.Clamp(columns, 1, short.MaxValue), Y = (short)Math.Clamp(rows, 1, short.MaxValue) };
        lock (_handleLock)
        {
            if (_pseudoConsole != Win32Native.INVALID_HANDLE_VALUE)
            {
                Win32Native.ResizePseudoConsole(_pseudoConsole, size);
            }
        }
    }

    public void Dispose()
    {
        _terminated = true;

        lock (_writeGate)
        {
            _queuedData.Clear();
            Monitor.PulseAll(_writeGate);
        }

        // Each pipe is closed by the thread that uses it; here a blocked call is only cancelled.
        lock (_handleLock)
        {
            if (_outputRead != Win32Native.INVALID_HANDLE_VALUE)
            {
                Win32Native.CancelIoEx(_outputRead, 0);
            }

            if (_inputWrite != Win32Native.INVALID_HANDLE_VALUE)
            {
                Win32Native.CancelIoEx(_inputWrite, 0);
            }
        }

        StopProcessWait();
        ClosePseudoConsole();
        CloseProcessHandle();
    }

    private static void CloseIfValid(nint handle)
    {
        if (handle != Win32Native.INVALID_HANDLE_VALUE)
        {
            Win32Native.CloseHandle(handle);
        }
    }

    private void OnProcessExited()
    {
        StopProcessWait();

        // Closing the pseudo-console flushes its last output and then ends the output pipe, which ends the read thread.
        ClosePseudoConsole();
    }

    private void StopProcessWait()
    {
        var wait = Interlocked.Exchange(ref _processWait, null);
        wait?.Unregister(null);
    }

    private void ClosePseudoConsole()
    {
        nint pseudoConsole;
        lock (_handleLock)
        {
            pseudoConsole = _pseudoConsole;
            _pseudoConsole = Win32Native.INVALID_HANDLE_VALUE;
        }

        if (pseudoConsole != Win32Native.INVALID_HANDLE_VALUE)
        {
            // Closing waits for the console host to drain its output, which needs the read thread, so it runs elsewhere.
            ThreadPool.UnsafeQueueUserWorkItem(static handle => Win32Native.ClosePseudoConsole(handle), pseudoConsole, preferLocal: false);
        }
    }

    private void CloseProcessHandle()
    {
        nint process;
        lock (_handleLock)
        {
            process = _process;
            _process = Win32Native.INVALID_HANDLE_VALUE;
        }

        CloseIfValid(process);
        _processWaitHandle?.Dispose();
        _processWaitHandle = null;
    }

    private void ReadThread()
    {
        byte[] buffer = new byte[READ_BUFFER_SIZE];
        while (!_terminated)
        {
            uint length = 0;
            bool readOk;
            fixed (byte* bufferPointer = buffer)
            {
                readOk = Win32Native.ReadFile(_outputRead, bufferPointer, READ_BUFFER_SIZE, &length, 0);
            }

            // A broken pipe is the normal end of output; any other failure ends it too.
            if (!readOk || length == 0)
            {
                break;
            }

            if (!_terminated)
            {
                _owner.RaiseOutput(buffer.AsSpan(0, (int)length));
            }
        }

        lock (_handleLock)
        {
            Win32Native.CloseHandle(_outputRead);
            _outputRead = Win32Native.INVALID_HANDLE_VALUE;
        }

        RaiseClosed();
    }

    private void WriteThread()
    {
        while (true)
        {
            byte[] data;
            lock (_writeGate)
            {
                while (!_terminated && _queuedData.Count == 0)
                {
                    Monitor.Wait(_writeGate);
                }

                if (_terminated)
                {
                    break;
                }

                data = _queuedData.Dequeue();
            }

            int offset = 0;
            bool failed = false;
            fixed (byte* dataPointer = data)
            {
                while (offset < data.Length)
                {
                    uint written = 0;
                    if (!Win32Native.WriteFile(_inputWrite, dataPointer + offset, (uint)(data.Length - offset), &written, 0))
                    {
                        failed = true;
                        break;
                    }

                    offset += (int)written;
                }
            }

            if (failed)
            {
                break;
            }
        }

        lock (_handleLock)
        {
            Win32Native.CloseHandle(_inputWrite);
            _inputWrite = Win32Native.INVALID_HANDLE_VALUE;
        }
    }

    private void RaiseClosed()
    {
        if (Interlocked.Exchange(ref _closedRaised, 1) != 0 || _terminated)
        {
            return;
        }

        int exitCode = -1;
        lock (_handleLock)
        {
            if (_process != Win32Native.INVALID_HANDLE_VALUE && Win32Native.GetExitCodeProcess(_process, out uint status))
            {
                // STILL_ACTIVE: the output ended but the program did not.
                const uint STILL_ACTIVE = 259;
                exitCode = status == STILL_ACTIVE ? -1 : unchecked((int)status);
            }
        }

        _owner.RaiseClosed(exitCode);
    }

    /// <summary>Quotes the program and arguments so the C runtime splits the line back into the same arguments.</summary>
    internal static string BuildCommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        AppendArgument(builder, fileName);
        foreach (string argument in arguments)
        {
            builder.Append(' ');
            AppendArgument(builder, argument);
        }

        return builder.ToString();
    }

    private static void AppendArgument(StringBuilder builder, string argument)
    {
        if (argument.Length != 0 && argument.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        int index = 0;
        while (index < argument.Length)
        {
            int backslashes = 0;
            while (index < argument.Length && argument[index] == '\\')
            {
                backslashes++;
                index++;
            }

            if (index == argument.Length)
            {
                // Backslashes before the closing quote are doubled so they do not escape it.
                builder.Append('\\', backslashes * 2);
            }
            else if (argument[index] == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                index++;
            }
            else
            {
                builder.Append('\\', backslashes);
                builder.Append(argument[index]);
                index++;
            }
        }

        builder.Append('"');
    }

    /// <summary>A Unicode environment block: NAME=value strings each ending in NUL, sorted, then a final NUL.</summary>
    private static string BuildEnvironmentBlock(Dictionary<string, string> environment)
    {
        var keys = new List<string>(environment.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        foreach (string key in keys)
        {
            builder.Append(key).Append('=').Append(environment[key]).Append('\0');
        }

        builder.Append('\0');
        return builder.ToString();
    }
}
