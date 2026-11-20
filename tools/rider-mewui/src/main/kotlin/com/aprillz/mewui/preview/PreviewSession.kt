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
//
// Threading: every process transition (spawn, restart, retry, fallback, shim) runs on the single
// lifecycle thread; socket writes go through a per-connection writer thread, so no caller thread
// (the EDT in particular) ever blocks on the process or the socket.
package com.aprillz.mewui.preview

import java.io.File
import java.io.IOException
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.security.SecureRandom
import java.util.Base64
import java.util.UUID
import java.util.concurrent.CompletableFuture
import java.util.concurrent.RejectedExecutionException
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.ScheduledThreadPoolExecutor
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.thread
import kotlin.concurrent.withLock

enum class SessionState { STARTING, CONNECTED, DISCONNECTED, STOPPED, FAILED }

enum class ReloadDriver { WATCH, BUILD_RESTART }

interface SessionCallbacks {
    fun onLog(line: String)

    /** One line of driver process output (dotnet watch/run and the app). */
    fun onOutput(line: String)
    fun onState(state: SessionState, detail: String?)
    fun onTargets(targets: List<PreviewTargetInfo>, activeId: String)

    /** Called on the socket reader thread, so the receiver can convert pixels off the UI thread. */
    fun onFrame(header: FrameHeader, pixels: ByteArray)
    fun onStatus(status: StatusInfo)
}

/** The dotnet host executable and the environment every dotnet process of the session runs with. */
class DotnetHost(val executable: String, val environment: Map<String, String>)

class SessionOptions(
    /** Null (auto, default) starts with watch and falls back to buildRestart if watch dies early. */
    val driver: ReloadDriver? = null,
    /** Milliseconds to wait for SessionStarted before the shim fallback; 0 disables. Default 60000. */
    val sessionStartTimeoutMs: Long = 60_000,
    /** Resolves the dotnet host once per session, on the lifecycle thread (may block). */
    val dotnetHost: () -> DotnetHost = { DotnetHost("dotnet", System.getenv()) },
)

private const val REBUILD_DEBOUNCE_MS = 500L
private const val KILL_GRACE_SECONDS = 3L
private const val POINTER_BACKLOG_LIMIT = 256

