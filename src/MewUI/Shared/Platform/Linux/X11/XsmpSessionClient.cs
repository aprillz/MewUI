using System.Runtime.InteropServices;
using System.Text;

using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Client of the X session manager through libSM (XSMP). A logout that lets clients talk to the user becomes
/// the application's quit request, which can refuse it and so cancel the logout; the manager's Die ends the run.
/// </summary>
internal sealed unsafe partial class XsmpSessionClient : IDisposable
{
    private const string SM_LIBRARY = "libSM.so.6";
    private const string ICE_LIBRARY = "libICE.so.6";
    private const int INTERACT_STYLE_ANY = 2;
    private const int DIALOG_NORMAL = 1;
    private const byte RESTART_NEVER = 3;
    private const int PROCESS_MESSAGES_IO_ERROR = 1;
    private const short POLL_HANG_UP_OR_ERROR = 0x0010 | 0x0008;

    // Save yourself, die, save complete and shutdown cancelled.
    private const nuint CALLBACK_MASK = 0xF;

    // libSM hands its callbacks no managed state, and a process holds one session connection.
    private static XsmpSessionClient? _active;

    private nint _connection;
    private nint _iceConnection;
    // Set while a logout waits for the application's decision, so the manager is answered once.
    private bool _awaitingDecision;

    private XsmpSessionClient()
    {
    }

    /// <summary>Gets the descriptor that becomes readable when the session manager sends a message, or -1.</summary>
    public int FileDescriptor { get; private set; } = -1;

