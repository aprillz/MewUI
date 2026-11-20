// mew-preview v1 wire contract, editor side (see MewUI/agent/preview-tooling/plan.md 4.3):
// 4-byte LE total length (excluding itself), 4-byte type id, 4-byte JSON length,
// UTF-8 JSON body, optional trailing binary payload (Frame pixels).

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Aprillz.MewUI.VisualStudio.Session
{
    internal static class PreviewProtocol
    {
        internal const int PROTOCOL_MAJOR = 1;
        internal const int PROTOCOL_MINOR = 2;
        internal const int MAX_MESSAGE_BYTES = 64 * 1024 * 1024;

        internal const int HELLO = 1;
        internal const int SESSION_STARTED = 2;
        internal const int CLIENT_INFO = 3;
        internal const int PREVIEW_TARGETS = 4;
        internal const int SELECT_TARGET = 5;
        internal const int VIEWPORT_CHANGED = 6;
        internal const int FRAME = 7;
        internal const int FRAME_ACK = 8;
        internal const int STATUS = 9;
        internal const int REFRESH_TARGET = 10;
        internal const int SESSION_REJECTED = 11;
        internal const int SET_THEME = 12;
        internal const int POINTER_MOVED = 13;
        internal const int POINTER_PRESSED = 14;
        internal const int POINTER_RELEASED = 15;
        internal const int SCROLL = 16;
        internal const int KEY = 17;
        internal const int TEXT_INPUT = 18;

        internal static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore,
        };

        internal static byte[] Encode(int typeId, object body)
        {
            byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, JsonSettings));
            byte[] message = new byte[12 + json.Length];
            WriteInt32(message, 0, json.Length + 8);
            WriteInt32(message, 4, typeId);
            WriteInt32(message, 8, json.Length);
            Buffer.BlockCopy(json, 0, message, 12, json.Length);
            return message;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }

    internal sealed class DecodedMessage
    {
        public int TypeId;
        public JObject Json;
        public byte[] Binary;
    }

    /// <summary>Incremental frame decoder: feed socket chunks, emits complete messages via the callback.</summary>
    internal sealed class MessageDecoder
    {
        // Room for one more socket read past the end of a pending message.
        private const int READ_SLACK_BYTES = 64 * 1024;
        // About one 4K BGRA frame; a buffer grown past this is released once it is no longer needed.
        private const int RETAINED_BUFFER_BYTES = 32 * 1024 * 1024;

        private readonly Action<DecodedMessage> _onMessage;
        private readonly MemoryStream _buffer = new MemoryStream();
        private int _consumed;

        public MessageDecoder(Action<DecodedMessage> onMessage)
        {
            _onMessage = onMessage;
        }

        public void Push(byte[] chunk, int count)
        {
            _buffer.Write(chunk, 0, count);
            byte[] data = _buffer.GetBuffer();
            int available = (int)_buffer.Length;
            int pendingLength = 0;

            while (available - _consumed >= 12)
            {
                int totalLength = ReadInt32(data, _consumed);
                if (totalLength < 8 || totalLength > PreviewProtocol.MAX_MESSAGE_BYTES)
                {
                    throw new InvalidDataException($"invalid message length {totalLength}");
                }
                if (available - _consumed < 4 + totalLength)
                {
                    pendingLength = 4 + totalLength;
                    break;
                }

                int typeId = ReadInt32(data, _consumed + 4);
                int jsonLength = ReadInt32(data, _consumed + 8);
                int payloadLength = totalLength - 8;
                if (jsonLength < 0 || jsonLength > payloadLength)
                {
                    throw new InvalidDataException($"invalid json length {jsonLength}");
                }

                string jsonText = Encoding.UTF8.GetString(data, _consumed + 12, jsonLength);
                byte[] binary = new byte[payloadLength - jsonLength];
                Buffer.BlockCopy(data, _consumed + 12 + jsonLength, binary, 0, binary.Length);
                _consumed += 4 + totalLength;

                _onMessage(new DecodedMessage { TypeId = typeId, Json = JObject.Parse(jsonText), Binary = binary });
            }

            if (_consumed > 0)
            {
                int remaining = available - _consumed;
                if (remaining > 0)
                {
                    Buffer.BlockCopy(data, _consumed, data, 0, remaining);
                }
                _buffer.SetLength(remaining);
                _consumed = 0;
            }

            int required = pendingLength + READ_SLACK_BYTES;
            if (pendingLength > 0 && _buffer.Capacity < required)
            {
                // Sized once for the pending message: doubling growth would overshoot to twice its size.
                _buffer.Capacity = required;
            }
            else if (_buffer.Capacity > RETAINED_BUFFER_BYTES && _buffer.Capacity > required)
            {
                _buffer.Capacity = required;
            }
        }

        private static int ReadInt32(byte[] buffer, int offset) =>
            buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24);
    }

    internal sealed class PreviewTargetInfo
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string SourcePath { get; set; }
        public int? SourceLine { get; set; }
        public bool Available { get; set; } = true;
        public string Reason { get; set; }
    }

    internal sealed class FrameHeader
    {
        public long Seq { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public string Format { get; set; } = "bgra8888";
        public double DpiScale { get; set; }
    }

    internal sealed class StatusInfo
    {
        public string Message { get; set; } = string.Empty;
        public bool IsBuilding { get; set; }
        public bool HasError { get; set; }
        public string UpdateKind { get; set; }
        public string ExceptionDetail { get; set; }
        public string ThemeMode { get; set; }
    }
}
