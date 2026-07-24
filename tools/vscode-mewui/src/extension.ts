import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import { PreviewPanel } from "./panel";
import { PreviewSession, SessionOptions } from "./session";
import { PreviewTargetInfo } from "./protocol";

let session: PreviewSession | undefined;
let panel: PreviewPanel | undefined;
let outputChannel: vscode.OutputChannel;
let sessionSubscriptions: vscode.Disposable[] = [];
let lastTargets: PreviewTargetInfo[] = [];
let activeTargetId = "";

export function activate(context: vscode.ExtensionContext): void {
    outputChannel = vscode.window.createOutputChannel("MewUI Preview");
    context.subscriptions.push(
        outputChannel,
        vscode.commands.registerCommand("mewui.preview.start", startPreview),
        vscode.commands.registerCommand("mewui.preview.stop", stopPreview),
        vscode.commands.registerCommand("mewui.preview.refreshTarget", () => session?.refreshTarget()),
        vscode.commands.registerCommand("mewui.preview.restart", () => session?.restartProcess()),
    );
}

export function deactivate(): void {
    stopPreview();
}

async function startPreview(): Promise<void> {
    if (!vscode.workspace.isTrusted) {
        void vscode.window.showWarningMessage("MewUI Preview requires a trusted workspace (it builds and runs project code).");
        return;
    }

    const projectPath = await resolveExecutableProject();
    if (!projectPath) {
        return;
    }

    stopPreview();

    const configuration = vscode.workspace.getConfiguration("mewui.preview");
    const options: SessionOptions = {
        reloadDriver: configuration.get<"auto" | "watch" | "buildRestart">("reloadDriver", "auto"),
        sessionStartTimeoutMs: configuration.get<number>("sessionStartTimeoutSeconds", 60) * 1000,
    };
    const autoSelectTarget = configuration.get<boolean>("autoSelectTarget", true);

    const newPanel = new PreviewPanel(`MewUI Preview: ${path.basename(projectPath, ".csproj")}`, {
        onSelectTarget: (id) => {
            activeTargetId = id;
            session?.selectTarget(id);
        },
        onRefresh: () => session?.refreshTarget(),
        onRestart: () => session?.restartProcess(),
        onRendered: (seq) => session?.ackFrame(seq),
        onViewport: debounce((width: number, height: number, dpi: number) => {
            session?.setViewport(width, height, dpi);
        }, 250),
        onSetTheme: (mode) => session?.setTheme(mode as "light" | "dark" | "system"),
        onDisposed: () => {
            panel = undefined;
            stopPreview();
        },
    });
    panel = newPanel;

    const newSession = new PreviewSession(projectPath, {
        onLog: (line) => outputChannel.appendLine(`${timestamp()} ${line}`),
        onState: (state, detail) => {
            outputChannel.appendLine(`${timestamp()} [session] ${state}${detail ? `: ${detail}` : ""}`);
            newPanel.showSessionState(state, detail);
        },
        onTargets: (targets, activeId) => {
            lastTargets = targets;
            activeTargetId = activeId;
            newPanel.showTargets(targets, activeId);
            if (autoSelectTarget) {
                autoMatchTarget(vscode.window.activeTextEditor);
            }
        },
        onFrame: (header, pixels) => newPanel.showFrame(header, pixels),
        onStatus: (status) => newPanel.showStatus(status),
    }, options);
    session = newSession;

    // The buildRestart driver relies on IDE save events (watch owns detection otherwise, and
    // notifySourceChanged is a no-op there). Auto-match follows the active editor (plan.md 4.5).
    sessionSubscriptions.push(
        vscode.workspace.onDidSaveTextDocument((document) => newSession.notifySourceChanged(document.uri.fsPath)),
    );
    if (autoSelectTarget) {
        sessionSubscriptions.push(
            vscode.window.onDidChangeActiveTextEditor((editor) => autoMatchTarget(editor)),
        );
    }

    try {
        await newSession.start();
    } catch (error) {
        void vscode.window.showErrorMessage(`MewUI Preview failed to start: ${error}`);
        stopPreview();
    }
}

