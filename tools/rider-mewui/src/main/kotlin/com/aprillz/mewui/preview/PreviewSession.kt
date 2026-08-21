// Preview session lifecycle, ported from tools/vscode-mewui/src/session.ts: listen on loopback,
// spawn the reload driver process with the preview environment, handshake (Hello/SessionStarted),
// then surface targets/frames/status through callbacks. Free of IntelliJ API so a plain JVM
// driver can exercise it end to end.
//
// Reload drivers (plan.md 4.2/4.6):
// - "watch" (default): dotnet watch owns change detection, incremental build, and hot reload.
// - "buildRestart" (fallback): the IDE detects saves and restarts `dotnet run`. Used when watch
//   is unavailable or misbehaving; "auto" switches to it when watch dies before connecting.
// If SessionStarted never arrives (user Main blocks or exits before Run), the session restarts
// with a generated shim project that skips the app entry point (low fidelity: plan.md 4.5).
package com.aprillz.mewui.preview

import java.io.File
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.security.SecureRandom
import java.util.Base64
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread

enum class SessionState { STARTING, CONNECTED, DISCONNECTED, STOPPED, FAILED }

enum class ReloadDriver { WATCH, BUILD_RESTART }

interface SessionCallbacks {
    fun onLog(line: String)
    fun onState(state: SessionState, detail: String?)
    fun onTargets(targets: List<PreviewTargetInfo>, activeId: String)
    fun onFrame(header: FrameHeader, pixels: ByteArray)
    fun onStatus(status: StatusInfo)
}

class SessionOptions(
    /** Null (auto, default) starts with watch and falls back to buildRestart if watch dies early. */
    val driver: ReloadDriver? = null,
    /** Milliseconds to wait for SessionStarted before the shim fallback; 0 disables. Default 60000. */
    val sessionStartTimeoutMs: Long = 60_000,
)

private const val REBUILD_DEBOUNCE_MS = 500L

