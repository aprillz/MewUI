// mew-preview v1 wire contract, editor side (see MewUI/agent/preview-tooling/plan.md 4.3):
// 4-byte LE total length (excluding itself), 4-byte type id, 4-byte JSON length,
// UTF-8 JSON body, optional trailing binary payload (Frame pixels).
package com.aprillz.mewui.preview

import com.google.gson.Gson
import com.google.gson.JsonObject
import java.io.BufferedInputStream
import java.io.EOFException
import java.io.InputStream

object PreviewProtocol {
    const val PROTOCOL_MAJOR = 1
    const val PROTOCOL_MINOR = 2
    const val MAX_MESSAGE_BYTES = 64 * 1024 * 1024

    const val HELLO = 1
    const val SESSION_STARTED = 2
    const val CLIENT_INFO = 3
    const val PREVIEW_TARGETS = 4
    const val SELECT_TARGET = 5
    const val VIEWPORT_CHANGED = 6
    const val FRAME = 7
    const val FRAME_ACK = 8
    const val STATUS = 9
    const val REFRESH_TARGET = 10
    const val SESSION_REJECTED = 11
    const val SET_THEME = 12
    const val POINTER_MOVED = 13
    const val POINTER_PRESSED = 14
    const val POINTER_RELEASED = 15
    const val SCROLL = 16
    const val KEY = 17
    const val TEXT_INPUT = 18

    val gson = Gson()

    fun encode(typeId: Int, body: Any): ByteArray {
        val json = gson.toJson(body).toByteArray(Charsets.UTF_8)
        val message = ByteArray(12 + json.size)
        writeInt(message, 0, json.size + 8)
        writeInt(message, 4, typeId)
        writeInt(message, 8, json.size)
        json.copyInto(message, 12)
        return message
    }

    private fun writeInt(buffer: ByteArray, offset: Int, value: Int) {
        buffer[offset] = value.toByte()
        buffer[offset + 1] = (value shr 8).toByte()
        buffer[offset + 2] = (value shr 16).toByte()
        buffer[offset + 3] = (value shr 24).toByte()
    }
}

class DecodedMessage(val typeId: Int, val json: JsonObject, val binary: ByteArray)

/** Blocking message reader: reads each header, then fills exact-size JSON and payload arrays. */
class MessageReader(stream: InputStream) {
    private val input = BufferedInputStream(stream, 64 * 1024)
    private val header = ByteArray(12)

    /** Returns the next message, or null when the stream ends on a message boundary. */
    fun read(): DecodedMessage? {
        val headerStart = input.readNBytes(header, 0, 4)
        if (headerStart == 0) {
            return null
        }
        if (headerStart < 4) {
            throw EOFException("truncated message header")
        }
        val totalLength = readInt(header, 0)
        require(totalLength in 8..PreviewProtocol.MAX_MESSAGE_BYTES) { "invalid message length $totalLength" }
        readExactly(header, 4, 8)

        val typeId = readInt(header, 4)
        val jsonLength = readInt(header, 8)
        val payloadLength = totalLength - 8
        require(jsonLength in 0..payloadLength) { "invalid json length $jsonLength" }

        val json = ByteArray(jsonLength)
        readExactly(json, 0, jsonLength)
        val binary = ByteArray(payloadLength - jsonLength)
        readExactly(binary, 0, binary.size)

        val body = PreviewProtocol.gson.fromJson(String(json, Charsets.UTF_8), JsonObject::class.java) ?: JsonObject()
        return DecodedMessage(typeId, body, binary)
    }

    private fun readExactly(target: ByteArray, offset: Int, length: Int) {
        if (input.readNBytes(target, offset, length) < length) {
            throw EOFException("truncated message")
        }
    }

    private fun readInt(data: ByteArray, offset: Int): Int =
        (data[offset].toInt() and 0xff) or
            ((data[offset + 1].toInt() and 0xff) shl 8) or
            ((data[offset + 2].toInt() and 0xff) shl 16) or
            ((data[offset + 3].toInt() and 0xff) shl 24)
}

class PreviewTargetInfo(
    val id: String = "",
    val displayName: String = "",
    val kind: String = "",
    val sourcePath: String? = null,
    val sourceLine: Int? = null,
    val available: Boolean = true,
    val reason: String? = null,
)

class FrameHeader(
    val seq: Long = 0,
    val width: Int = 0,
    val height: Int = 0,
    val stride: Int = 0,
    val format: String = "bgra8888",
    val dpiScale: Double = 1.0,
)

class StatusInfo(
    val message: String = "",
    val isBuilding: Boolean = false,
    val hasError: Boolean = false,
    val updateKind: String? = null,
    val exceptionDetail: String? = null,
    val themeMode: String? = null,
)
