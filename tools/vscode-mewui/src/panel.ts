// Webview preview panel: canvas surface + toolbar (target picker, refresh, restart, status).
// Frames arrive as BGRA buffers; the webview converts to RGBA, draws, then reports rendered so
// the session can ack (flow control per plan.md 4.3.1).

import * as vscode from "vscode";
import { FrameHeader, PreviewTargetInfo, StatusInfo } from "./protocol";

export interface PanelCallbacks {
    onSelectTarget(id: string): void;
    onRefresh(): void;
    onRestart(): void;
    onRendered(seq: number): void;
    onViewport(width: number, height: number, dpi: number): void;
    onSetTheme(mode: string): void;
    onDisposed(): void;
}

export class PreviewPanel {
    private readonly panel: vscode.WebviewPanel;
    // Messages posted before the webview script registered its listener are dropped by VSCode;
    // cache the latest of each kind and flush once the webview reports ready.
    private webviewReady = false;
    private pendingFrame: { header: FrameHeader; pixels: Buffer } | undefined;
    private pendingTargets: { targets: PreviewTargetInfo[]; activeId: string } | undefined;
    private pendingStatus: StatusInfo | undefined;
    private pendingState: { state: string; detail?: string } | undefined;

    constructor(title: string, private readonly callbacks: PanelCallbacks) {
        this.panel = vscode.window.createWebviewPanel(
            "mewuiPreview",
            title,
            { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
            { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] },
        );
        this.panel.webview.html = createHtml();
        this.panel.webview.onDidReceiveMessage((message) => this.onMessage(message));
        this.panel.onDidDispose(() => callbacks.onDisposed());
    }

    public reveal(): void {
        this.panel.reveal(vscode.ViewColumn.Beside, true);
    }

    public dispose(): void {
        this.panel.dispose();
    }

    public showFrame(header: FrameHeader, pixels: Buffer): void {
        if (!this.webviewReady) {
            this.pendingFrame = { header, pixels };
            return;
        }
        void this.panel.webview.postMessage({
            type: "frame",
            seq: header.seq,
            width: header.width,
            height: header.height,
            stride: header.stride,
            pixels: pixels.buffer.slice(pixels.byteOffset, pixels.byteOffset + pixels.byteLength),
        });
    }

    public showTargets(targets: PreviewTargetInfo[], activeId: string): void {
        if (!this.webviewReady) {
            this.pendingTargets = { targets, activeId };
            return;
        }
        void this.panel.webview.postMessage({ type: "targets", targets, activeId });
    }

    public showStatus(status: StatusInfo): void {
        if (!this.webviewReady) {
            this.pendingStatus = status;
            return;
        }
        void this.panel.webview.postMessage({ type: "status", status });
    }

    public showSessionState(state: string, detail?: string): void {
        if (!this.webviewReady) {
            this.pendingState = { state, detail };
            return;
        }
        void this.panel.webview.postMessage({ type: "sessionState", state, detail });
    }

    private flushPending(): void {
        const targets = this.pendingTargets;
        this.pendingTargets = undefined;
        if (targets !== undefined) {
            this.showTargets(targets.targets, targets.activeId);
        }

        const state = this.pendingState;
        this.pendingState = undefined;
        if (state !== undefined) {
            this.showSessionState(state.state, state.detail);
        }

        const status = this.pendingStatus;
        this.pendingStatus = undefined;
        if (status !== undefined) {
            this.showStatus(status);
        }

        const frame = this.pendingFrame;
        this.pendingFrame = undefined;
        if (frame !== undefined) {
            this.showFrame(frame.header, frame.pixels);
        }
    }

    private onMessage(message: { type: string; [key: string]: unknown }): void {
        switch (message.type) {
            case "ready":
                this.webviewReady = true;
                this.flushPending();
                break;
            case "rendered":
                this.callbacks.onRendered(message.seq as number);
                break;
            case "selectTarget":
                this.callbacks.onSelectTarget(message.id as string);
                break;
            case "refresh":
                this.callbacks.onRefresh();
                break;
            case "restart":
                this.callbacks.onRestart();
                break;
            case "viewport":
                this.callbacks.onViewport(
                    message.width as number,
                    message.height as number,
                    message.dpi as number,
                );
                break;
            case "setTheme":
                this.callbacks.onSetTheme(message.mode as string);
                break;
        }
    }
}