class PreviewSession(
    val projectPath: String,
    private val callbacks: SessionCallbacks,
    options: SessionOptions = SessionOptions(),
) {
    private val token: String = run {
        val bytes = ByteArray(24)
        SecureRandom().nextBytes(bytes)
        Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)
    }
    private val sessionId = UUID.randomUUID().toString()
    private val autoDriver = options.driver == null
    private val sessionStartTimeoutMs = options.sessionStartTimeoutMs
    private val scheduler = Executors.newSingleThreadScheduledExecutor { runnable ->
        Thread(runnable, "mewui-preview-timer").apply { isDaemon = true }
    }
    private val lock = Any()

    @Volatile private var driver = options.driver ?: ReloadDriver.WATCH
    @Volatile private var effectiveProjectPath = projectPath
    @Volatile private var usingShim = false
    @Volatile private var everConnected = false
    @Volatile private var skipRestore = true
    @Volatile private var stopped = false
    private var listener: ServerSocket? = null
    private var socket: Socket? = null
    private var process: Process? = null
    private var startTimer: ScheduledFuture<*>? = null
    private var rebuildTimer: ScheduledFuture<*>? = null

    val activeDriver: ReloadDriver get() = driver
    val isShimSession: Boolean get() = usingShim
    val isStopped: Boolean get() = stopped

    fun start() {
        val server = ServerSocket(0, 1, InetAddress.getLoopbackAddress())
        listener = server
        thread(name = "mewui-preview-accept", isDaemon = true) { acceptLoop(server) }
        spawnProcess()
        armStartTimeout()
        callbacks.onState(SessionState.STARTING, null)
    }

    fun stop() {
        synchronized(lock) {
            if (stopped) {
                return
            }
            stopped = true
        }
        clearTimers()
        scheduler.shutdown()
        killProcess()
        runCatching { socket?.close() }
        runCatching { listener?.close() }
        callbacks.onState(SessionState.STOPPED, null)
    }

    /** Restarts the driver process (full state reset); the listener keeps waiting for the reconnect. */
    fun restartProcess(detail: String = "restarting") {
        if (stopped || listener == null) {
            return
        }
        killProcess()
        spawnProcess()
        callbacks.onState(SessionState.STARTING, detail)
    }

    /**
     * Feeds an IDE save event to the buildRestart driver (debounced restart). A no-op under the
     * watch driver, so callers can forward every save unconditionally.
     */
    fun notifySourceChanged(fsPath: String) {
        if (stopped || driver != ReloadDriver.BUILD_RESTART || !fsPath.endsWith(".cs", ignoreCase = true)) {
            return
        }
        synchronized(lock) {
            rebuildTimer?.cancel(false)
            rebuildTimer = scheduler.schedule(
                { restartProcess("rebuilding (buildRestart driver)") },
                REBUILD_DEBOUNCE_MS,
                TimeUnit.MILLISECONDS,
            )
        }
    }

    fun selectTarget(id: String) = send(PreviewProtocol.SELECT_TARGET, mapOf("id" to id))

    fun refreshTarget() = send(PreviewProtocol.REFRESH_TARGET, emptyMap<String, Any>())

    fun ackFrame(seq: Long) = send(PreviewProtocol.FRAME_ACK, mapOf("seq" to seq))

    fun setViewport(width: Double, height: Double, dpi: Double) =
        send(PreviewProtocol.VIEWPORT_CHANGED, mapOf("width" to width, "height" to height, "dpi" to dpi))

    fun setTheme(mode: String) = send(PreviewProtocol.SET_THEME, mapOf("mode" to mode))

    fun sendInput(typeId: Int, body: Map<String, Any>) {
        if (typeId in PreviewProtocol.POINTER_MOVED..PreviewProtocol.TEXT_INPUT) {
            send(typeId, body)
        }
    }

    private fun send(typeId: Int, body: Any) {
        val message = PreviewProtocol.encode(typeId, body)
        synchronized(lock) {
            val current = socket ?: return
            runCatching { current.getOutputStream().write(message) }
            // A broken socket surfaces through the read loop's disconnect.
        }
    }

    private fun acceptLoop(server: ServerSocket) {
        try {
            while (!stopped) {
                onConnection(server.accept())
            }
        } catch (_: Exception) {
            // Listener closed (session shutdown); state is reported by the exit/timeout paths.
        }
    }

    private fun onConnection(client: Socket) {
        synchronized(lock) {
            // A process restart reconnects with a fresh socket; the newest connection wins.
            runCatching { socket?.close() }
            socket = client
        }
        client.tcpNoDelay = true

        val hello = PreviewProtocol.encode(
            PreviewProtocol.HELLO,
            mapOf(
                "protocolMajor" to PreviewProtocol.PROTOCOL_MAJOR,
                "protocolMinor" to PreviewProtocol.PROTOCOL_MINOR,
                "token" to token,
                "capabilities" to emptyList<String>(),
            ),
        )
        try {
            client.getOutputStream().write(hello)
        } catch (_: Exception) {
            return
        }

        thread(name = "mewui-preview-read", isDaemon = true) { readLoop(client) }
    }

    private fun readLoop(client: Socket) {
        val decoder = MessageDecoder(::onMessage)
        val buffer = ByteArray(64 * 1024)
        try {
            val stream = client.getInputStream()
            while (true) {
                val count = stream.read(buffer)
                if (count <= 0) {
                    break
                }
                decoder.push(buffer, count)
            }
        } catch (error: IllegalArgumentException) {
            callbacks.onLog("protocol error: ${error.message}")
        } catch (_: Exception) {
            // Socket torn down by a restart or shutdown.
        }

        synchronized(lock) {
            if (socket != client) {
                return
            }
            socket = null
        }
        runCatching { client.close() }
        if (!stopped) {
            callbacks.onState(SessionState.DISCONNECTED, null)
        }
    }

    private fun onMessage(message: DecodedMessage) {
        when (message.typeId) {
            PreviewProtocol.SESSION_STARTED -> {
                everConnected = true
                clearStartTimer()
                callbacks.onState(
                    SessionState.CONNECTED,
                    if (usingShim) "shim session (low fidelity: app Main not executed)" else null,
                )
            }
            PreviewProtocol.SESSION_REJECTED ->
                callbacks.onState(SessionState.FAILED, message.json.toString())
            PreviewProtocol.PREVIEW_TARGETS -> {
                val targets = message.json.getAsJsonArray("targets").map {
                    PreviewProtocol.gson.fromJson(it, PreviewTargetInfo::class.java)
                }
                callbacks.onTargets(targets, message.json.get("activeId").asString)
            }
            PreviewProtocol.FRAME ->
                callbacks.onFrame(PreviewProtocol.gson.fromJson(message.json, FrameHeader::class.java), message.binary)
            PreviewProtocol.STATUS ->
                callbacks.onStatus(PreviewProtocol.gson.fromJson(message.json, StatusInfo::class.java))
            // Unknown message ids are ignored for forward compatibility.
        }
    }

    private fun spawnProcess() {
        val port = listener?.localPort ?: return
        val projectDirectory = File(effectiveProjectPath).parentFile

        // Skipping the restore evaluation saves 2-3s per start; only safe once assets exist,
        // and a pre-connect failure retries with restore (e.g. after a PackageReference change).
        val noRestore = skipRestore && File(projectDirectory, "obj/project.assets.json").exists()
        val arguments = buildList {
            add("dotnet")
            if (driver == ReloadDriver.WATCH) {
                addAll(listOf("watch", "--non-interactive", "--project", effectiveProjectPath, "run"))
            } else {
                addAll(listOf("run", "--project", effectiveProjectPath))
            }
            if (noRestore) {
                add("--no-restore")
            }

            // The app the session runs holds its own output directory open for as long as it lives, so it
            // gets one of its own: sharing bin would fail every build made while the preview is up. Under
            // obj, which keeps the intermediate outputs shared, so this costs a copy and not a compile.
            // Spelled --property because dotnet watch reads -p as the abbreviation of --project.
            add("--property:BaseOutputPath=" + File(projectDirectory, "obj/mewui-preview").path + File.separator)
        }

        val builder = ProcessBuilder(arguments)
            .directory(projectDirectory)
            .redirectErrorStream(true)
        builder.environment().apply {
            put("MEWUI_PREVIEW", "1")
            put("MEWUI_PREVIEW_ENDPOINT", "127.0.0.1:$port")
            put("MEWUI_PREVIEW_TOKEN", token)
            put("MEWUI_PREVIEW_SESSION", sessionId)
            put("DOTNET_WATCH_RESTART_ON_RUDE_EDIT", "1")
        }

        val child: Process
        try {
            child = builder.start()
        } catch (error: Exception) {
            callbacks.onState(SessionState.FAILED, "dotnet failed to start: ${error.message}")
            return
        }
        synchronized(lock) {
            process = child
        }

        thread(name = "mewui-preview-log", isDaemon = true) {
            child.inputStream.bufferedReader().forEachLine(::forwardLog)
        }
        child.onExit().thenAccept { onProcessExited(child) }
    }

    private fun onProcessExited(child: Process) {
        val code = runCatching { child.exitValue() }.getOrDefault(-1)
        synchronized(lock) {
            if (process != child) {
                return
            }
            process = null
        }
        if (stopped) {
            return
        }

        // A pre-connect failure with --no-restore may just be a stale restore (new package
        // reference); retry once with the restore enabled before any driver fallback.
        if (!everConnected && code != 0 && skipRestore && listener != null) {
            skipRestore = false
            callbacks.onLog("build failed before connecting; retrying with restore enabled")
            spawnProcess()
            return
        }

        // "auto" treats an early watch death (before any handshake) as watch being unusable
        // in this environment and switches to the IDE-driven restart driver.
        if (driver == ReloadDriver.WATCH && autoDriver && !everConnected && code != 0 && listener != null) {
            driver = ReloadDriver.BUILD_RESTART
            callbacks.onLog("dotnet watch exited with code $code before connecting; falling back to the buildRestart driver")
            spawnProcess()
            callbacks.onState(SessionState.STARTING, "buildRestart driver fallback")
            return
        }

        val watchPrefix = if (driver == ReloadDriver.WATCH) "watch " else ""
        callbacks.onState(SessionState.FAILED, "dotnet ${watchPrefix}exited with code $code")
    }

    private fun armStartTimeout() {
        synchronized(lock) {
            startTimer?.cancel(false)
            startTimer = null
            if (sessionStartTimeoutMs <= 0 || scheduler.isShutdown) {
                return
            }
            startTimer = scheduler.schedule(::onStartTimeout, sessionStartTimeoutMs, TimeUnit.MILLISECONDS)
        }
    }

    private fun onStartTimeout() {
        if (everConnected || stopped) {
            return
        }

        if (usingShim) {
            callbacks.onState(SessionState.FAILED, "session start timed out (shim fallback also failed)")
            killProcess()
            return
        }

        // The app's Main likely never reached Application.Run (plan.md 4.5): restart with a shim
        // project that skips the entry point entirely.
        callbacks.onLog("no SessionStarted within ${sessionStartTimeoutMs}ms; retrying with the shim fallback session")
        try {
            effectiveProjectPath = ShimProject.generate(projectPath)
        } catch (error: Exception) {
            callbacks.onState(SessionState.FAILED, "session start timed out and shim generation failed: ${error.message}")
            return
        }
        usingShim = true
        restartProcess("shim fallback (low fidelity: app Main not executed)")
        armStartTimeout()
    }

    private fun forwardLog(line: String) {
        if (line.isEmpty()) {
            return
        }
        // Process output counts as startup progress: a slow cold build must not trigger the
        // shim fallback, which exists for a Main that never reaches Application.Run. The
        // timeout measures silence, so it only fires after the output has gone quiet.
        val rearm = synchronized(lock) { !everConnected && startTimer != null }
        if (rearm) {
            armStartTimeout()
        }
        callbacks.onLog(line)
    }

    private fun clearStartTimer() {
        synchronized(lock) {
            startTimer?.cancel(false)
            startTimer = null
        }
    }

    private fun clearTimers() {
        synchronized(lock) {
            startTimer?.cancel(false)
            startTimer = null
            rebuildTimer?.cancel(false)
            rebuildTimer = null
        }
    }

    private fun killProcess() {
        val child: Process?
        synchronized(lock) {
            child = process
            process = null
        }
        if (child == null) {
            return
        }
        // Kill the whole tree: watch's child app process must not outlive the session. An
        // orphaned app keeps reconnecting to this session's port and fights the replacement.
        child.toHandle().descendants().forEach { it.destroy() }
        child.destroy()
        if (!child.waitFor(3, TimeUnit.SECONDS)) {
            child.toHandle().descendants().forEach { it.destroyForcibly() }
            child.destroyForcibly()
        }
    }
}
