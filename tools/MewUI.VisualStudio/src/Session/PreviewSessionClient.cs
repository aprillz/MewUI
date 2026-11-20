// Preview session lifecycle, ported from tools/vscode-mewui/src/session.ts: listen on loopback,
// spawn the reload driver process with the preview environment, handshake (Hello/SessionStarted),
// then surface targets/frames/status through events. Free of VS API so it can be exercised from
// a plain console driver.
//
// Reload drivers (plan.md 4.2/4.6):
// - "watch" (default): dotnet watch owns change detection, incremental build, and hot reload.
// - "buildRestart" (fallback): the IDE detects saves and restarts `dotnet run`. Used when watch
//   is unavailable or misbehaving; "auto" switches to it when watch dies before connecting.
// If SessionStarted never arrives (user Main blocks or exits before Run), the session restarts
// with a generated shim project that skips the app entry point (low fidelity: plan.md 4.5).
//
// Process lifecycle transitions (spawn, restart, exit handling, shim fallback) run one at a time
// on pool threads; Stop only flags the session and starts a non-blocking kill, so no caller on
// the UI thread ever waits for a process.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Aprillz.MewUI.VisualStudio.Session
{
    internal enum SessionState
    {
        Starting,
        Connected,
        Disconnected,
        Stopped,
        Failed,
    }

    internal enum ReloadDriver
    {
        Watch,
        BuildRestart,
    }

    internal sealed class SessionOptions
    {
        /// <summary>Null (auto, default) starts with watch and falls back to buildRestart if watch dies early.</summary>
        public ReloadDriver? Driver { get; set; }

        /// <summary>Milliseconds to wait for SessionStarted before the shim fallback; 0 disables. Default 60000.</summary>
        public int SessionStartTimeoutMs { get; set; } = 60000;
    }

    internal sealed class PreviewSessionClient : IDisposable
    {
        private const int REBUILD_DEBOUNCE_MS = 500;
        private const int KILL_WAIT_MS = 5000;

        // Guards lifecycle state; held only briefly, never across a process wait or a socket write.
        private readonly object _gate = new object();
        // Guards the published socket and serializes writes to it.
        private readonly object _connectionGate = new object();
        // Serializes kill/spawn sequences; only pool threads wait on it.
        private readonly SemaphoreSlim _transitionGate = new SemaphoreSlim(1, 1);
        private readonly string _token;
        private readonly string _sessionId = Guid.NewGuid().ToString();
        private readonly bool _autoDriver;
        private readonly int _sessionStartTimeoutMs;
        // Written only inside transitions, which run one at a time.
        private ReloadDriver _driver;
        private string _effectiveProjectPath;
        private bool _usingShim;
        private bool _skipRestore = true;
        private volatile bool _everConnected;
        private volatile bool _stopped;
        private TcpListener _listener;
        private TcpClient _client;
        private Process _process;
        private Timer _startTimer;
        private int _startDeadline;
        private Timer _rebuildTimer;
        // Client state replayed on every SessionStarted, so a restarted app adopts it again.
        private double _viewportWidth;
        private double _viewportHeight;
        private double _viewportDpi;
        private string _themeMode;
        private string _lastTargetId;
        private bool _restoreTargetPending;

        public event Action<string> Log;
        public event Action<SessionState, string> StateChanged;
        public event Action<PreviewTargetInfo[], string> TargetsChanged;
        public event Action<FrameHeader, byte[]> FrameReceived;
        public event Action<StatusInfo> StatusChanged;

        public PreviewSessionClient(string projectPath, SessionOptions options)
        {
            ProjectPath = projectPath;
            _effectiveProjectPath = projectPath;
            _autoDriver = options.Driver == null;
            _driver = options.Driver ?? ReloadDriver.Watch;
            _sessionStartTimeoutMs = options.SessionStartTimeoutMs;

            byte[] tokenBytes = new byte[24];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(tokenBytes);
            }
            _token = Convert.ToBase64String(tokenBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public string ProjectPath { get; }

        public ReloadDriver ActiveDriver => _driver;

        public bool IsShimSession => _usingShim;

        public bool IsStopped => _stopped;

        /// <summary>Opens the listener and queues the first spawn; returns without waiting for the process.</summary>
        public void Start()
        {
            TcpListener listener;
            lock (_gate)
            {
                if (_stopped || _listener != null)
                {
                    return;
                }
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                _listener = listener;
            }
            _ = AcceptLoopAsync(listener);
            ArmStartTimeout();
            StateChanged?.Invoke(SessionState.Starting, null);
            QueueTransition(SpawnProcess);
        }

        /// <summary>Ends the session and kills its process tree without waiting for it to exit.</summary>
        public void Stop()
        {
            Process process;
            TcpListener listener;
            lock (_gate)
            {
                if (_stopped)
                {
                    return;
                }
                _stopped = true;
                process = _process;
                _process = null;
                listener = _listener;
                DisposeTimersLocked();
            }
            TcpClient client;
            lock (_connectionGate)
            {
                client = _client;
                _client = null;
            }
            KillProcessTree(process, waitForExit: false);
            client?.Close();
            listener?.Stop();
            StateChanged?.Invoke(SessionState.Stopped, null);
        }

        public void Dispose() => Stop();

        /// <summary>Restarts the driver process (full state reset); the listener keeps waiting for the reconnect.</summary>
        public void RestartProcess(string detail = "restarting")
        {
            if (_stopped)
            {
                return;
            }
            StateChanged?.Invoke(SessionState.Starting, detail);
            QueueTransition(() =>
            {
                KillCurrentProcess();
                SpawnProcess();
            });
        }

        /// <summary>
        /// Feeds an IDE save event to the buildRestart driver (debounced restart). A no-op under the
        /// watch driver, so callers can forward every save unconditionally.
        /// </summary>
        public void NotifySourceChanged(string fsPath)
        {
            if (_stopped || _driver != ReloadDriver.BuildRestart
                || !fsPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            lock (_gate)
            {
                if (_stopped)
                {
                    return;
                }
                if (_rebuildTimer == null)
                {
                    _rebuildTimer = new Timer(
                        _ => RestartProcess("rebuilding (buildRestart driver)"), null, Timeout.Infinite, Timeout.Infinite);
                }
                _rebuildTimer.Change(REBUILD_DEBOUNCE_MS, Timeout.Infinite);
            }
        }

        public void SelectTarget(string id)
        {
            lock (_gate)
            {
                _lastTargetId = id;
                _restoreTargetPending = false;
            }
            Send(PreviewProtocol.SELECT_TARGET, new { id });
        }

        public void RefreshTarget() => Send(PreviewProtocol.REFRESH_TARGET, new { });

        public void AckFrame(long seq) => Send(PreviewProtocol.FRAME_ACK, new { seq });

        /// <summary>Sends the viewport now and keeps it for every later (re)connect.</summary>
        public void SetViewport(double width, double height, double dpi)
        {
            lock (_gate)
            {
                _viewportWidth = width;
                _viewportHeight = height;
                _viewportDpi = dpi;
            }
            Send(PreviewProtocol.VIEWPORT_CHANGED, new { width, height, dpi });
        }

        /// <summary>Sends the theme now and keeps it for every later (re)connect.</summary>
        public void SetTheme(string mode)
        {
            lock (_gate)
            {
                _themeMode = mode;
            }
            Send(PreviewProtocol.SET_THEME, new { mode });
        }

        public void SendInput(int typeId, object body)
        {
            if (typeId >= PreviewProtocol.POINTER_MOVED && typeId <= PreviewProtocol.TEXT_INPUT)
            {
                Send(typeId, body);
            }
        }

        private void Send(int typeId, object body)
        {
            byte[] message = PreviewProtocol.Encode(typeId, body);
            lock (_connectionGate)
            {
                if (_client == null || !_client.Connected)
                {
                    return;
                }
                try
                {
                    _client.GetStream().Write(message, 0, message.Length);
                }
                catch (Exception)
                {
                    // The read loop notices the broken socket and reports the disconnect.
                }
            }
        }

        private async Task AcceptLoopAsync(TcpListener listener)
        {
            try
            {
                while (!_stopped)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    OnConnection(client);
                }
            }
            catch (Exception)
            {
                // Listener stopped (session shutdown) or accept failed; the session surface is
                // already reporting state through the exit/timeout paths.
            }
        }

        private void OnConnection(TcpClient client)
        {
            client.NoDelay = true;

            byte[] hello = PreviewProtocol.Encode(PreviewProtocol.HELLO, new
            {
                protocolMajor = PreviewProtocol.PROTOCOL_MAJOR,
                protocolMinor = PreviewProtocol.PROTOCOL_MINOR,
                token = _token,
                capabilities = Array.Empty<string>(),
            });
            try
            {
                client.GetStream().Write(hello, 0, hello.Length);
            }
            catch (Exception)
            {
                client.Close();
                return;
            }

            // Published only after Hello: the server drops a handshake whose first message is not Hello.
            TcpClient previous;
            bool stopped;
            lock (_connectionGate)
            {
                stopped = _stopped;
                if (stopped)
                {
                    previous = null;
                }
                else
                {
                    // A process restart reconnects with a fresh socket; the newest connection wins.
                    previous = _client;
                    _client = client;
                }
            }
            if (stopped)
            {
                client.Close();
                return;
            }
            previous?.Close();

            _ = ReadLoopAsync(client);
        }

        private async Task ReadLoopAsync(TcpClient client)
        {
            var decoder = new MessageDecoder(OnMessage);
            byte[] buffer = new byte[64 * 1024];
            try
            {
                NetworkStream stream = client.GetStream();
                while (true)
                {
                    int count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (count <= 0)
                    {
                        break;
                    }
                    decoder.Push(buffer, count);
                }
            }
            catch (InvalidDataException error)
            {
                Log?.Invoke($"protocol error: {error.Message}");
            }
            catch (Exception error) when (!(error is IOException) && !(error is ObjectDisposedException) && !(error is SocketException))
            {
                Log?.Invoke($"message handling failed: {error.Message}");
            }
            catch (Exception)
            {
                // Socket torn down by a restart or shutdown.
            }

            bool current;
            lock (_connectionGate)
            {
                current = _client == client;
                if (current)
                {
                    _client = null;
                }
            }
            client.Close();
            if (current && !_stopped)
            {
                StateChanged?.Invoke(SessionState.Disconnected, null);
            }
        }

        private void OnMessage(DecodedMessage message)
        {
            switch (message.TypeId)
            {
                case PreviewProtocol.SESSION_STARTED:
                    _everConnected = true;
                    ClearStartTimer();
                    ReplayClientState();
                    StateChanged?.Invoke(
                        SessionState.Connected,
                        _usingShim ? "shim session (low fidelity: app Main not executed)" : null);
                    break;
                case PreviewProtocol.SESSION_REJECTED:
                    StateChanged?.Invoke(SessionState.Failed, message.Json.ToString(Newtonsoft.Json.Formatting.None));
                    break;
                case PreviewProtocol.PREVIEW_TARGETS:
                    PreviewTargetInfo[] targets = message.Json["targets"].ToObject<PreviewTargetInfo[]>();
                    string activeId = (string)message.Json["activeId"];
                    string restoredId = TakeTargetToRestore(targets, activeId);
                    if (restoredId != null)
                    {
                        Send(PreviewProtocol.SELECT_TARGET, new { id = restoredId });
                        activeId = restoredId;
                    }
                    TargetsChanged?.Invoke(targets, activeId);
                    break;
                case PreviewProtocol.FRAME:
                    FrameReceived?.Invoke(message.Json.ToObject<FrameHeader>(), message.Binary);
                    break;
                case PreviewProtocol.STATUS:
                    StatusChanged?.Invoke(message.Json.ToObject<StatusInfo>());
                    break;
                default:
                    // Unknown message ids are ignored for forward compatibility.
                    break;
            }
        }

        private void ReplayClientState()
        {
            double width;
            double height;
            double dpi;
            string themeMode;
            lock (_gate)
            {
                width = _viewportWidth;
                height = _viewportHeight;
                dpi = _viewportDpi;
                themeMode = _themeMode;
                _restoreTargetPending = _lastTargetId != null;
            }
            if (width > 0 && height > 0 && dpi > 0)
            {
                Send(PreviewProtocol.CLIENT_INFO, new { dpi, viewportWidth = width, viewportHeight = height });
            }
            if (themeMode != null)
            {
                Send(PreviewProtocol.SET_THEME, new { mode = themeMode });
            }
        }

        private string TakeTargetToRestore(PreviewTargetInfo[] targets, string activeId)
        {
            lock (_gate)
            {
                bool pending = _restoreTargetPending;
                _restoreTargetPending = false;
                string previous = _lastTargetId;
                if (pending && previous != null && previous != activeId
                    && targets.Any(target => target.Id == previous && target.Available))
                {
                    return previous;
                }
                _lastTargetId = string.IsNullOrEmpty(activeId) ? null : activeId;
                return null;
            }
        }

        private void QueueTransition(Action transition)
        {
            _ = Task.Run(async () =>
            {
                await _transitionGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!_stopped)
                    {
                        transition();
                    }
                }
                catch (Exception error)
                {
                    Log?.Invoke($"session transition failed: {error.Message}");
                }
                finally
                {
                    _transitionGate.Release();
                }
            });
        }

        private void SpawnProcess()
        {
            int port;
            lock (_gate)
            {
                if (_stopped || _listener == null)
                {
                    return;
                }
                port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            }
            KillCurrentProcess();

            // Skipping the restore evaluation saves 2-3s per start; only safe once assets exist,
            // and a pre-connect failure retries with restore (e.g. after a PackageReference change).
            bool noRestore = _skipRestore
                && File.Exists(Path.Combine(Path.GetDirectoryName(_effectiveProjectPath), "obj", "project.assets.json"));

            // The app the session runs holds its own output directory open for as long as it lives, so it
            // gets one of its own: sharing bin would fail every build made while the preview is up. Under
            // obj, which keeps the intermediate outputs shared, so this costs a copy and not a compile.
            // Spelled --property because dotnet watch reads -p as the abbreviation of --project.
            // The trailing separator is required: a global property skips MSBuild's own slash fix-up.
            string outputPath = Path.Combine(
                Path.GetDirectoryName(_effectiveProjectPath), "obj", "mewui-preview") + Path.DirectorySeparatorChar;
            string output = $" --property:BaseOutputPath={CommandLine.Quote(outputPath)}";
            string project = CommandLine.Quote(_effectiveProjectPath);

            string arguments = _driver == ReloadDriver.Watch
                ? $"watch --non-interactive --project {project} run{(noRestore ? " --no-restore" : "")}{output}"
                : $"run --project {project}{(noRestore ? " --no-restore" : "")}{output}";

            var startInfo = new ProcessStartInfo("dotnet", arguments)
            {
                WorkingDirectory = Path.GetDirectoryName(_effectiveProjectPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.EnvironmentVariables["MEWUI_PREVIEW"] = "1";
            startInfo.EnvironmentVariables["MEWUI_PREVIEW_ENDPOINT"] = $"127.0.0.1:{port}";
            startInfo.EnvironmentVariables["MEWUI_PREVIEW_TOKEN"] = _token;
            startInfo.EnvironmentVariables["MEWUI_PREVIEW_SESSION"] = _sessionId;
            startInfo.EnvironmentVariables["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "1";

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => ForwardLog(args.Data);
            process.ErrorDataReceived += (_, args) => ForwardLog(args.Data);
            // Queued behind this transition, so the exit is seen only after the process is published.
            process.Exited += (_, __) => QueueTransition(() => OnProcessExited(process));

            try
            {
                process.Start();
            }
            catch (Exception error)
            {
                process.Dispose();
                StateChanged?.Invoke(SessionState.Failed, $"dotnet failed to start: {error.Message}");
                return;
            }
            if (!ChildProcessJob.TryAssign(process))
            {
                Log?.Invoke("could not assign the preview process to the kill-on-close job; it may outlive an IDE crash");
            }

            bool stopped;
            lock (_gate)
            {
                stopped = _stopped;
                if (!stopped)
                {
                    _process = process;
                }
            }
            if (stopped)
            {
                KillProcessTree(process, waitForExit: false);
                return;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private void OnProcessExited(Process process)
        {
            int code;
            try
            {
                code = process.ExitCode;
            }
            catch (Exception)
            {
                code = -1;
            }

            lock (_gate)
            {
                if (_process != process)
                {
                    return;
                }
                _process = null;
            }

            // A pre-connect failure with --no-restore may just be a stale restore (new package
            // reference); retry once with the restore enabled before any driver fallback.
            if (!_everConnected && code != 0 && _skipRestore)
            {
                _skipRestore = false;
                Log?.Invoke("build failed before connecting; retrying with restore enabled");
                SpawnProcess();
                return;
            }

            // "auto" treats an early watch death (before any handshake) as watch being unusable
            // in this environment and switches to the IDE-driven restart driver.
            if (_driver == ReloadDriver.Watch && _autoDriver && !_everConnected && code != 0)
            {
                _driver = ReloadDriver.BuildRestart;
                Log?.Invoke($"dotnet watch exited with code {code} before connecting; falling back to the buildRestart driver");
                StateChanged?.Invoke(SessionState.Starting, "buildRestart driver fallback");
                SpawnProcess();
                return;
            }

            StateChanged?.Invoke(
                SessionState.Failed,
                $"dotnet {(_driver == ReloadDriver.Watch ? "watch " : "")}exited with code {code}");
        }

        private void ArmStartTimeout()
        {
            lock (_gate)
            {
                if (_stopped || _sessionStartTimeoutMs <= 0)
                {
                    return;
                }
                if (_startTimer == null)
                {
                    _startTimer = new Timer(_ => QueueTransition(OnStartTimeout), null, Timeout.Infinite, Timeout.Infinite);
                }
                _startDeadline = unchecked(Environment.TickCount + _sessionStartTimeoutMs);
                _startTimer.Change(_sessionStartTimeoutMs, Timeout.Infinite);
            }
        }

        private void OnStartTimeout()
        {
            lock (_gate)
            {
                // A callback queued just before a re-arm is stale; the re-armed timer fires again.
                if (_everConnected || _startTimer == null || unchecked(Environment.TickCount - _startDeadline) < 0)
                {
                    return;
                }
            }

            if (_usingShim)
            {
                StateChanged?.Invoke(SessionState.Failed, "session start timed out (shim fallback also failed)");
                KillCurrentProcess();
                return;
            }

            // The app's Main likely never reached Application.Run (plan.md 4.5): restart with a shim
            // project that skips the entry point entirely.
            Log?.Invoke($"no SessionStarted within {_sessionStartTimeoutMs}ms; retrying with the shim fallback session");
            string shimPath;
            try
            {
                shimPath = ShimProject.Generate(ProjectPath);
            }
            catch (Exception error)
            {
                StateChanged?.Invoke(SessionState.Failed, $"session start timed out and shim generation failed: {error.Message}");
                return;
            }
            if (_stopped)
            {
                return;
            }
            _effectiveProjectPath = shimPath;
            _usingShim = true;
            StateChanged?.Invoke(SessionState.Starting, "shim fallback (low fidelity: app Main not executed)");
            KillCurrentProcess();
            SpawnProcess();
            ArmStartTimeout();
        }

        private void ForwardLog(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }
            // Process output counts as startup progress: a slow cold build must not trigger the
            // shim fallback, which exists for a Main that never reaches Application.Run. The
            // timeout measures silence, so it only fires after the output has gone quiet.
            lock (_gate)
            {
                if (!_everConnected && _startTimer != null)
                {
                    _startDeadline = unchecked(Environment.TickCount + _sessionStartTimeoutMs);
                    _startTimer.Change(_sessionStartTimeoutMs, Timeout.Infinite);
                }
            }
            Log?.Invoke(line);
        }

        private void ClearStartTimer()
        {
            lock (_gate)
            {
                _startTimer?.Dispose();
                _startTimer = null;
            }
        }

        private void DisposeTimersLocked()
        {
            _startTimer?.Dispose();
            _startTimer = null;
            _rebuildTimer?.Dispose();
            _rebuildTimer = null;
        }

        private void KillCurrentProcess()
        {
            Process process;
            lock (_gate)
            {
                process = _process;
                _process = null;
            }
            KillProcessTree(process, waitForExit: true);
        }

        private static void KillProcessTree(Process process, bool waitForExit)
        {
            if (process == null)
            {
                return;
            }
            try
            {
                // A dead root has no tree left for taskkill to walk, and its PID may already be reused.
                if (process.HasExited)
                {
                    return;
                }
                // Kill the whole tree: watch's child app process must not outlive the session.
                // An orphaned app keeps reconnecting to this session's port and fights the
                // replacement for the connection.
                using (var killer = Process.Start(new ProcessStartInfo("taskkill", $"/T /F /PID {process.Id}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }))
                {
                    if (waitForExit)
                    {
                        killer?.WaitForExit(KILL_WAIT_MS);
                    }
                }
            }
            catch (Exception)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                    // Already exited.
                }
            }
        }
    }

    internal static class CommandLine
    {
        /// <summary>Quotes one argument so the Windows argument parser returns it unchanged.</summary>
        internal static string Quote(string argument)
        {
            var builder = new StringBuilder(argument.Length + 2);
            builder.Append('"');
            int backslashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                }
                else
                {
                    builder.Append('\\', backslashes);
                }
                builder.Append(character);
                backslashes = 0;
            }
            // Backslashes before the closing quote are doubled, or the last one escapes the quote.
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }

    /// <summary>
    /// Process-wide job that kills every assigned process tree when the IDE process ends, crashes
    /// included, so dotnet watch and the previewed app never outlive it.
    /// </summary>
    internal static class ChildProcessJob
    {
        private const int JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        private static readonly object _gate = new object();
        // Never closed on purpose: the OS closes it when the IDE exits, which is what kills the trees.
        private static IntPtr _handle;
        private static bool _created;

        /// <summary>Adds a started process (and the children it spawns afterwards) to the job; false when that fails.</summary>
        internal static bool TryAssign(Process process)
        {
            IntPtr job = GetOrCreate();
            if (job == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                return AssignProcessToJobObject(job, process.Handle);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IntPtr GetOrCreate()
        {
            lock (_gate)
            {
                if (!_created)
                {
                    _created = true;
                    _handle = Create();
                }
                return _handle;
            }
        }

        private static IntPtr Create()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if (!SetInformationJobObject(
                    job,
                    JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS,
                    ref limits,
                    (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
            return job;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint infoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
