// Preview tool window surface, the Swing counterpart of tools/vscode-mewui/src/panel.ts:
// toolbar (project/target pickers, zoom, theme, refresh, restart, status), frame canvas, and
// input forwarding. Frames arrive as BGRA buffers and are copied into an ARGB BufferedImage on
// the socket reader thread; the EDT only swaps the image in, then the session gets a FrameAck
// (flow control, plan.md 4.3.1).
package com.aprillz.mewui.preview

import com.intellij.openapi.actionSystem.CustomShortcutSet
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.project.DumbAwareAction
import java.awt.BorderLayout
import java.awt.Color
import java.awt.Dimension
import java.awt.FlowLayout
import java.awt.Graphics
import java.awt.Graphics2D
import java.awt.RenderingHints
import java.awt.event.ComponentAdapter
import java.awt.event.ComponentEvent
import java.awt.event.KeyAdapter
import java.awt.event.KeyEvent
import java.awt.event.MouseAdapter
import java.awt.event.MouseEvent
import java.awt.event.MouseWheelEvent
import java.awt.image.BufferedImage
import java.awt.image.DataBufferInt
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.atomic.AtomicReference
import javax.swing.JButton
import javax.swing.JCheckBox
import javax.swing.JComboBox
import javax.swing.JLabel
import javax.swing.JPanel
import javax.swing.JScrollPane
import javax.swing.JTextArea
import javax.swing.KeyStroke
import javax.swing.SwingUtilities
import javax.swing.Timer
import kotlin.math.abs
import kotlin.math.round

private const val UNTRUSTED_MESSAGE =
    "Not started: this project is not trusted. The preview builds and runs the app (dotnet watch run); trust the project, then press Start."
private const val MAX_BUILD_ERRORS = 50

