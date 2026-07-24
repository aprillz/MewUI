// mew-preview v1 wire contract (see MewUI/agent/preview-tooling/plan.md 4.3):
// 4-byte LE total length (excluding itself), 4-byte type id, 4-byte JSON length,
// UTF-8 JSON body, optional trailing binary payload (Frame pixels).

import { Socket } from "net";

export const PROTOCOL_MAJOR = 1;
export const PROTOCOL_MINOR = 1;
export const MAX_MESSAGE_BYTES = 64 * 1024 * 1024;

export const enum MessageType {
    Hello = 1,
    SessionStarted = 2,
    ClientInfo = 3,
    PreviewTargets = 4,
    SelectTarget = 5,
    ViewportChanged = 6,
    Frame = 7,
    FrameAck = 8,
    Status = 9,
    RefreshTarget = 10,
    SessionRejected = 11,
    SetTheme = 12,
}

export interface PreviewTargetInfo {
    id: string;
    displayName: string;
    kind: string;
    sourcePath?: string | null;
    sourceLine?: number | null;
    available?: boolean;
    reason?: string | null;
}

export interface FrameHeader {
    seq: number;
    width: number;
    height: number;
    stride: number;
    format: string;
    dpiScale: number;
}

export interface StatusInfo {
    message: string;
    isBuilding: boolean;
    hasError: boolean;
    updateKind?: string | null;
    exceptionDetail?: string | null;
    themeMode?: string | null;
}

export interface DecodedMessage {
    typeId: number;
    json: unknown;
    binary: Buffer;
}

export function sendMessage(socket: Socket, typeId: number, body: unknown): void {
    const json = Buffer.from(JSON.stringify(body), "utf8");
    const header = Buffer.allocUnsafe(12);
    header.writeInt32LE(json.length + 8, 0);
    header.writeInt32LE(typeId, 4);
    header.writeInt32LE(json.length, 8);
    socket.write(Buffer.concat([header, json]));
}

/** Incremental frame decoder: feed socket chunks, emits complete messages via the callback. */
export class MessageDecoder {
    private buffer: Buffer = Buffer.alloc(0);

    constructor(private readonly onMessage: (message: DecodedMessage) => void) {}

    public push(chunk: Buffer): void {
        this.buffer = this.buffer.length === 0 ? chunk : Buffer.concat([this.buffer, chunk]);

        while (this.buffer.length >= 12) {
            const totalLength = this.buffer.readInt32LE(0);
            if (totalLength < 8 || totalLength > MAX_MESSAGE_BYTES) {
                throw new Error(`invalid message length ${totalLength}`);
            }
            if (this.buffer.length < 4 + totalLength) {
                return;
            }

            const typeId = this.buffer.readInt32LE(4);
            const jsonLength = this.buffer.readInt32LE(8);
            const payloadLength = totalLength - 8;
            if (jsonLength < 0 || jsonLength > payloadLength) {
                throw new Error(`invalid json length ${jsonLength}`);
            }

            const jsonText = this.buffer.subarray(12, 12 + jsonLength).toString("utf8");
            const binary = Buffer.from(this.buffer.subarray(12 + jsonLength, 4 + totalLength));
            this.buffer = this.buffer.subarray(4 + totalLength);

            this.onMessage({ typeId, json: JSON.parse(jsonText), binary });
        }
    }
}
