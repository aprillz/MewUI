// Preview session lifecycle: listen on loopback, spawn the reload driver process with the preview
// environment, handshake (Hello/SessionStarted), then surface targets/frames/status through
// callbacks. Free of vscode API so a plain node driver can exercise it end to end.
//
// Reload drivers (plan.md 4.2/4.6):
// - "watch" (default): dotnet watch owns change detection, incremental build, and hot reload.
// - "buildRestart" (fallback): the extension detects saves and restarts `dotnet run`. Used when
//   watch is unavailable or misbehaving; "auto" switches to it when watch dies before connecting.
// If SessionStarted never arrives (user Main blocks or exits before Run), the session restarts
// with a generated shim project that skips the app entry point (low fidelity: plan.md 4.5).

import { ChildProcess, spawn, spawnSync } from "child_process";
import * as crypto from "crypto";
import * as fs from "fs";
import * as net from "net";
import * as path from "path";
import {
    FrameHeader,
    INPUT_MESSAGE_TYPES,
    MessageDecoder,
    MessageType,
    PreviewTargetInfo,
    PROTOCOL_MAJOR,
    PROTOCOL_MINOR,
    sendMessage,
    StatusInfo,
} from "./protocol";

export type SessionState = "starting" | "connected" | "disconnected" | "stopped" | "failed";
export type ReloadDriver = "watch" | "buildRestart";

export interface SessionCallbacks {
    onLog(line: string): void;
    onState(state: SessionState, detail?: string): void;
    onTargets(targets: PreviewTargetInfo[], activeId: string): void;
    onFrame(header: FrameHeader, pixels: Buffer): void;
    onStatus(status: StatusInfo): void;
}

export interface SessionOptions {
    /** "auto" (default) starts with watch and falls back to buildRestart if watch dies early. */
    reloadDriver?: "auto" | ReloadDriver;
    /** Milliseconds to wait for SessionStarted before the shim fallback; 0 disables. Default 60000. */
    sessionStartTimeoutMs?: number;
}

export type ThemeMode = "light" | "dark" | "system";

const REBUILD_DEBOUNCE_MS = 500;
const KILL_ESCALATION_MS = 2000;
// Canonical MSBuild diagnostic ("origin: error CS1002: text"); the category keyword is never localized.
const COMPILE_ERROR_PATTERN = /:\s*error\s+[A-Za-z]+\d+\s*:/;
const BUILD_FAILED_DETAIL = "build failed; fix the errors and save to retry";

export class PreviewSession {
    private readonly token = crypto.randomBytes(24).toString("base64url");
    private readonly sessionId = crypto.randomUUID();
    private readonly autoDriver: boolean;
    private readonly sessionStartTimeoutMs: number;
    private driver: ReloadDriver;
    private effectiveProjectPath: string;
    private usingShim = false;
    private everConnected = false;
    private skipRestore = true;
    private rejected = false;
    private buildFailed = false;
    // dotnet watch rebuilds by itself after a save; its next output marks the new attempt.
    private rebuildRequested = false;
    private server: net.Server | undefined;
    private socket: net.Socket | undefined;
    private childProcess: ChildProcess | undefined;
    private startTimer: NodeJS.Timeout | undefined;
    private rebuildTimer: NodeJS.Timeout | undefined;
    private stopped = false;
    // Client state replayed to every new app process, which starts on its main window at its default metrics.
    private viewport: { width: number; height: number; dpi: number } | undefined;
    private themeMode: ThemeMode | undefined;
    private activeTargetId = "";
    private restoreTargetPending = false;
    // Last-known state, replayed when a recreated panel rebinds to a kept-alive session.
    private lastState: { state: SessionState; detail?: string } | undefined;
    private lastTargets: { targets: PreviewTargetInfo[]; activeId: string } | undefined;
    private lastStatus: StatusInfo | undefined;
    private lastFrame: { header: FrameHeader; pixels: Buffer } | undefined;

    constructor(
        public readonly projectPath: string,
        private callbacks: SessionCallbacks,
        options?: SessionOptions,
    ) {
        const requested = options?.reloadDriver ?? "auto";
        this.autoDriver = requested === "auto";
        this.driver = requested === "buildRestart" ? "buildRestart" : "watch";
        this.sessionStartTimeoutMs = options?.sessionStartTimeoutMs ?? 60000;
        this.effectiveProjectPath = projectPath;
    }