function createHtml(): string {
    const nonce = Math.random().toString(36).slice(2);
    return /* html */ `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  * { box-sizing: border-box; }
  body { margin: 0; height: 100vh; display: flex; flex-direction: column; overflow: hidden;
         color: var(--vscode-foreground); background: var(--vscode-editor-background);
         font-family: var(--vscode-font-family); font-size: 12px; }
  #toolbar { display: flex; align-items: center; gap: 6px; padding: 4px 8px;
             border-bottom: 1px solid var(--vscode-panel-border); flex: none; }
  #toolbar select { max-width: 45%; background: var(--vscode-dropdown-background);
                    color: var(--vscode-dropdown-foreground);
                    border: 1px solid var(--vscode-dropdown-border); }
  #toolbar button { border: 0; border-radius: 2px; padding: 2px 8px; cursor: pointer;
                    color: var(--vscode-button-foreground); background: var(--vscode-button-background); }
  #toolbar button:hover { background: var(--vscode-button-hoverBackground); }
  #state { margin-left: auto; max-width: 55%; overflow: hidden; text-overflow: ellipsis;
           white-space: nowrap; opacity: .8; }
  #state.error { color: var(--vscode-errorForeground); opacity: 1; }
  #surface { flex: 1; display: flex; align-items: center; justify-content: center;
             overflow: auto; padding: 8px; }
  canvas { border: 1px solid var(--vscode-panel-border); flex: none; }
  canvas.fit { max-width: 100%; max-height: 100%; }
  #detail { flex: none; max-height: 30%; overflow: auto; display: none; margin: 0; padding: 6px 8px;
            border-top: 1px solid var(--vscode-panel-border);
            color: var(--vscode-errorForeground); white-space: pre-wrap;
            font-family: var(--vscode-editor-font-family); font-size: 11px; }
</style>
</head>
<body>
  <div id="toolbar">
    <select id="targets" title="Preview target"></select>
    <select id="zoom" title="Zoom (display scale only)">
      <option value="fit" selected>Fit</option>
      <option value="50">50%</option>
      <option value="100">100%</option>
      <option value="150">150%</option>
      <option value="200">200%</option>
    </select>
    <button id="theme" title="Toggle light/dark theme">Theme</button>
    <button id="refresh" title="Rebuild the current target">Refresh</button>
    <button id="restart" title="Restart the preview session (full state reset)">Restart</button>
    <span id="state">Starting...</span>
  </div>
  <div id="surface"><canvas id="canvas" width="1" height="1"></canvas></div>
  <pre id="detail"></pre>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const canvas = document.getElementById("canvas");
  const context = canvas.getContext("2d");
  const targetsSelect = document.getElementById("targets");
  const zoomSelect = document.getElementById("zoom");
  const themeButton = document.getElementById("theme");
  const stateSpan = document.getElementById("state");
  const detailPre = document.getElementById("detail");
  let imageData = null;
  let themeMode = "";

  // Zoom is display-only (plan.md 4.5): 100% maps one frame pixel to one device pixel.
  function applyZoom() {
    const zoom = zoomSelect.value;
    if (zoom === "fit") {
      canvas.classList.add("fit");
      canvas.style.width = "";
      canvas.style.height = "";
    } else {
      const scale = parseInt(zoom, 10) / 100;
      canvas.classList.remove("fit");
      canvas.style.width = (canvas.width / window.devicePixelRatio * scale) + "px";
      canvas.style.height = (canvas.height / window.devicePixelRatio * scale) + "px";
    }
  }

  window.addEventListener("message", (event) => {
    const message = event.data;
    if (message.type === "frame") {
      drawFrame(message);
      vscode.postMessage({ type: "rendered", seq: message.seq });
    } else if (message.type === "targets") {
      targetsSelect.replaceChildren();
      for (const target of message.targets) {
        const option = document.createElement("option");
        option.value = target.id;
        const label = target.kind === "main" ? target.displayName : target.displayName + " (" + target.kind + ")";
        if (target.available === false) {
          option.textContent = label + " - unavailable";
          option.disabled = true;
          option.title = target.reason ?? "";
        } else {
          option.textContent = label;
        }
        option.selected = target.id === message.activeId;
        targetsSelect.append(option);
      }
    } else if (message.type === "status") {
      stateSpan.textContent = message.status.message;
      stateSpan.className = message.status.hasError ? "error" : "";
      detailPre.textContent = message.status.exceptionDetail ?? "";
      detailPre.style.display = message.status.exceptionDetail ? "block" : "none";
      if (message.status.themeMode) {
        themeMode = message.status.themeMode;
        themeButton.textContent = themeMode === "dark" ? "Dark" : themeMode === "light" ? "Light" : "System";
      }
    } else if (message.type === "sessionState") {
      if (message.state === "failed" || message.state === "disconnected") {
        stateSpan.textContent = message.state + (message.detail ? ": " + message.detail : "");
        stateSpan.className = "error";
      } else if (message.state === "starting") {
        stateSpan.textContent = message.detail ? "Restarting..." : "Starting...";
        stateSpan.className = "";
      }
    }
  });

  function drawFrame(message) {
    const { width, height, stride } = message;
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width;
      canvas.height = height;
      imageData = context.createImageData(width, height);
      applyZoom();
    }
    if (imageData === null) {
      imageData = context.createImageData(width, height);
    }
    const source = new Uint8Array(message.pixels);
    const destination = imageData.data;
    for (let y = 0; y < height; y++) {
      let sourceOffset = y * stride;
      let destinationOffset = y * width * 4;
      for (let x = 0; x < width; x++) {
        destination[destinationOffset] = source[sourceOffset + 2];
        destination[destinationOffset + 1] = source[sourceOffset + 1];
        destination[destinationOffset + 2] = source[sourceOffset];
        destination[destinationOffset + 3] = source[sourceOffset + 3];
        sourceOffset += 4;
        destinationOffset += 4;
      }
    }
    context.putImageData(imageData, 0, 0);
  }

  targetsSelect.addEventListener("change", () => {
    vscode.postMessage({ type: "selectTarget", id: targetsSelect.value });
  });
  zoomSelect.addEventListener("change", () => applyZoom());
  themeButton.addEventListener("click", () => {
    vscode.postMessage({ type: "setTheme", mode: themeMode === "dark" ? "light" : "dark" });
  });
  document.getElementById("refresh").addEventListener("click", () => {
    vscode.postMessage({ type: "refresh" });
  });
  document.getElementById("restart").addEventListener("click", () => {
    vscode.postMessage({ type: "restart" });
  });

  new ResizeObserver(() => {
    const surface = document.getElementById("surface");
    vscode.postMessage({
      type: "viewport",
      width: Math.max(1, surface.clientWidth - 16),
      height: Math.max(1, surface.clientHeight - 16),
      dpi: 96 * window.devicePixelRatio,
    });
  }).observe(document.getElementById("surface"));

  vscode.postMessage({ type: "ready" });
</script>
</body>
</html>`;
}