/** Selects the preview target declared in the active editor's file (server-provided sourcePath). */
function autoMatchTarget(editor: vscode.TextEditor | undefined): void {
    const fsPath = editor?.document.uri.fsPath;
    if (!session || !fsPath || !fsPath.toLowerCase().endsWith(".cs")) {
        return;
    }

    const match = lastTargets.find(
        (target) => target.available !== false && target.sourcePath && samePath(target.sourcePath, fsPath),
    );
    if (match && match.id !== activeTargetId) {
        outputChannel.appendLine(`${timestamp()} [session] auto-selecting ${match.id} for ${path.basename(fsPath)}`);
        activeTargetId = match.id;
        session.selectTarget(match.id);
    }
}

function samePath(left: string, right: string): boolean {
    const normalizedLeft = path.normalize(left);
    const normalizedRight = path.normalize(right);
    if (process.platform === "win32" || process.platform === "darwin") {
        return normalizedLeft.toLowerCase() === normalizedRight.toLowerCase();
    }
    return normalizedLeft === normalizedRight;
}

function stopPreview(): void {
    for (const subscription of sessionSubscriptions) {
        subscription.dispose();
    }
    sessionSubscriptions = [];
    lastTargets = [];
    activeTargetId = "";
    session?.stop();
    session = undefined;
    const currentPanel = panel;
    panel = undefined;
    currentPanel?.dispose();
}

/** Resolves the executable project for the active document (plan.md 4.5.1/4.7). */
async function resolveExecutableProject(): Promise<string | undefined> {
    const activePath = vscode.window.activeTextEditor?.document.uri.fsPath;
    const nearest = activePath ? findNearestProject(activePath) : undefined;
    if (nearest && isExecutableProject(nearest)) {
        return nearest;
    }

    // Library (or no) context: offer the workspace's executable projects.
    const projectUris = await vscode.workspace.findFiles("**/*.csproj", "**/{bin,obj,node_modules}/**");
    const executables = projectUris.map((uri) => uri.fsPath).filter(isExecutableProject).sort();
    if (executables.length === 0) {
        void vscode.window.showWarningMessage("No executable (.csproj with OutputType Exe/WinExe) project found in the workspace.");
        return undefined;
    }
    if (executables.length === 1) {
        return executables[0];
    }

    const picked = await vscode.window.showQuickPick(
        executables.map((projectPath) => ({
            label: path.basename(projectPath, ".csproj"),
            description: vscode.workspace.asRelativePath(projectPath),
            projectPath,
        })),
        { placeHolder: "Select the executable project to run the preview session in" },
    );
    return picked?.projectPath;
}

function findNearestProject(filePath: string): string | undefined {
    let directory = path.dirname(filePath);
    const root = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(filePath))?.uri.fsPath;

    for (;;) {
        const projects = fs.readdirSync(directory).filter((entry) => entry.toLowerCase().endsWith(".csproj"));
        if (projects.length > 0) {
            return path.join(directory, projects.sort()[0]);
        }
        if (root !== undefined && path.relative(root, directory) === "") {
            return undefined;
        }
        const parent = path.dirname(directory);
        if (parent === directory) {
            return undefined;
        }
        directory = parent;
    }
}

function isExecutableProject(projectPath: string): boolean {
    try {
        const text = fs.readFileSync(projectPath, "utf8");
        return /<OutputType>\s*(WinExe|Exe)\s*<\/OutputType>/i.test(text);
    } catch {
        return false;
    }
}

function timestamp(): string {
    return `[${new Date().toISOString().slice(11, 23)}]`;
}

function debounce<TArgs extends unknown[]>(action: (...args: TArgs) => void, delayMs: number): (...args: TArgs) => void {
    let timer: NodeJS.Timeout | undefined;
    return (...args: TArgs) => {
        if (timer !== undefined) {
            clearTimeout(timer);
        }
        timer = setTimeout(() => action(...args), delayMs);
    };
}