    public get activeDriver(): ReloadDriver {
        return this.driver;
    }

    public get isShimSession(): boolean {
        return this.usingShim;
    }

    public get isStopped(): boolean {
        return this.stopped;
    }

    /** Rebinds a recreated panel to this running session and replays the last-known state. */
    public rebind(callbacks: SessionCallbacks): void {
        this.callbacks = callbacks;
        if (this.lastState !== undefined) {
            callbacks.onState(this.lastState.state, this.lastState.detail);
        }
        if (this.lastTargets !== undefined) {
            callbacks.onTargets(this.lastTargets.targets, this.lastTargets.activeId);
        }
        if (this.lastStatus !== undefined) {
            callbacks.onStatus(this.lastStatus);
        }
        if (this.lastFrame !== undefined) {
            callbacks.onFrame(this.lastFrame.header, this.lastFrame.pixels);
        }
    }

    private emitState(state: SessionState, detail?: string): void {
        this.lastState = { state, detail };
        this.callbacks.onState(state, detail);
    }

    private emitTargets(targets: PreviewTargetInfo[], activeId: string): void {
        this.lastTargets = { targets, activeId };
        this.callbacks.onTargets(targets, activeId);
    }

    private emitStatus(status: StatusInfo): void {
        this.lastStatus = status;
        this.callbacks.onStatus(status);
    }

    private emitFrame(header: FrameHeader, pixels: Buffer): void {
        this.lastFrame = { header, pixels };
        this.callbacks.onFrame(header, pixels);
    }

    public async start(): Promise<void> {
        const port = await this.listen();
        this.spawnProcess(port);
        this.armStartTimeout();
        this.emitState("starting");
    }

    public stop(): void {
        if (this.stopped) {
            return;
        }
        this.stopped = true;
        this.clearTimers();
        this.killProcess();
        this.socket?.destroy();
        this.server?.close();
        this.emitState("stopped");
    }

    /**
     * User-requested restart: reruns the real app (leaving any shim fallback) as a fresh start,
     * with the start timeout armed again.
     */
    public restart(): void {
        if (this.stopped || !this.server) {
            return;
        }
        this.usingShim = false;
        this.effectiveProjectPath = this.projectPath;
        this.everConnected = false;
        this.restartProcess();
        this.armStartTimeout();
    }

    /** Restarts the driver process (full state reset); the server keeps listening for the reconnect. */
    public restartProcess(detail = "restarting"): void {
        if (this.stopped || !this.server) {
            return;
        }
        this.killProcess();
        const address = this.server.address() as net.AddressInfo;
        this.spawnProcess(address.port);
        this.emitState("starting", detail);
    }

    /**
     * Feeds an IDE save event to the session: the buildRestart driver restarts (debounced); under
     * the watch driver it only marks the retry after a failed build. Callers can forward every save.
     */
    public notifySourceChanged(fsPath: string): void {
        if (this.stopped) {
            return;
        }
        if (this.driver !== "buildRestart") {
            if (this.buildFailed) {
                this.rebuildRequested = true;
            }
            return;
        }
        if (!fsPath.toLowerCase().endsWith(".cs")) {
            return;
        }
        if (this.rebuildTimer !== undefined) {
            clearTimeout(this.rebuildTimer);
        }
        this.rebuildTimer = setTimeout(() => {
            this.rebuildTimer = undefined;
            this.restartProcess("rebuilding (buildRestart driver)");
            this.armStartTimeout();
        }, REBUILD_DEBOUNCE_MS);
    }

    public selectTarget(id: string): void {
        this.activeTargetId = id;
        this.send(MessageType.SelectTarget, { id });
    }

    public refreshTarget(): void {
        this.send(MessageType.RefreshTarget, {});
    }

    public ackFrame(seq: number): void {
        this.send(MessageType.FrameAck, { seq });
    }

    public setViewport(width: number, height: number, dpi: number): void {
        this.viewport = { width, height, dpi };
        this.send(MessageType.ViewportChanged, { width, height, dpi });
    }

    public setTheme(mode: ThemeMode): void {
        this.themeMode = mode;
        this.send(MessageType.SetTheme, { mode });
    }