    /// <summary>
    /// Connects to the session manager named by SESSION_MANAGER, or returns null when there is none or libSM
    /// is missing.
    /// </summary>
    public static XsmpSessionClient? TryConnect()
    {
        if (_active != null || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SESSION_MANAGER")))
        {
            return null;
        }

        var client = new XsmpSessionClient();
        try
        {
            if (!client.Open())
            {
                return null;
            }
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        _active = client;
        return client;
    }

    /// <summary>Handles what the session manager has sent, without waiting when nothing has arrived.</summary>
    public void ProcessPending()
    {
        if (_iceConnection == 0)
        {
            return;
        }

        var descriptor = new PollFd { fd = FileDescriptor, events = PollEvents.POLLIN };
        if (LibC.Poll(&descriptor, 1, 0) <= 0 || (descriptor.revents & (PollEvents.POLLIN | POLL_HANG_UP_OR_ERROR)) == 0)
        {
            return;
        }

        int status = IceProcessMessages(_iceConnection, 0, 0);
        if (status == PROCESS_MESSAGES_IO_ERROR)
        {
            Dispose();
        }
        else if (status != 0)
        {
            // The connection closed and libICE freed it already.
            Forget();
        }
    }

    public void Dispose()
    {
        if (_connection != 0)
        {
            _ = SmcCloseConnection(_connection, 0, 0);
        }

        Forget();
    }

    private bool Open()
    {
        // The default handlers exit the process when the manager goes away or sends something malformed;
        // these leave the connection to be closed by the next ProcessPending instead.
        _ = IceSetIOErrorHandler((nint)(delegate* unmanaged<nint, void>)&OnIceIOError);
        _ = IceSetErrorHandler((nint)(delegate* unmanaged<nint, int, int, nuint, int, int, nint, void>)&OnProtocolError);
        _ = SmcSetErrorHandler((nint)(delegate* unmanaged<nint, int, int, nuint, int, int, nint, void>)&OnProtocolError);

        var callbacks = new SmcCallbacks
        {
            SaveYourself = (nint)(delegate* unmanaged<nint, nint, int, int, int, int, void>)&OnSaveYourself,
            Die = (nint)(delegate* unmanaged<nint, nint, void>)&OnDie,
            SaveComplete = (nint)(delegate* unmanaged<nint, nint, void>)&OnSaveComplete,
            ShutdownCancelled = (nint)(delegate* unmanaged<nint, nint, void>)&OnShutdownCancelled,
        };

        const int ERROR_LENGTH = 256;
        byte* error = stackalloc byte[ERROR_LENGTH];
        nint clientId = 0;
        _connection = SmcOpenConnection(0, 0, 1, 0, CALLBACK_MASK, &callbacks, 0, &clientId, ERROR_LENGTH, error);
        if (_connection == 0)
        {
            return false;
        }

        if (clientId != 0)
        {
            Free(clientId);
        }

        _iceConnection = SmcGetIceConnection(_connection);
        FileDescriptor = IceConnectionNumber(_iceConnection);
        SetRequiredProperties();
        return true;
    }

    private void Forget()
    {
        _connection = 0;
        _iceConnection = 0;
        FileDescriptor = -1;
        _awaitingDecision = false;
        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }
    }

    /// <summary>
    /// Sets the properties every XSMP client must have, with a restart hint that keeps the manager from
    /// starting the application again at the next login.
    /// </summary>
    private void SetRequiredProperties()
    {
        string program = Environment.ProcessPath ?? "mewui";
        var allocations = new List<nint>();
        nint Utf8(string text)
        {
            nint pointer = Marshal.StringToCoTaskMemUTF8(text);
            allocations.Add(pointer);
            return pointer;
        }

        try
        {
            var programValue = new SmPropValue { Length = Encoding.UTF8.GetByteCount(program), Value = Utf8(program) };
            var userValue = new SmPropValue { Length = Encoding.UTF8.GetByteCount(Environment.UserName), Value = Utf8(Environment.UserName) };
            byte restartNever = RESTART_NEVER;
            var restartValue = new SmPropValue { Length = 1, Value = (nint)(&restartNever) };

            const int COUNT = 5;
            SmProp* properties = stackalloc SmProp[COUNT];
            properties[0] = new SmProp { Name = Utf8("Program"), Type = Utf8("ARRAY8"), ValueCount = 1, Values = &programValue };
            properties[1] = new SmProp { Name = Utf8("UserID"), Type = Utf8("ARRAY8"), ValueCount = 1, Values = &userValue };
            properties[2] = new SmProp { Name = Utf8("RestartCommand"), Type = Utf8("LISTofARRAY8"), ValueCount = 1, Values = &programValue };
            properties[3] = new SmProp { Name = Utf8("CloneCommand"), Type = Utf8("LISTofARRAY8"), ValueCount = 1, Values = &programValue };
            properties[4] = new SmProp { Name = Utf8("RestartStyleHint"), Type = Utf8("CARD8"), ValueCount = 1, Values = &restartValue };

            SmProp** list = stackalloc SmProp*[COUNT];
            for (int index = 0; index < COUNT; index++)
            {
                list[index] = &properties[index];
            }

            SmcSetProperties(_connection, COUNT, list);
        }
        finally
        {
            foreach (var pointer in allocations)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    private void SaveYourself(nint connection, bool shutdown, int interactStyle)
    {
        // Only a logout that lets clients talk to the user is put to the application; any other save is
        // answered at once, since the application keeps no session state to save.
        if (!shutdown || interactStyle != INTERACT_STYLE_ANY || !Application.IsRunning || _awaitingDecision)
        {
            SmcSaveYourselfDone(connection, 1);
            return;
        }

        _awaitingDecision = true;
        var interact = (nint)(delegate* unmanaged<nint, nint, void>)&OnInteract;
        if (SmcInteractRequest(connection, DIALOG_NORMAL, interact, 0) == 0)
        {
            _awaitingDecision = false;
            SmcSaveYourselfDone(connection, 1);
        }
    }

    private void Interact(nint connection)
    {
        if (!_awaitingDecision)
        {
            return;
        }

        if (!Application.IsRunning)
        {
            Answer(connection, granted: true);
            return;
        }

        Application.Current.RequestShutdownFromSystem(granted => Answer(connection, granted));
    }

    private void Answer(nint connection, bool granted)
    {
        if (!_awaitingDecision || _connection != connection)
        {
            return;
        }

        _awaitingDecision = false;
        SmcInteractDone(connection, granted ? 0 : 1);
        SmcSaveYourselfDone(connection, 1);
    }

    private void ShutdownCancelled(nint connection)
    {
        // The logout ended elsewhere while the application was deciding; the save it asked for is finished.
        if (_awaitingDecision)
        {
            _awaitingDecision = false;
            SmcSaveYourselfDone(connection, 1);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnSaveYourself(nint connection, nint clientData, int saveType, int shutdown, int interactStyle, int fast)
    {
        try
        {
            _active?.SaveYourself(connection, shutdown != 0, interactStyle);
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnInteract(nint connection, nint clientData)
    {
        try
        {
            _active?.Interact(connection);
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnDie(nint connection, nint clientData)
    {
        try
        {
            if (Application.IsRunning)
            {
                Application.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnSaveComplete(nint connection, nint clientData)
    {
    }

    [UnmanagedCallersOnly]
    private static void OnIceIOError(nint iceConnection)
    {
    }

    [UnmanagedCallersOnly]
    private static void OnProtocolError(nint connection, int swap, int offendingMinorOpcode, nuint offendingSequence,
        int errorClass, int severity, nint values)
    {
    }

    [UnmanagedCallersOnly]
    private static void OnShutdownCancelled(nint connection, nint clientData)
    {
        try
        {
            _active?.ShutdownCancelled(connection);
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmcCallbacks
    {
        public nint SaveYourself;
        public nint SaveYourselfData;
        public nint Die;
        public nint DieData;
        public nint SaveComplete;
        public nint SaveCompleteData;
        public nint ShutdownCancelled;
        public nint ShutdownCancelledData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmPropValue
    {
        public int Length;
        public nint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmProp
    {
        public nint Name;
        public nint Type;
        public int ValueCount;
        public SmPropValue* Values;
    }

    [LibraryImport(SM_LIBRARY)]
    private static partial nint SmcOpenConnection(nint networkIdsList, nint context, int xsmpMajorRevision, int xsmpMinorRevision,
        nuint mask, SmcCallbacks* callbacks, nint previousId, nint* clientIdReturn, int errorLength, byte* errorStringReturn);

    [LibraryImport(SM_LIBRARY)]
    private static partial int SmcCloseConnection(nint connection, int count, nint reasonMessages);

    [LibraryImport(SM_LIBRARY)]
    private static partial void SmcSaveYourselfDone(nint connection, int success);

    [LibraryImport(SM_LIBRARY)]
    private static partial int SmcInteractRequest(nint connection, int dialogType, nint interactProc, nint clientData);

    [LibraryImport(SM_LIBRARY)]
    private static partial void SmcInteractDone(nint connection, int cancelShutdown);

    [LibraryImport(SM_LIBRARY)]
    private static partial void SmcSetProperties(nint connection, int count, SmProp** properties);

    [LibraryImport(SM_LIBRARY)]
    private static partial nint SmcGetIceConnection(nint connection);

    [LibraryImport(ICE_LIBRARY)]
    private static partial int IceConnectionNumber(nint connection);

    [LibraryImport(ICE_LIBRARY)]
    private static partial nint IceSetIOErrorHandler(nint handler);

    [LibraryImport(ICE_LIBRARY)]
    private static partial nint IceSetErrorHandler(nint handler);

    [LibraryImport(SM_LIBRARY)]
    private static partial nint SmcSetErrorHandler(nint handler);

    [LibraryImport(ICE_LIBRARY)]
    private static partial int IceProcessMessages(nint connection, nint replyWait, nint replyReadyReturn);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(nint pointer);
}
