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

const REBUILD_DEBOUNCE_MS = 500;

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
    private server: net.Server | undefined;
    private socket: net.Socket | undefined;
    private childProcess: ChildProcess | undefined;
    private startTimer: NodeJS.Timeout | undefined;
    private rebuildTimer: NodeJS.Timeout | undefined;
    private stopped = false;
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
     * Feeds an IDE save event to the buildRestart driver (debounced restart). A no-op under the
     * watch driver, so callers can forward every save unconditionally.
     */
    public notifySourceChanged(fsPath: string): void {
        if (this.stopped || this.driver !== "buildRestart" || !fsPath.toLowerCase().endsWith(".cs")) {
            return;
        }
        if (this.rebuildTimer !== undefined) {
            clearTimeout(this.rebuildTimer);
        }
        this.rebuildTimer = setTimeout(() => {
            this.rebuildTimer = undefined;
            this.restartProcess("rebuilding (buildRestart driver)");
        }, REBUILD_DEBOUNCE_MS);
    }

    public selectTarget(id: string): void {
        this.send(MessageType.SelectTarget, { id });
    }

    public refreshTarget(): void {
        this.send(MessageType.RefreshTarget, {});
    }

    public ackFrame(seq: number): void {
        this.send(MessageType.FrameAck, { seq });
    }

    public setViewport(width: number, height: number, dpi: number): void {
        this.send(MessageType.ViewportChanged, { width, height, dpi });
    }

    public setTheme(mode: "light" | "dark" | "system"): void {
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
            switch (message.typeId) {
                case MessageType.SessionStarted:
                    this.everConnected = true;
                    this.clearStartTimer();
                    this.emitState("connected", this.usingShim ? "shim session (low fidelity: app Main not executed)" : undefined);
                    break;
                case MessageType.SessionRejected:
                    this.emitState("failed", JSON.stringify(message.json));
                    break;
                case MessageType.PreviewTargets: {
                    const body = message.json as { targets: PreviewTargetInfo[]; activeId: string };
                    this.emitTargets(body.targets, body.activeId);
                    break;
                }
                case MessageType.Frame:
                    this.emitFrame(message.json as FrameHeader, message.binary);
                    break;
                case MessageType.Status:
                    this.emitStatus(message.json as StatusInfo);
                    break;
                default:
                    // Unknown message ids are ignored for forward compatibility.
                    break;
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
                if (!this.stopped) {
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

    private spawnProcess(port: number): void {
        // Skipping the restore evaluation saves 2-3s per start; only safe once assets exist,
        // and a pre-connect failure retries with restore (e.g. after a PackageReference change).
        const noRestore = this.skipRestore
            && fs.existsSync(path.join(path.dirname(this.effectiveProjectPath), "obj", "project.assets.json"))
            ? ["--no-restore"]
            : [];
        const args = this.driver === "watch"
            ? ["watch", "--non-interactive", "--project", this.effectiveProjectPath, "run", ...noRestore]
            : ["run", "--project", this.effectiveProjectPath, ...noRestore];
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
        });
        this.childProcess = child;

        child.stdout?.on("data", (data: Buffer) => this.forwardLog(data));
        child.stderr?.on("data", (data: Buffer) => this.forwardLog(data));
        child.on("error", (error) => this.emitState("failed", `dotnet failed to start: ${error.message}`));
        child.on("exit", (code) => {
            if (this.childProcess !== child) {
                return;
            }
            this.childProcess = undefined;
            if (this.stopped) {
                return;
            }

            // A pre-connect failure with --no-restore may just be a stale restore (new package
            // reference); retry once with the restore enabled before any driver fallback.
            if (!this.everConnected && code !== 0 && this.skipRestore && this.server) {
                this.skipRestore = false;
                this.callbacks.onLog("build failed before connecting; retrying with restore enabled");
                const address = this.server.address() as net.AddressInfo;
                this.spawnProcess(address.port);
                return;
            }

            // "auto" treats an early watch death (before any handshake) as watch being unusable
            // in this environment and switches to the extension-driven restart driver.
            if (this.driver === "watch" && this.autoDriver && !this.everConnected && code !== 0 && this.server) {
                this.driver = "buildRestart";
                this.callbacks.onLog(`dotnet watch exited with code ${code} before connecting; falling back to the buildRestart driver`);
                const address = this.server.address() as net.AddressInfo;
                this.spawnProcess(address.port);
                this.emitState("starting", "buildRestart driver fallback");
                return;
            }

            this.emitState("failed", `dotnet ${this.driver === "watch" ? "watch " : ""}exited with code ${code}`);
        });
    }

    private armStartTimeout(): void {
        this.clearStartTimer();
        if (this.sessionStartTimeoutMs <= 0) {
            return;
        }
        this.startTimer = setTimeout(() => this.onStartTimeout(), this.sessionStartTimeoutMs);
    }

    private onStartTimeout(): void {
        if (this.everConnected || this.stopped) {
            return;
        }

        if (this.usingShim) {
            this.emitState("failed", "session start timed out (shim fallback also failed)");
            this.killProcess();
            return;
        }

        // The app's Main likely never reached Application.Run (plan.md 4.5): restart with a shim
        // project that skips the entry point entirely.
        this.callbacks.onLog(`no SessionStarted within ${this.sessionStartTimeoutMs}ms; retrying with the shim fallback session`);
        try {
            this.effectiveProjectPath = this.generateShimProject();
        } catch (error) {
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
    <ProjectReference Include="${referencePath}" />
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

    private forwardLog(data: Buffer): void {
        for (const line of data.toString("utf8").split(/\r?\n/)) {
            if (line.length > 0) {
                this.callbacks.onLog(line);
            }
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
            child.kill("SIGTERM");
        }
    }
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