    /** Forwards a canvas input event; unknown kinds are dropped. */
    public sendInput(kind: string, body: unknown): void {
        const typeId = INPUT_MESSAGE_TYPES[kind];
        if (typeId !== undefined) {
            this.send(typeId, body);
        }
    }

    private send(typeId: number, body: unknown): void {
        if (this.socket && !this.socket.destroyed) {
            sendMessage(this.socket, typeId, body);
        }
    }

    private listen(): Promise<number> {
        return new Promise((resolve, reject) => {
            const server = net.createServer((socket) => this.onConnection(socket));
            server.on("error", (error) => reject(error));
            server.listen(0, "127.0.0.1", () => {
                const address = server.address() as net.AddressInfo;
                resolve(address.port);
            });
            this.server = server;
        });
    }

    private onConnection(socket: net.Socket): void {
        // A process restart reconnects with a fresh socket; the newest connection wins.
        this.socket?.destroy();
        this.socket = socket;
        socket.setNoDelay(true);

        const decoder = new MessageDecoder((message) => {
            // A failing UI callback must not be mistaken for a protocol error and drop the connection.
            try {
                this.handleMessage(message.typeId, message.json, message.binary);
            } catch (error) {
                this.callbacks.onLog(`message ${message.typeId} handling failed: ${error}`);
            }
        });

        socket.on("data", (chunk) => {
            try {
                decoder.push(chunk);
            } catch (error) {
                this.callbacks.onLog(`protocol error: ${error}`);
                socket.destroy();
            }
        });
        socket.on("close", () => {
            if (this.socket === socket) {
                this.socket = undefined;
                if (!this.stopped && !this.rejected) {
                    this.emitState("disconnected");
                }
            }
        });
        socket.on("error", () => socket.destroy());

        sendMessage(socket, MessageType.Hello, {
            protocolMajor: PROTOCOL_MAJOR,
            protocolMinor: PROTOCOL_MINOR,
            token: this.token,
            capabilities: [],
        });
    }

    private handleMessage(typeId: number, json: unknown, binary: Buffer): void {
        switch (typeId) {
            case MessageType.SessionStarted:
                this.everConnected = true;
                this.rejected = false;
                this.buildFailed = false;
                this.clearStartTimer();
                this.restoreTargetPending = true;
                if (this.viewport !== undefined) {
                    this.send(MessageType.ClientInfo, {
                        dpi: this.viewport.dpi,
                        viewportWidth: this.viewport.width,
                        viewportHeight: this.viewport.height,
                    });
                }
                if (this.themeMode !== undefined) {
                    this.send(MessageType.SetTheme, { mode: this.themeMode });
                }
                this.emitState("connected", this.usingShim ? "shim session (low fidelity: app Main not executed)" : undefined);
                break;
            case MessageType.SessionRejected: {
                const body = json as { reason?: string };
                this.rejected = true;
                this.clearStartTimer();
                this.emitState("failed", `session rejected: ${body.reason || "no reason given"}`);
                break;
            }
            case MessageType.PreviewTargets: {
                const body = json as { targets: PreviewTargetInfo[]; activeId: string };
                let activeId = body.activeId;
                if (this.restoreTargetPending) {
                    this.restoreTargetPending = false;
                    const previous = this.activeTargetId;
                    if (previous !== "" && previous !== activeId
                        && body.targets.some((target) => target.id === previous && target.available !== false)) {
                        this.callbacks.onLog(`restoring preview target ${previous}`);
                        this.send(MessageType.SelectTarget, { id: previous });
                        activeId = previous;
                    }
                }
                this.activeTargetId = activeId;
                this.emitTargets(body.targets, activeId);
                break;
            }
            case MessageType.Frame:
                this.emitFrame(json as FrameHeader, binary);
                break;
            case MessageType.Status:
                this.emitStatus(json as StatusInfo);
                break;
            default:
                // Unknown message ids are ignored for forward compatibility.
                break;
        }
    }