class PreviewSession(
    val projectPath: String,
    private val callbacks: SessionCallbacks,
    private val options: SessionOptions = SessionOptions(),
) {
    private val token: String = run {
        val bytes = ByteArray(24)
        SecureRandom().nextBytes(bytes)
        Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)
    }
    private val sessionId = UUID.randomUUID().toString()
    private val autoDriver = options.driver == null
    private val sessionStartTimeoutMs = options.sessionStartTimeoutMs
    private val lifecycle = ScheduledThreadPoolExecutor(1) { runnable ->
        Thread(runnable, "mewui-preview-lifecycle").apply { isDaemon = true }
    }.apply {
        executeExistingDelayedTasksAfterShutdownPolicy = false
        removeOnCancelPolicy = true
    }
    private val lock = Any()

    @Volatile private var driver = options.driver ?: ReloadDriver.WATCH
    @Volatile private var effectiveProjectPath = projectPath
    @Volatile private var usingShim = false
    @Volatile private var everConnected = false
    @Volatile private var skipRestore = true
    @Volatile private var stopped = false
    @Volatile private var connection: Connection? = null
    @Volatile private var viewport: ViewportMetrics? = null
    @Volatile private var requestedTargetId: String? = null
    @Volatile private var requestedTheme: String? = null
    private var resolvedHost: DotnetHost? = null
    private var listener: ServerSocket? = null
    private var process: Process? = null
    private var startTimer: ScheduledFuture<*>? = null
    private var rebuildTimer: ScheduledFuture<*>? = null

    val activeDriver: ReloadDriver get() = driver
    val isShimSession: Boolean get() = usingShim
    val isStopped: Boolean get() = stopped

    fun start() {
        val server = ServerSocket(0, 1, InetAddress.getLoopbackAddress())
        synchronized(lock) {
            listener = server
        }
        thread(name = "mewui-preview-accept", isDaemon = true) { acceptLoop(server) }
        callbacks.onState(SessionState.STARTING, null)
        runLifecycle {
            if (spawnProcess()) {
                armStartTimeout()
            }
        }
    }

    /** Stops the session without blocking: the process tree is terminated in the background. */
    fun stop() {
        val child: Process?
        val current: Connection?
        val server: ServerSocket?
        synchronized(lock) {
            if (stopped) {
                return
            }
            stopped = true
            child = process
            process = null
            current = connection
            connection = null
            server = listener
            startTimer?.cancel(false)
            startTimer = null
            rebuildTimer?.cancel(false)
            rebuildTimer = null
        }
        lifecycle.shutdown()
        current?.close()
        runCatching { server?.close() }
        if (child != null) {
            terminate(child, waitForExit = false)
        }
        callbacks.onState(SessionState.STOPPED, null)
    }

    /** Restarts the driver process (full state reset); the listener keeps waiting for the reconnect. */
    fun restartProcess(detail: String = "restarting") {
        runLifecycle { respawn(detail) }
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
            rebuildTimer = schedule(REBUILD_DEBOUNCE_MS) { respawn("rebuilding (buildRestart driver)") }
        }
    }

    fun selectTarget(id: String) {
        requestedTargetId = id
        send(PreviewProtocol.SELECT_TARGET, mapOf("id" to id))
    }

    fun refreshTarget() = send(PreviewProtocol.REFRESH_TARGET, emptyMap<String, Any>())

    fun ackFrame(seq: Long) = send(PreviewProtocol.FRAME_ACK, mapOf("seq" to seq))

    /** Sends the viewport now and remembers it for the ClientInfo of every later connection. */
    fun setViewport(width: Double, height: Double, dpi: Double) {
        viewport = ViewportMetrics(width, height, dpi)
        send(PreviewProtocol.VIEWPORT_CHANGED, mapOf("width" to width, "height" to height, "dpi" to dpi))
    }

    fun setTheme(mode: String) {
        requestedTheme = mode
        send(PreviewProtocol.SET_THEME, mapOf("mode" to mode))
    }

    fun sendInput(typeId: Int, body: Map<String, Any>) {
        if (typeId in PreviewProtocol.POINTER_MOVED..PreviewProtocol.TEXT_INPUT) {
            send(typeId, body)
        }
    }

    private fun send(typeId: Int, body: Any) {
        val current = connection ?: return
        current.enqueue(typeId, PreviewProtocol.encode(typeId, body))
    }

    private fun acceptLoop(server: ServerSocket) {
        try {
            while (!stopped) {
                onConnection(server.accept())
            }
        } catch (_: IOException) {
            // Listener closed (session shutdown); state is reported by the exit/timeout paths.
        }
    }

    private fun onConnection(client: Socket) {
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
            client.tcpNoDelay = true
            // Hello goes out before the socket is published, so no other message can precede it.
            client.getOutputStream().write(hello)
        } catch (_: IOException) {
            runCatching { client.close() }
            return
        }

        val incoming = Connection(client)
        val previous: Connection?
        synchronized(lock) {
            if (stopped) {
                incoming.close()
                return
            }
            // A process restart reconnects with a fresh socket; the newest connection wins.
            previous = connection
            connection = incoming
        }
        previous?.close()
        incoming.startWriter()
        thread(name = "mewui-preview-read", isDaemon = true) { readLoop(incoming) }
    }

    private fun readLoop(current: Connection) {
        try {
            val reader = MessageReader(current.socket.getInputStream())
            while (true) {
                val message = reader.read() ?: break
                if (connection !== current) {
                    break
                }
                onMessage(current, message)
            }
        } catch (_: IOException) {
            // Socket torn down by a restart or shutdown.
        } catch (error: Exception) {
            callbacks.onLog("protocol error: ${error.message}")
        }

        val wasCurrent: Boolean
        synchronized(lock) {
            wasCurrent = connection === current
            if (wasCurrent) {
                connection = null
            }
        }
        current.close()
        if (wasCurrent && !stopped) {
            callbacks.onState(SessionState.DISCONNECTED, null)
        }
    }

    private fun onMessage(current: Connection, message: DecodedMessage) {
        when (message.typeId) {
            PreviewProtocol.SESSION_STARTED -> {
                everConnected = true
                clearStartTimer()
                replayClientState(current)
                callbacks.onState(
                    SessionState.CONNECTED,
                    if (usingShim) "shim session (low fidelity: app Main not executed)" else null,
                )
            }
            PreviewProtocol.SESSION_REJECTED ->
                callbacks.onState(SessionState.FAILED, message.json.toString())
            PreviewProtocol.PREVIEW_TARGETS -> {
                val targets = message.json.getAsJsonArray("targets")?.map { element ->
                    PreviewProtocol.gson.fromJson(element, PreviewTargetInfo::class.java)
                } ?: emptyList()
                var activeId = message.json.get("activeId")?.takeUnless { it.isJsonNull }?.asString ?: ""
                if (current.restoreTargetPending) {
                    current.restoreTargetPending = false
                    val wanted = requestedTargetId
                    if (wanted != null && wanted != activeId && targets.any { it.id == wanted && it.available }) {
                        current.enqueue(PreviewProtocol.SELECT_TARGET, PreviewProtocol.encode(PreviewProtocol.SELECT_TARGET, mapOf("id" to wanted)))
                        activeId = wanted
                    }
                }
                callbacks.onTargets(targets, activeId)
            }
            PreviewProtocol.FRAME ->
                callbacks.onFrame(PreviewProtocol.gson.fromJson(message.json, FrameHeader::class.java), message.binary)
            PreviewProtocol.STATUS ->
                callbacks.onStatus(PreviewProtocol.gson.fromJson(message.json, StatusInfo::class.java))
            // Unknown message ids are ignored for forward compatibility.
        }
    }

    /** A new server starts from defaults: resend the panel metrics, the toggled theme, and (once targets arrive) the picked target. */
    private fun replayClientState(current: Connection) {
        viewport?.let { metrics ->
            current.enqueue(
                PreviewProtocol.CLIENT_INFO,
                PreviewProtocol.encode(
                    PreviewProtocol.CLIENT_INFO,
                    mapOf("dpi" to metrics.dpi, "viewportWidth" to metrics.width, "viewportHeight" to metrics.height),
                ),
            )
        }
        requestedTheme?.let { mode ->
            current.enqueue(PreviewProtocol.SET_THEME, PreviewProtocol.encode(PreviewProtocol.SET_THEME, mapOf("mode" to mode)))
        }
        current.restoreTargetPending = requestedTargetId != null
    }

    private fun respawn(detail: String): Boolean {
        callbacks.onState(SessionState.STARTING, detail)
        return spawnProcess()
    }

    /** Replaces any running driver process with a new one; false when the spawn failed. Lifecycle thread only. */
    private fun spawnProcess(): Boolean {
        val previous: Process?
        val port: Int
        synchronized(lock) {
            port = listener?.localPort ?: return false
            previous = process
            process = null
        }
        if (previous != null) {
            terminate(previous, waitForExit = true)
        }
        if (stopped) {
            return false
        }

        val host = resolvedHost ?: options.dotnetHost().also { resolvedHost = it }
        val projectDirectory = File(effectiveProjectPath).parentFile

        // Skipping the restore evaluation saves 2-3s per start; only safe once assets exist,
        // and a pre-connect failure retries with restore (e.g. after a PackageReference change).
        val noRestore = skipRestore && File(projectDirectory, "obj/project.assets.json").exists()
        val arguments = buildList {
            add(host.executable)
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
            clear()
            putAll(host.environment)
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
            callbacks.onState(SessionState.FAILED, "dotnet failed to start (${host.executable}): ${error.message}")
            return false
        }

        val orphaned: Boolean
        synchronized(lock) {
            orphaned = stopped
            if (!orphaned) {
                process = child
            }
        }
        if (orphaned) {
            terminate(child, waitForExit = false)
            return false
        }

        thread(name = "mewui-preview-log", isDaemon = true) {
            runCatching { child.inputStream.bufferedReader().forEachLine(::forwardOutput) }
        }
        child.onExit().thenAccept { exited -> runLifecycle { onProcessExited(exited) } }
        return true
    }

    private fun onProcessExited(child: Process) {
        val code = runCatching { child.exitValue() }.getOrDefault(-1)
        synchronized(lock) {
            if (process !== child) {
                return
            }
            process = null
        }

        // A pre-connect failure with --no-restore may just be a stale restore (new package
        // reference); retry once with the restore enabled before any driver fallback.
        if (!everConnected && code != 0 && skipRestore) {
            skipRestore = false
            callbacks.onLog("build failed before connecting; retrying with restore enabled")
            spawnProcess()
            return
        }

        // "auto" treats an early watch death (before any handshake) as watch being unusable
        // in this environment and switches to the IDE-driven restart driver.
        if (driver == ReloadDriver.WATCH && autoDriver && !everConnected && code != 0) {
            driver = ReloadDriver.BUILD_RESTART
            callbacks.onLog("dotnet watch exited with code $code before connecting; falling back to the buildRestart driver")
            respawn("buildRestart driver fallback")
            return
        }

        val watchPrefix = if (driver == ReloadDriver.WATCH) "watch " else ""
        callbacks.onState(SessionState.FAILED, "dotnet ${watchPrefix}exited with code $code")
    }

    private fun armStartTimeout() {
        synchronized(lock) {
            startTimer?.cancel(false)
            startTimer = null
            if (sessionStartTimeoutMs <= 0 || stopped || everConnected) {
                return
            }
            startTimer = schedule(sessionStartTimeoutMs, ::onStartTimeout)
        }
    }

    private fun onStartTimeout() {
        if (everConnected) {
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
        val shimPath: String
        try {
            val host = resolvedHost ?: options.dotnetHost().also { resolvedHost = it }
            shimPath = ShimProject.generate(projectPath, host, callbacks::onLog)
        } catch (error: Exception) {
            callbacks.onState(SessionState.FAILED, "session start timed out and shim generation failed: ${error.message}")
            return
        }
        // The target framework query can take seconds: the app may have connected or the user stopped meanwhile.
        if (stopped || everConnected) {
            return
        }
        effectiveProjectPath = shimPath
        usingShim = true
        if (respawn("shim fallback (low fidelity: app Main not executed)")) {
            armStartTimeout()
        }
    }

    private fun forwardOutput(line: String) {
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
        callbacks.onOutput(line)
    }

    private fun clearStartTimer() {
        synchronized(lock) {
            startTimer?.cancel(false)
            startTimer = null
        }
    }

    private fun killProcess() {
        val child: Process?
        synchronized(lock) {
            child = process
            process = null
        }
        if (child != null) {
            terminate(child, waitForExit = true)
        }
    }

    /** Queues a lifecycle transition; dropped once the session is stopped. */
    private fun runLifecycle(action: () -> Unit) {
        try {
            lifecycle.execute(guarded(action))
        } catch (_: RejectedExecutionException) {
            // Shut down by stop(): there is nothing left to transition.
        }
    }

    private fun schedule(delayMs: Long, action: () -> Unit): ScheduledFuture<*>? =
        try {
            lifecycle.schedule(guarded(action), delayMs, TimeUnit.MILLISECONDS)
        } catch (_: RejectedExecutionException) {
            null
        }

    private fun guarded(action: () -> Unit) = Runnable {
        if (!stopped) {
            try {
                action()
            } catch (error: Exception) {
                callbacks.onLog("preview session error: $error")
            }
        }
    }

    /**
     * Kills the process tree. With [waitForExit] the caller blocks until the tree exits (so the next
     * build can overwrite the output files); otherwise escalation to a forced kill happens in the background.
     */
    private fun terminate(child: Process, waitForExit: Boolean) {
        // Kill the whole tree: watch's child app process must not outlive the session. An
        // orphaned app keeps reconnecting to this session's port and fights the replacement.
        val tree = child.toHandle().descendants().toList() + child.toHandle()
        tree.forEach { handle -> handle.destroy() }
        val exits = CompletableFuture.allOf(*tree.map { handle -> handle.onExit() }.toTypedArray())
        if (waitForExit) {
            try {
                exits.get(KILL_GRACE_SECONDS, TimeUnit.SECONDS)
            } catch (_: TimeoutException) {
                tree.forEach { handle -> handle.destroyForcibly() }
                runCatching { exits.get(KILL_GRACE_SECONDS, TimeUnit.SECONDS) }
            }
        } else {
            exits.completeOnTimeout(null, KILL_GRACE_SECONDS, TimeUnit.SECONDS).thenRun {
                tree.filter { handle -> handle.isAlive }.forEach { handle -> handle.destroyForcibly() }
            }
        }
    }

    private class ViewportMetrics(val width: Double, val height: Double, val dpi: Double)

    /** One accepted socket with its own writer thread, so a stalled app never blocks a sender. */
    private class Connection(val socket: Socket) {
        private val queueLock = ReentrantLock()
        private val queued = queueLock.newCondition()
        private val queue = ArrayDeque<Outbound>()
        private var closed = false

        // Reader-thread state: replay the picked target once the new server lists its targets.
        var restoreTargetPending = false

        fun startWriter() {
            thread(name = "mewui-preview-write", isDaemon = true) { writeLoop() }
        }

        fun enqueue(typeId: Int, bytes: ByteArray) {
            queueLock.withLock {
                if (closed) {
                    return
                }
                if (typeId == PreviewProtocol.POINTER_MOVED) {
                    // Only the latest position matters; a paused app must not grow the backlog with moves.
                    if (queue.lastOrNull()?.typeId == PreviewProtocol.POINTER_MOVED) {
                        queue.removeLast()
                    } else if (queue.size >= POINTER_BACKLOG_LIMIT) {
                        return
                    }
                }
                queue.addLast(Outbound(typeId, bytes))
                queued.signal()
            }
        }

        fun close() {
            queueLock.withLock {
                closed = true
                queue.clear()
                queued.signalAll()
            }
            runCatching { socket.close() }
        }

        private fun writeLoop() {
            try {
                val stream = socket.getOutputStream()
                while (true) {
                    val next = queueLock.withLock {
                        while (queue.isEmpty() && !closed) {
                            queued.await()
                        }
                        if (closed) null else queue.removeFirst()
                    } ?: break
                    stream.write(next.bytes)
                }
            } catch (_: Exception) {
                // A broken socket surfaces through the read loop's disconnect.
            }
            close()
        }

        private class Outbound(val typeId: Int, val bytes: ByteArray)
    }
}