class PreviewPanel(
    private val logSink: (line: String, verbose: Boolean) -> Unit,
    private val activeDocumentPathProvider: () -> String?,
    private val solutionDirectoryProvider: () -> String?,
    private val navigateToSource: (path: String, line: Int?) -> Unit,
    private val isProjectTrusted: () -> Boolean,
    private val dotnetHostProvider: () -> DotnetHost,
    loadNavigatePreference: () -> Boolean = { true },
    private val saveNavigatePreference: (Boolean) -> Unit = {},
) : JPanel(BorderLayout()) {

    private val projectCombo = JComboBox<ProjectEntry>()
    private val startButton = JButton("Start")
    private val targetCombo = JComboBox<TargetEntry>()
    private val zoomCombo = JComboBox(arrayOf("Fit", "50%", "100%", "150%", "200%"))
    private val themeButton = JButton("Theme")
    private val refreshButton = JButton("Refresh")
    private val restartButton = JButton("Restart")
    private val navigateCheck = JCheckBox("Go to code", loadNavigatePreference())
    private val stateLabel = JLabel("Not running. Pick a project and press Start.")
    private val canvas = FrameCanvas()
    private val scrollPane = JScrollPane(canvas)
    private val detailArea = JTextArea().apply { isEditable = false; foreground = Color(205, 92, 92); rows = 6 }
    private val detailPane = JScrollPane(detailArea).apply { isVisible = false }
    private val viewportTimer = Timer(250) { postViewport() }.apply { isRepeats = false }
    // The image the canvas no longer shows, handed back by the EDT for the reader thread to refill.
    private val spareImage = AtomicReference<BufferedImage?>(null)

    private var session: PreviewSession? = null
    private var sessionState: SessionState? = null
    private var targets: List<PreviewTargetInfo> = emptyList()
    private var activeTargetId = ""
    private var themeMode = ""
    private var updatingCombos = false
    // A manual dropdown pick wins over auto-match until the user moves to another editor file.
    private var manualOverride = false
    private var scanningProjects = false
    // A start requested before the project scan finished runs when the scan completes.
    private var startPending = false
    private val buildErrors = LinkedHashSet<String>()

    init {
        val toolbar = JPanel(FlowLayout(FlowLayout.LEFT, 6, 4))
        listOf(projectCombo, startButton, targetCombo, zoomCombo, themeButton, refreshButton, restartButton, navigateCheck, stateLabel)
            .forEach(toolbar::add)
        navigateCheck.toolTipText = "Jump to the target's declaration when the preview target changes"
        navigateCheck.addActionListener { saveNavigatePreference(navigateCheck.isSelected) }
        targetCombo.isEnabled = false
        themeButton.isEnabled = false
        refreshButton.isEnabled = false
        restartButton.isEnabled = false
        refreshButton.toolTipText = "Rebuild the current target"
        restartButton.toolTipText = "Restart the preview session (full state reset)"

        add(toolbar, BorderLayout.NORTH)
        add(scrollPane, BorderLayout.CENTER)
        add(detailPane, BorderLayout.SOUTH)

        startButton.addActionListener { toggleSession() }
        refreshButton.addActionListener { session?.refreshTarget() }
        restartButton.addActionListener { restartSession() }
        themeButton.addActionListener { session?.setTheme(if (themeMode == "dark") "light" else "dark") }
        zoomCombo.addActionListener {
            canvas.zoom = zoomCombo.selectedItem as String
            canvas.revalidate()
            canvas.repaint()
            postViewport()
        }
        targetCombo.addActionListener {
            if (!updatingCombos) {
                (targetCombo.selectedItem as? TargetEntry)?.let { entry ->
                    if (entry.target.available) {
                        activeTargetId = entry.target.id
                        manualOverride = true
                        session?.selectTarget(entry.target.id)
                        // Reverse sync (opt-in): picking a target jumps the editor to its declaration.
                        if (navigateCheck.isSelected) {
                            entry.target.sourcePath?.let { path -> navigateToSource(path, entry.target.sourceLine) }
                        }
                    }
                }
            }
        }
        scrollPane.viewport.addComponentListener(object : ComponentAdapter() {
            override fun componentResized(event: ComponentEvent) {
                viewportTimer.restart()
            }
        })
        // Moving the IDE window to a monitor with another scale changes the configuration, not the size.
        addPropertyChangeListener("graphicsConfiguration") {
            canvas.revalidate()
            canvas.repaint()
            postViewport()
        }
        hookInput()
        refreshProjects()
    }

    /**
     * Selects the target declared in the given file. Editor-driven calls (the user moved to
     * another file) override a manual dropdown pick; refresh-driven calls must not, or a
     * targets rebroadcast would silently revert the pick while its file is open.
     */
    fun autoMatchTarget(fsPath: String?, fromEditor: Boolean = false) {
        val current = session ?: return
        if (fsPath == null || !fsPath.endsWith(".cs", ignoreCase = true)) {
            return
        }
        if (!fromEditor && manualOverride) {
            return
        }
        val file = File(fsPath).absolutePath
        // A file that declares the active target keeps it, so jumping to a picked target's file never re-picks.
        if (targets.any { it.id == activeTargetId && isSameFile(it.sourcePath, file) }) {
            return
        }
        val match = targets.firstOrNull { it.available && isSameFile(it.sourcePath, file) } ?: return
        logSink("[session] auto-selecting ${match.id} for ${File(fsPath).name}", false)
        activeTargetId = match.id
        manualOverride = false
        current.selectTarget(match.id)
        syncTargetCombo()
    }

    fun notifySourceChanged(fsPath: String) {
        session?.notifySourceChanged(fsPath)
    }

    /** Starts the session for the auto-selected project; a no-op when one is already running. */
    fun autoStart() {
        if (session == null) {
            requestStart()
        }
    }

    /**
     * Previews the given file's project: starts a session when none runs, and replaces a failed
     * session or one running another project. The project lookup reads the disk off the EDT.
     */
    fun openFor(fsPath: String?) {
        if (fsPath == null) {
            autoStart()
            return
        }
        val solutionDirectory = solutionDirectoryProvider()
        runInBackground {
            val nearest = ProjectLocator.findNearestProject(fsPath, solutionDirectory)
                ?.takeIf(ProjectLocator::isExecutableProject)
            onEdt { applyOpenFor(fsPath, nearest) }
        }
    }

    /** Stops the session without blocking the caller; the process tree is terminated in the background. */
    fun stopSession() {
        val current = session
        session = null
        sessionState = null
        startPending = false
        current?.stop()
        setSessionUiEnabled(false)
    }

    private fun applyOpenFor(fsPath: String, projectPath: String?) {
        if (projectPath != null) {
            selectProject(projectPath)
        }
        val current = session
        val wanted = (projectCombo.selectedItem as? ProjectEntry)?.path
        if (current != null) {
            val otherProject = wanted != null && !current.projectPath.equals(wanted, ignoreCase = true)
            if (sessionState == SessionState.FAILED || otherProject) {
                stopSession()
            }
        }
        if (session == null) {
            requestStart()
        } else {
            autoMatchTarget(fsPath, fromEditor = true)
        }
    }

    private fun toggleSession() {
        if (session != null) {
            stopSession()
        } else {
            requestStart()
        }
    }

    private fun requestStart() {
        val projectPath = (projectCombo.selectedItem as? ProjectEntry)?.path
        if (projectPath != null) {
            startSession(projectPath)
            return
        }
        startPending = true
        showState("Looking for executable projects...", isError = false)
        if (!scanningProjects) {
            refreshProjects()
        }
    }

    private fun startSession(projectPath: String) {
        startPending = false
        // The session runs MSBuild targets and the app's Main: never for a project the user has not trusted.
        if (!isProjectTrusted()) {
            showState(UNTRUSTED_MESSAGE, isError = true)
            return
        }

        val callbacks = PanelCallbacks()
        val newSession = PreviewSession(projectPath, callbacks, SessionOptions(dotnetHost = dotnetHostProvider))
        callbacks.owner = newSession
        session = newSession
        sessionState = null
        clearBuildErrors()
        setSessionUiEnabled(true)
        postViewport()
        try {
            newSession.start()
        } catch (error: Exception) {
            stopSession()
            showState("failed to start: ${error.message}", isError = true)
        }
    }

    private fun restartSession() {
        val current = session ?: return
        if (!isProjectTrusted()) {
            showState(UNTRUSTED_MESSAGE, isError = true)
            return
        }
        clearBuildErrors()
        current.restartProcess()
    }

    /** Session events, dropped once their session is no longer the panel's (a stopped session's queued events). */
    private inner class PanelCallbacks : SessionCallbacks {
        lateinit var owner: PreviewSession

        private fun ifCurrent(action: () -> Unit) = onEdt {
            if (session === owner) {
                action()
            }
        }

        override fun onLog(line: String) = logSink(line, false)

        override fun onOutput(line: String) {
            // Per-frame and timing trace lines from the preview runtime stay out of the default log level.
            logSink(line, line.startsWith("[mewui-preview "))
            if (BUILD_ERROR_PATTERN.containsMatchIn(line)) {
                ifCurrent { addBuildError(line.trim()) }
            }
        }

        override fun onState(state: SessionState, detail: String?) = ifCurrent { handleSessionState(state, detail) }

        override fun onTargets(targets: List<PreviewTargetInfo>, activeId: String) = ifCurrent { handleTargets(targets, activeId) }

        override fun onFrame(header: FrameHeader, pixels: ByteArray) {
            val image = decodeFrame(header, pixels)
            if (image == null) {
                logSink("[session] dropped malformed frame ${header.seq} (${header.width}x${header.height}, stride ${header.stride})", false)
                owner.ackFrame(header.seq)
                return
            }
            onEdt {
                if (session === owner) {
                    canvas.setFrame(image, header.dpiScale)
                    owner.ackFrame(header.seq)
                }
            }
        }

        override fun onStatus(status: StatusInfo) = ifCurrent { handleStatus(status) }
    }

    private fun onEdt(action: () -> Unit) = SwingUtilities.invokeLater(action)

    private fun runInBackground(action: () -> Unit) {
        ApplicationManager.getApplication().executeOnPooledThread(Runnable { action() })
    }

    private fun setSessionUiEnabled(running: Boolean) {
        startButton.text = if (running) "Stop" else "Start"
        projectCombo.isEnabled = !running
        targetCombo.isEnabled = running
        themeButton.isEnabled = running
        refreshButton.isEnabled = running
        restartButton.isEnabled = running
        if (!running) {
            showState("Stopped.", isError = false)
        }
    }

    /** Rescans the solution for executable projects off the EDT, then applies the list on the EDT. */
    private fun refreshProjects() {
        scanningProjects = true
        val solutionDirectory = solutionDirectoryProvider()
        val activePath = activeDocumentPathProvider()
        runInBackground {
            val nearest = runCatching {
                activePath?.let { path -> ProjectLocator.findNearestProject(path, solutionDirectory) }
                    ?.takeIf(ProjectLocator::isExecutableProject)
            }.getOrNull()
            val found = runCatching { ProjectLocator.findExecutableProjects(solutionDirectory) }.getOrDefault(emptyList())
            val executables = if (nearest != null && found.none { it.equals(nearest, ignoreCase = true) }) {
                listOf(nearest) + found
            } else {
                found
            }
            onEdt { applyProjects(executables, nearest) }
        }
    }

    private fun applyProjects(executables: List<String>, nearest: String?) {
        scanningProjects = false
        val previous = (projectCombo.selectedItem as? ProjectEntry)?.path
        updatingCombos = true
        projectCombo.removeAllItems()
        executables.forEach { projectCombo.addItem(ProjectEntry(it)) }
        updatingCombos = false
        (previous ?: nearest)?.let(::selectProject)

        if (startPending) {
            startPending = false
            val selected = (projectCombo.selectedItem as? ProjectEntry)?.path
            if (session != null) {
                return
            }
            if (selected != null) {
                startSession(selected)
            } else {
                showState("No executable (.csproj with OutputType Exe/WinExe) project found.", isError = true)
            }
        }
    }

    private fun selectProject(projectPath: String) {
        for (index in 0 until projectCombo.itemCount) {
            if (projectCombo.getItemAt(index).path.equals(projectPath, ignoreCase = true)) {
                projectCombo.selectedIndex = index
                return
            }
        }
        val entry = ProjectEntry(projectPath)
        projectCombo.insertItemAt(entry, 0)
        projectCombo.selectedItem = entry
    }

    private class ProjectEntry(val path: String) {
        override fun toString(): String = File(path).nameWithoutExtension
    }

    private class TargetEntry(val target: PreviewTargetInfo) {
        override fun toString(): String {
            val label = if (target.kind == "main") target.displayName else "${target.displayName} (${target.kind})"
            return if (target.available) label else "$label - unavailable"
        }
    }

    private fun handleSessionState(state: SessionState, detail: String?) {
        sessionState = state
        logSink("[session] $state${if (detail != null) ": $detail" else ""}", false)
        when (state) {
            SessionState.FAILED, SessionState.DISCONNECTED ->
                showState("$state${if (detail != null) ": $detail" else ""}", isError = true)
            SessionState.STARTING -> {
                clearBuildErrors()
                showState(if (detail != null) "Restarting..." else "Starting...", isError = false)
            }
            else -> {}
        }
    }

    private fun handleTargets(newTargets: List<PreviewTargetInfo>, activeId: String) {
        targets = newTargets
        activeTargetId = activeId
        syncTargetCombo()
        autoMatchTarget(activeDocumentPathProvider())
    }

    private fun syncTargetCombo() {
        updatingCombos = true
        targetCombo.removeAllItems()
        var selected: TargetEntry? = null
        for (target in targets) {
            val entry = TargetEntry(target)
            targetCombo.addItem(entry)
            if (target.id == activeTargetId) {
                selected = entry
            }
        }
        if (selected != null) {
            targetCombo.selectedItem = selected
        }
        updatingCombos = false
    }

    private fun handleStatus(status: StatusInfo) {
        if (!status.hasError) {
            buildErrors.clear()
        }
        showState(status.message, status.hasError)
        showDetail(status.exceptionDetail)
        if (!status.themeMode.isNullOrEmpty()) {
            themeMode = status.themeMode
            themeButton.text = when (themeMode) {
                "dark" -> "Dark"
                "light" -> "Light"
                else -> "System"
            }
        }
    }

    /** Surfaces compiler/MSBuild errors from the driver output, which otherwise only reach the IDE log. */
    private fun addBuildError(line: String) {
        if (buildErrors.size >= MAX_BUILD_ERRORS || !buildErrors.add(line)) {
            return
        }
        showState("Build error: ${buildErrors.first()}", isError = true)
        showDetail(buildErrors.joinToString("\n"))
    }

    private fun clearBuildErrors() {
        if (buildErrors.isNotEmpty()) {
            buildErrors.clear()
            showDetail(null)
        }
    }

    private fun showDetail(text: String?) {
        detailArea.text = text ?: ""
        detailPane.isVisible = !text.isNullOrEmpty()
        revalidate()
    }

    private fun showState(message: String, isError: Boolean) {
        stateLabel.text = message
        stateLabel.foreground = if (isError) Color(205, 92, 92) else null
        stateLabel.toolTipText = message
    }

    private fun zoomFactor(): Double {
        val zoom = zoomCombo.selectedItem as? String ?: "Fit"
        if (zoom == "Fit") {
            return 1.0
        } else {
            return zoom.trimEnd('%').toInt() / 100.0
        }
    }

    private fun postViewport() {
        val current = session ?: return
        val viewport = scrollPane.viewport.size
        if (viewport.width <= 0) {
            return
        }
        current.setViewport(
            maxOf(1.0, (viewport.width - 16).toDouble()),
            maxOf(1.0, (viewport.height - 16).toDouble()),
            96.0 * deviceScale() * zoomFactor(),
        )
    }

    private fun deviceScale(): Double =
        graphicsConfiguration?.defaultTransform?.scaleX ?: 1.0

    /** Copies a BGRA frame into a reusable ARGB image; runs on the socket reader thread, never the EDT. */
    private fun decodeFrame(header: FrameHeader, pixels: ByteArray): BufferedImage? {
        val width = header.width
        val height = header.height
        val rowBytes = width.toLong() * 4
        if (width <= 0 || height <= 0 || header.stride < rowBytes
            || pixels.size < header.stride.toLong() * (height - 1) + rowBytes
        ) {
            return null
        }

        var image = spareImage.getAndSet(null)
        if (image == null || image.width != width || image.height != height) {
            image = BufferedImage(width, height, BufferedImage.TYPE_INT_ARGB)
        }
        val target = (image.raster.dataBuffer as DataBufferInt).data
        // BGRA little-endian bytes read as ints are exactly ARGB values.
        if (header.stride.toLong() == rowBytes) {
            ByteBuffer.wrap(pixels, 0, width * 4 * height).order(ByteOrder.LITTLE_ENDIAN).asIntBuffer()
                .get(target, 0, width * height)
        } else {
            for (row in 0 until height) {
                ByteBuffer.wrap(pixels, row * header.stride, width * 4).order(ByteOrder.LITTLE_ENDIAN).asIntBuffer()
                    .get(target, row * width, width)
            }
        }
        return image
    }

    private inner class FrameCanvas : JPanel() {
        var zoom: String = "Fit"
        private var image: BufferedImage? = null
        private var dpiScale = 1.0

        init {
            isFocusable = true
            // Tab must reach the app instead of moving IDE focus.
            focusTraversalKeysEnabled = false
        }

        fun setFrame(next: BufferedImage, frameDpiScale: Double) {
            dpiScale = if (frameDpiScale > 0) frameDpiScale else 1.0
            val previous = image
            image = next
            if (previous == null || previous.width != next.width || previous.height != next.height) {
                revalidate()
            }
            if (previous != null && previous !== next) {
                spareImage.set(previous)
            }
            repaint()
        }

        /**
         * Displayed size in swing units. Frames are rendered at the device scale (times the
         * zoom), so frame pixels / device scale shows one frame pixel per device pixel; Fit
         * only ever shrinks that natural size to the viewport.
         */
        private fun displaySize(): Dimension {
            val current = image ?: return Dimension(1, 1)
            val scale = deviceScale()
            val naturalWidth = current.width / scale
            val naturalHeight = current.height / scale
            if (zoom == "Fit") {
                val viewport = scrollPane.viewport.size
                val fit = minOf(
                    1.0,
                    (viewport.width - 16) / naturalWidth,
                    (viewport.height - 16) / naturalHeight,
                )
                return Dimension((naturalWidth * fit).toInt().coerceAtLeast(1), (naturalHeight * fit).toInt().coerceAtLeast(1))
            }
            return Dimension(naturalWidth.toInt().coerceAtLeast(1), naturalHeight.toInt().coerceAtLeast(1))
        }

        override fun getPreferredSize(): Dimension = displaySize()

        override fun paintComponent(graphics: Graphics) {
            super.paintComponent(graphics)
            val current = image ?: return
            val size = displaySize()
            val g2 = graphics.create() as Graphics2D
            try {
                // Integer device-pixel upscales keep pixels crisp; everything else (fit
                // downscale, fractional zoom) needs resampling - AWT defaults to nearest.
                val factor = size.width * deviceScale() / current.width
                val isIntegerUpscale = factor >= 1.0 && abs(factor - round(factor)) < 0.01
                g2.setRenderingHint(
                    RenderingHints.KEY_INTERPOLATION,
                    if (isIntegerUpscale) RenderingHints.VALUE_INTERPOLATION_NEAREST_NEIGHBOR
                    else RenderingHints.VALUE_INTERPOLATION_BILINEAR,
                )
                g2.drawImage(current, 0, 0, size.width, size.height, null)
            } finally {
                g2.dispose()
            }
        }

        fun toDip(event: MouseEvent): Pair<Double, Double> {
            val current = image ?: return 0.0 to 0.0
            val size = displaySize()
            val scale = if (size.width > 0) current.width.toDouble() / size.width else 1.0
            return (event.x * scale / dpiScale) to (event.y * scale / dpiScale)
        }
    }

    private fun hookInput() {
        val mouse = object : MouseAdapter() {
            override fun mousePressed(event: MouseEvent) {
                canvas.requestFocusInWindow()
                sendPointer(PreviewProtocol.POINTER_PRESSED, event)
            }

            override fun mouseReleased(event: MouseEvent) = sendPointer(PreviewProtocol.POINTER_RELEASED, event)

            override fun mouseMoved(event: MouseEvent) = sendPointer(PreviewProtocol.POINTER_MOVED, event)

            override fun mouseDragged(event: MouseEvent) = sendPointer(PreviewProtocol.POINTER_MOVED, event)

            override fun mouseWheelMoved(event: MouseWheelEvent) {
                val (x, y) = canvas.toDip(event)
                // AWT +rotation = scroll down; the wire convention is +Y = scroll-up intent in notches.
                session?.sendInput(
                    PreviewProtocol.SCROLL,
                    mapOf(
                        "x" to x, "y" to y,
                        "deltaX" to 0.0, "deltaY" to -event.preciseWheelRotation,
                        "modifiers" to W3cInput.modifiers(event),
                    ),
                )
                event.consume()
            }
        }
        canvas.addMouseListener(mouse)
        canvas.addMouseMotionListener(mouse)
        canvas.addMouseWheelListener(mouse)

        canvas.addKeyListener(object : KeyAdapter() {
            override fun keyPressed(event: KeyEvent) = sendKey(event, isDown = true)

            override fun keyReleased(event: KeyEvent) = sendKey(event, isDown = false)

            override fun keyTyped(event: KeyEvent) {
                val char = event.keyChar
                if (!char.isISOControl() && char != KeyEvent.CHAR_UNDEFINED) {
                    session?.sendInput(PreviewProtocol.TEXT_INPUT, mapOf("text" to char.toString()))
                }
            }
        })

        // The keymap binds Escape to "focus editor", which the IDE runs before component key
        // listeners; a shortcut scoped to the canvas wins, so the press reaches the app instead.
        DumbAwareAction.create {
            session?.sendInput(PreviewProtocol.KEY, mapOf("code" to "Escape", "isDown" to true, "modifiers" to 0))
        }.registerCustomShortcutSet(CustomShortcutSet(KeyStroke.getKeyStroke(KeyEvent.VK_ESCAPE, 0)), canvas)
    }

    private fun sendPointer(typeId: Int, event: MouseEvent) {
        val (x, y) = canvas.toDip(event)
        session?.sendInput(
            typeId,
            mapOf(
                "x" to x, "y" to y,
                "button" to W3cInput.button(event),
                "buttons" to W3cInput.buttons(event),
                "clickCount" to maxOf(1, event.clickCount),
                "modifiers" to W3cInput.modifiers(event),
            ),
        )
    }

    private fun sendKey(event: KeyEvent, isDown: Boolean) {
        // Every key goes out (unmapped ones as "Unidentified") so the app resets its per-keystroke text suppression.
        session?.sendInput(
            PreviewProtocol.KEY,
            mapOf("code" to W3cInput.keyCode(event), "isDown" to isDown, "modifiers" to W3cInput.modifiers(event)),
        )
        // Mirror the native key-then-character sequence for keys keyTyped skips.
        if (isDown && W3cInput.modifiers(event) and 13 == 0) {
            when (event.keyCode) {
                KeyEvent.VK_ENTER -> session?.sendInput(PreviewProtocol.TEXT_INPUT, mapOf("text" to "\r"))
                KeyEvent.VK_TAB -> session?.sendInput(PreviewProtocol.TEXT_INPUT, mapOf("text" to "\t"))
            }
        }
        if (event.keyCode in intArrayOf(KeyEvent.VK_TAB, KeyEvent.VK_LEFT, KeyEvent.VK_RIGHT, KeyEvent.VK_UP, KeyEvent.VK_DOWN, KeyEvent.VK_SPACE)) {
            event.consume()
        }
    }

    private companion object {
        val BUILD_ERROR_PATTERN = Regex("""\berror\s+[A-Z]{2,}\d+\s*:""")

        fun isSameFile(sourcePath: String?, absolutePath: String): Boolean =
            sourcePath != null && File(sourcePath).absolutePath.equals(absolutePath, ignoreCase = true)
    }
}