    private spawnProcess(port: number): void {
        // Skipping the restore evaluation saves 2-3s per start; only safe once assets exist,
        // and a pre-connect failure retries with restore (e.g. after a PackageReference change).
        const noRestore = this.skipRestore
            && fs.existsSync(path.join(path.dirname(this.effectiveProjectPath), "obj", "project.assets.json"))
            ? ["--no-restore"]
            : [];
        // The app the session runs holds its own output directory open for as long as it lives, so it gets
        // one of its own: sharing bin would fail every build made while the preview is up. Under obj, which
        // keeps the intermediate outputs shared, so this costs a copy and not a compile. Spelled --property
        // because dotnet watch reads -p as the abbreviation of --project.
        const output = `--property:BaseOutputPath=${escapeMsBuild(path.join(path.dirname(this.effectiveProjectPath), "obj", "mewui-preview") + path.sep)}`;
        const args = this.driver === "watch"
            ? ["watch", "--non-interactive", "--project", this.effectiveProjectPath, "run", ...noRestore, output]
            : ["run", "--project", this.effectiveProjectPath, ...noRestore, output];
        const child = spawn("dotnet", args, {
            cwd: path.dirname(this.effectiveProjectPath),
            env: {
                ...process.env,
                MEWUI_PREVIEW: "1",
                MEWUI_PREVIEW_ENDPOINT: `127.0.0.1:${port}`,
                MEWUI_PREVIEW_TOKEN: this.token,
                MEWUI_PREVIEW_SESSION: this.sessionId,
                DOTNET_WATCH_RESTART_ON_RUDE_EDIT: "1",
            },
            stdio: ["ignore", "pipe", "pipe"],
            // Unix: own process group, so killProcess can signal watch AND its child app.
            // Killing only the watch process leaves an orphaned app that keeps reconnecting
            // to this session's port and fights the replacement for the connection.
            detached: process.platform !== "win32",
        });
        this.childProcess = child;
        this.rejected = false;
        this.buildFailed = false;
        this.rebuildRequested = false;

        child.stdout?.on("data", (data: Buffer) => this.forwardLog(child, data));
        child.stderr?.on("data", (data: Buffer) => this.forwardLog(child, data));
        child.on("error", (error) => {
            if (this.stopped || this.childProcess !== child) {
                return;
            }
            this.clearStartTimer();
            this.emitState("failed", `dotnet failed to start: ${error.message}`);
        });
        child.on("exit", (code) => {
            if (this.childProcess !== child) {
                return;
            }
            this.childProcess = undefined;
            if (this.stopped) {
                return;
            }

            // The app quits after a rejected handshake; the rejection reason stays the reported failure.
            if (this.rejected) {
                this.clearStartTimer();
                return;
            }

            if (code !== 0 && this.retryWithRestore()) {
                return;
            }

            // "auto" treats an early watch death (before any handshake) as watch being unusable
            // in this environment and switches to the extension-driven restart driver.
            if (this.driver === "watch" && this.autoDriver && !this.everConnected && code !== 0 && this.server) {
                this.driver = "buildRestart";
                this.callbacks.onLog(`dotnet watch exited with code ${code} before connecting; falling back to the buildRestart driver`);
                const address = this.server.address() as net.AddressInfo;
                this.spawnProcess(address.port);
                this.armStartTimeout();
                this.emitState("starting", "buildRestart driver fallback");
                return;
            }

            // A Main that returns before Application.Run is the shim's case, and with the process gone
            // there is no output silence left to wait out.
            if (!this.everConnected && code === 0 && !this.buildFailed && !this.usingShim && this.startTimer !== undefined) {
                this.startShimFallback("dotnet exited before the preview handshake");
                return;
            }

            this.clearStartTimer();
            this.emitState("failed", this.buildFailed
                ? BUILD_FAILED_DETAIL
                : `dotnet ${this.driver === "watch" ? "watch " : ""}exited with code ${code}`);
        });
    }

    /**
     * A pre-connect failure with --no-restore may just be a stale restore (new package reference);
     * retries once with the restore enabled before any driver fallback.
     */
    private retryWithRestore(): boolean {
        if (this.everConnected || !this.skipRestore || !this.server) {
            return false;
        }
        this.skipRestore = false;
        this.callbacks.onLog("build failed before connecting; retrying with restore enabled");
        this.killProcess();
        const address = this.server.address() as net.AddressInfo;
        this.spawnProcess(address.port);
        this.armStartTimeout();
        return true;
    }

