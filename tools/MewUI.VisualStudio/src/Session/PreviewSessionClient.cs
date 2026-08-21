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

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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

        private readonly object _gate = new object();
        private readonly string _token;
        private readonly string _sessionId = Guid.NewGuid().ToString();
        private readonly bool _autoDriver;
        private readonly int _sessionStartTimeoutMs;
        private ReloadDriver _driver;
        private string _effectiveProjectPath;
        private bool _usingShim;
        private bool _everConnected;
        private bool _skipRestore = true;
        private TcpListener _listener;
        private TcpClient _client;
        private Process _process;
        private Timer _startTimer;
        private Timer _rebuildTimer;
        private bool _stopped;

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

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            AcceptLoop();
            SpawnProcess();
            ArmStartTimeout();
            StateChanged?.Invoke(SessionState.Starting, null);
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_stopped)
                {
                    return;
                }
                _stopped = true;
            }
            ClearTimers();
            KillProcess();
            _client?.Close();
            _listener?.Stop();
            StateChanged?.Invoke(SessionState.Stopped, null);
        }

        public void Dispose() => Stop();

        /// <summary>Restarts the driver process (full state reset); the listener keeps waiting for the reconnect.</summary>
        public void RestartProcess(string detail = "restarting")
        {
            if (_stopped || _listener == null)
            {
                return;
            }
            KillProcess();
            SpawnProcess();
            StateChanged?.Invoke(SessionState.Starting, detail);
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
                _rebuildTimer?.Dispose();
                _rebuildTimer = new Timer(_ =>
                {
                    lock (_gate)
                    {
                        _rebuildTimer?.Dispose();
                        _rebuildTimer = null;
                    }
                    RestartProcess("rebuilding (buildRestart driver)");
                }, null, REBUILD_DEBOUNCE_MS, Timeout.Infinite);
            }
        }

        public void SelectTarget(string id) => Send(PreviewProtocol.SELECT_TARGET, new { id });

        public void RefreshTarget() => Send(PreviewProtocol.REFRESH_TARGET, new { });

        public void AckFrame(long seq) => Send(PreviewProtocol.FRAME_ACK, new { seq });

        public void SetViewport(double width, double height, double dpi) =>
            Send(PreviewProtocol.VIEWPORT_CHANGED, new { width, height, dpi });

        public void SetTheme(string mode) => Send(PreviewProtocol.SET_THEME, new { mode });

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
            lock (_gate)
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

        private async void AcceptLoop()
        {
            var listener = _listener;
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
            lock (_gate)
            {
                // A process restart reconnects with a fresh socket; the newest connection wins.
                _client?.Close();
                _client = client;
            }
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
                return;
            }

            ReadLoop(client);
        }

        private async void ReadLoop(TcpClient client)
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
            catch (Exception)
            {
                // Socket torn down by a restart or shutdown.
            }

            lock (_gate)
            {
                if (_client != client)
                {
                    return;
                }
                _client = null;
            }
            client.Close();
            if (!_stopped)
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
                    StateChanged?.Invoke(
                        SessionState.Connected,
                        _usingShim ? "shim session (low fidelity: app Main not executed)" : null);
                    break;
                case PreviewProtocol.SESSION_REJECTED:
                    StateChanged?.Invoke(SessionState.Failed, message.Json.ToString(Newtonsoft.Json.Formatting.None));
                    break;
                case PreviewProtocol.PREVIEW_TARGETS:
                    TargetsChanged?.Invoke(
                        message.Json["targets"].ToObject<PreviewTargetInfo[]>(),
                        (string)message.Json["activeId"]);
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

        private void SpawnProcess()
        {
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // Skipping the restore evaluation saves 2-3s per start; only safe once assets exist,
            // and a pre-connect failure retries with restore (e.g. after a PackageReference change).
            bool noRestore = _skipRestore
                && File.Exists(Path.Combine(Path.GetDirectoryName(_effectiveProjectPath), "obj", "project.assets.json"));

            // The app the session runs holds its own output directory open for as long as it lives, so it
            // gets one of its own: sharing bin would fail every build made while the preview is up. Under
            // obj, which keeps the intermediate outputs shared, so this costs a copy and not a compile.
            // Spelled --property because dotnet watch reads -p as the abbreviation of --project.
            string outputPath = Path.Combine(
                Path.GetDirectoryName(_effectiveProjectPath), "obj", "mewui-preview") + Path.DirectorySeparatorChar;
            string output = $" --property:BaseOutputPath=\"{outputPath}\"";

            string arguments = _driver == ReloadDriver.Watch
                ? $"watch --non-interactive --project \"{_effectiveProjectPath}\" run{(noRestore ? " --no-restore" : "")}{output}"
                : $"run --project \"{_effectiveProjectPath}\"{(noRestore ? " --no-restore" : "")}{output}";

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
            process.Exited += (_, __) => OnProcessExited(process);

            lock (_gate)
            {
                _process = process;
            }
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception error)
            {
                StateChanged?.Invoke(SessionState.Failed, $"dotnet failed to start: {error.Message}");
            }
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
            if (_stopped)
            {
                return;
            }

            // A pre-connect failure with --no-restore may just be a stale restore (new package
            // reference); retry once with the restore enabled before any driver fallback.
            if (!_everConnected && code != 0 && _skipRestore && _listener != null)
            {
                _skipRestore = false;
                Log?.Invoke("build failed before connecting; retrying with restore enabled");
                SpawnProcess();
                return;
            }

            // "auto" treats an early watch death (before any handshake) as watch being unusable
            // in this environment and switches to the IDE-driven restart driver.
            if (_driver == ReloadDriver.Watch && _autoDriver && !_everConnected && code != 0 && _listener != null)
            {
                _driver = ReloadDriver.BuildRestart;
                Log?.Invoke($"dotnet watch exited with code {code} before connecting; falling back to the buildRestart driver");
                SpawnProcess();
                StateChanged?.Invoke(SessionState.Starting, "buildRestart driver fallback");
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
                _startTimer?.Dispose();
                _startTimer = null;
                if (_sessionStartTimeoutMs <= 0)
                {
                    return;
                }
                _startTimer = new Timer(_ => OnStartTimeout(), null, _sessionStartTimeoutMs, Timeout.Infinite);
            }
        }

        private void OnStartTimeout()
        {
            if (_everConnected || _stopped)
            {
                return;
            }

            if (_usingShim)
            {
                StateChanged?.Invoke(SessionState.Failed, "session start timed out (shim fallback also failed)");
                KillProcess();
                return;
            }

            // The app's Main likely never reached Application.Run (plan.md 4.5): restart with a shim
            // project that skips the entry point entirely.
            Log?.Invoke($"no SessionStarted within {_sessionStartTimeoutMs}ms; retrying with the shim fallback session");
            try
            {
                _effectiveProjectPath = ShimProject.Generate(ProjectPath);
            }
            catch (Exception error)
            {
                StateChanged?.Invoke(SessionState.Failed, $"session start timed out and shim generation failed: {error.Message}");
                return;
            }
            _usingShim = true;
            RestartProcess("shim fallback (low fidelity: app Main not executed)");
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
            bool rearm;
            lock (_gate)
            {
                rearm = !_everConnected && _startTimer != null;
            }
            if (rearm)
            {
                ArmStartTimeout();
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

        private void ClearTimers()
        {
            lock (_gate)
            {
                _startTimer?.Dispose();
                _startTimer = null;
                _rebuildTimer?.Dispose();
                _rebuildTimer = null;
            }
        }

        private void KillProcess()
        {
            Process process;
            lock (_gate)
            {
                process = _process;
                _process = null;
            }
            if (process == null)
            {
                return;
            }

            try
            {
                // Kill the whole tree: watch's child app process must not outlive the session.
                // An orphaned app keeps reconnecting to this session's port and fights the
                // replacement for the connection.
                using (var killer = Process.Start(new ProcessStartInfo("taskkill", $"/T /F /PID {process.Id}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }))
                {
                    killer?.WaitForExit(5000);
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
}