    private onBuildFailed(): void {
        // dotnet watch keeps waiting for a file change after a failed build instead of exiting.
        if (this.retryWithRestore()) {
            return;
        }
        this.buildFailed = true;
        this.rebuildRequested = false;
        this.clearStartTimer();
        this.emitState("failed", BUILD_FAILED_DETAIL);
    }

    private armStartTimeout(): void {
        this.clearStartTimer();
        if (this.sessionStartTimeoutMs <= 0 || this.everConnected || this.rejected || this.stopped) {
            return;
        }
        this.startTimer = setTimeout(() => this.onStartTimeout(), this.sessionStartTimeoutMs);
    }

    private onStartTimeout(): void {
        this.startTimer = undefined;
        if (this.everConnected || this.stopped || this.rejected) {
            return;
        }

        if (this.usingShim) {
            this.emitState("failed", "session start timed out (shim fallback also failed)");
            this.killProcess();
            return;
        }

        this.startShimFallback(`no SessionStarted within ${this.sessionStartTimeoutMs}ms`);
    }

    private startShimFallback(reason: string): void {
        // The app's Main likely never reached Application.Run (plan.md 4.5): restart with a shim
        // project that skips the entry point entirely.
        this.callbacks.onLog(`${reason}; retrying with the shim fallback session`);
        try {
            this.effectiveProjectPath = this.generateShimProject();
        } catch (error) {
            this.clearStartTimer();
            this.emitState("failed", `session start timed out and shim generation failed: ${error}`);
            return;
        }
        this.usingShim = true;
        this.restartProcess("shim fallback (low fidelity: app Main not executed)");
        this.armStartTimeout();
    }

    /** Writes the shim project (app reference, no app Main) under the app's obj folder. */
    private generateShimProject(): string {
        const shimDir = path.join(path.dirname(this.projectPath), "obj", "mewui-preview", "shim");
        fs.mkdirSync(shimDir, { recursive: true });

        const targetFramework = this.queryTargetFramework() ?? "net8.0";
        const referencePath = path.resolve(this.projectPath).replace(/\\/g, "/");
        fs.writeFileSync(path.join(shimDir, "MewUIPreviewShim.csproj"), `<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>${targetFramework}</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="${escapeXml(escapeMsBuild(referencePath))}" />
  </ItemGroup>

</Project>
`);
        fs.writeFileSync(path.join(shimDir, "Program.cs"), SHIM_PROGRAM_SOURCE);
        return path.join(shimDir, "MewUIPreviewShim.csproj");
    }

    private queryTargetFramework(): string | undefined {
        const result = spawnSync(
            "dotnet",
            ["msbuild", this.projectPath, "-getProperty:TargetFramework", "-getProperty:TargetFrameworks"],
            { encoding: "utf8", timeout: 30000 },
        );
        if (result.status !== 0 || !result.stdout) {
            return undefined;
        }
        try {
            const properties = (JSON.parse(result.stdout) as { Properties?: Record<string, string> }).Properties;
            const single = properties?.TargetFramework?.trim();
            if (single) {
                return single;
            }
            const multi = properties?.TargetFrameworks?.split(";").map((tfm) => tfm.trim()).filter((tfm) => tfm.length > 0);
            return multi?.[0];
        } catch {
            return undefined;
        }
    }

    private forwardLog(child: ChildProcess, data: Buffer): void {
        let compileError = false;
        for (const line of data.toString("utf8").split(/\r?\n/)) {
            if (line.length > 0) {
                this.callbacks.onLog(line);
                compileError ||= COMPILE_ERROR_PATTERN.test(line);
            }
        }
        if (this.childProcess !== child || this.stopped) {
            return;
        }

        if (this.buildFailed && this.rebuildRequested) {
            this.buildFailed = false;
            this.rebuildRequested = false;
            this.armStartTimeout();
            this.emitState("starting", "rebuilding after the build failure");
        }

        // Hot reload compile errors arrive while the app is still connected and keep it running;
        // only a build that has no app to connect is a failed start.
        if (compileError && this.socket === undefined && !this.buildFailed) {
            this.onBuildFailed();
        } else if (!this.everConnected && this.startTimer !== undefined) {
            // Process output counts as startup progress: a slow cold build must not trigger the
            // shim fallback, which exists for a Main that never reaches Application.Run. The
            // timeout measures silence, so it only fires after the output has gone quiet
            // (an app that logs forever while blocking Main defeats it - acceptable trade-off).
            this.armStartTimeout();
        }
    }

    private clearStartTimer(): void {
        if (this.startTimer !== undefined) {
            clearTimeout(this.startTimer);
            this.startTimer = undefined;
        }
    }

    private clearTimers(): void {
        this.clearStartTimer();
        if (this.rebuildTimer !== undefined) {
            clearTimeout(this.rebuildTimer);
            this.rebuildTimer = undefined;
        }
    }

    private killProcess(): void {
        const child = this.childProcess;
        this.childProcess = undefined;
        if (!child?.pid) {
            return;
        }

        if (process.platform === "win32") {
            // Kill the whole tree: watch's child app process must not outlive the session.
            spawnSync("taskkill", ["/T", "/F", "/PID", String(child.pid)], { stdio: "ignore" });
        } else {
            const pid = child.pid;
            signalProcessGroup(child, pid, "SIGTERM");
            // A hung app ignores SIGTERM, and one that outlived its watch parent still sits in the group.
            setTimeout(() => signalProcessGroup(child, pid, "SIGKILL"), KILL_ESCALATION_MS).unref();
        }
    }
}

function signalProcessGroup(child: ChildProcess, pid: number, signal: NodeJS.Signals): void {
    try {
        // Negative pid signals the detached process group (watch + app together).
        process.kill(-pid, signal);
    } catch {
        child.kill(signal);
    }
}

/** Escapes MSBuild special characters, plus the "," and ";" that split command-line property lists, as %XX. */
function escapeMsBuild(value: string): string {
    return value.replace(/[%$@';?*,]/g, (character) => `%${character.charCodeAt(0).toString(16).toUpperCase()}`);
}

function escapeXml(value: string): string {
    return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

// The generated shim entry point: loads every managed assembly in the output folder (forcing
// module initializers so the injected preview bootstrap registers), registers the per-OS default
// platform/backend reflectively, and runs an empty window. App themes/fonts configured in the
// real Main are absent. Loading by folder rather than by reference walk is deliberate: the shim
// code never uses app types, so the compiler drops the app AssemblyRef entirely.
const SHIM_PROGRAM_SOURCE = `// Auto-generated by the MewUI preview extension for shim fallback sessions. Do not edit.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

internal static class Program
{
    private static void Main()
    {
        LoadLocalAssemblies();
        RegisterPlatform();
        Application.Run(new Window { Title = "MewUI preview shim" });
    }

    private static void LoadLocalAssemblies()
    {
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            try
            {
                var loaded = Assembly.Load(AssemblyName.GetAssemblyName(file));
                // Force module initializers so the preview bootstrap injected into the app
                // assembly registers even though the shim never calls into app code.
                RuntimeHelpers.RunModuleConstructor(loaded.ManifestModule.ModuleHandle);
            }
            catch
            {
                // Native or otherwise unloadable images; the scan proceeds with what did load.
            }
        }
    }

    private static void RegisterPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            Register("Aprillz.MewUI.Platform.Win32", "Aprillz.MewUI.Win32Platform");
            Register("Aprillz.MewUI.Backend.Gdi", "Aprillz.MewUI.GdiBackend");
        }
        else if (OperatingSystem.IsLinux())
        {
            Register("Aprillz.MewUI.Platform.X11", "Aprillz.MewUI.X11Platform");
            Register("Aprillz.MewUI.Backend.MewVG.X11", "Aprillz.MewUI.MewVGX11Backend");
        }
        else if (OperatingSystem.IsMacOS())
        {
            Register("Aprillz.MewUI.Platform.MacOS", "Aprillz.MewUI.MacOSPlatform");
            Register("Aprillz.MewUI.Backend.MewVG.MacOS", "Aprillz.MewUI.MewVGMacOSBackend");
        }
    }

    private static void Register(string assemblyName, string typeName)
    {
        try
        {
            Assembly.Load(assemblyName)
                .GetType(typeName)?
                .GetMethod("Register", BindingFlags.Public | BindingFlags.Static)?
                .Invoke(null, null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[mewui-preview-shim] " + typeName + " registration failed: " + ex.Message);
        }
    }
}
`;
