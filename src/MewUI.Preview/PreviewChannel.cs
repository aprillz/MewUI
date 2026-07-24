using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// Maintains the TCP connection to the IDE extension: connects (with retry) to the endpoint from
/// the preview environment, performs the Hello handshake, then pumps inbound messages to a
/// callback and serializes outbound messages. All callbacks arrive on background threads.
/// </summary>
internal sealed class PreviewChannel : IDisposable
{
    private const int RECONNECT_DELAY_MS = 1000;

    private readonly object _writeGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<int, JsonDocument> _onMessage;
    private readonly Action _onConnected;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private Thread? _thread;

    public PreviewChannel(Action<int, JsonDocument> onMessage, Action onConnected)
    {
        _onMessage = onMessage;
        _onConnected = onConnected;
    }

    public bool IsConnected => _stream != null;

    public void Start()
    {
        _thread = new Thread(ConnectionLoop) { IsBackground = true, Name = "MewUI Preview Channel" };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        CloseConnection();
        _stop.Dispose();
    }

    private void ConnectionLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = new TcpClient { NoDelay = true };
                client.Connect(PreviewEnvironment.Endpoint!);
                var stream = client.GetStream();

                if (!PerformHandshake(stream))
                {
                    client.Dispose();
                    return;
                }

                _client = client;
                _stream = stream;
                _onConnected();
                ReadLoop(stream);
            }
            catch
            {
                // Connection refused / dropped: fall through to the retry delay below.
            }
            finally
            {
                CloseConnection();
            }

            if (_stop.Token.WaitHandle.WaitOne(RECONNECT_DELAY_MS))
            {
                return;
            }
        }
    }

    private bool PerformHandshake(NetworkStream stream)
    {
        if (!TryReadMessage(stream, out int typeId, out var json, out _) || typeId != PreviewProtocol.HELLO)
        {
            return false;
        }

        using (json)
        {
            var hello = json.Deserialize(PreviewJsonContext.Default.HelloMessage);
            if (hello == null || hello.ProtocolMajor != PreviewProtocol.PROTOCOL_MAJOR || !TokenMatches(hello.Token))
            {
                var reason = hello == null || TokenMatches(hello.Token)
                    ? $"unsupported protocol major (host={PreviewProtocol.PROTOCOL_MAJOR})"
                    : "invalid token";
                WriteMessage(stream, PreviewProtocol.SESSION_REJECTED,
                    new SessionRejectedMessage { Reason = reason }, PreviewJsonContext.Default.SessionRejectedMessage);
                return false;
            }
        }

        WriteMessage(stream, PreviewProtocol.SESSION_STARTED, new SessionStartedMessage
        {
            SessionId = PreviewEnvironment.SessionId,
            FrameworkVersion = typeof(Application).Assembly.GetName().Version?.ToString() ?? "unknown",
            ProtocolMajor = PreviewProtocol.PROTOCOL_MAJOR,
            ProtocolMinor = PreviewProtocol.PROTOCOL_MINOR,
        }, PreviewJsonContext.Default.SessionStartedMessage);
        return true;
    }

    private static bool TokenMatches(string presented)
    {
        var expected = Encoding.UTF8.GetBytes(PreviewEnvironment.Token);
        var actual = Encoding.UTF8.GetBytes(presented);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private void ReadLoop(NetworkStream stream)
    {
        while (!_stop.IsCancellationRequested)
        {
            if (!TryReadMessage(stream, out int typeId, out var json, out _))
            {
                return;
            }

            try
            {
                _onMessage(typeId, json);
            }
            catch
            {
                json.Dispose();
            }
        }
    }

    private static bool TryReadMessage(NetworkStream stream, out int typeId, out JsonDocument json, out byte[] binary)
    {
        typeId = 0;
        json = null!;
        binary = [];

        Span<byte> header = stackalloc byte[12];
        if (!FillBuffer(stream, header))
        {
            return false;
        }

        int totalLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        typeId = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        // Total length counts everything after the length field itself (typeId + jsonLen + payloads).
        int payloadLength = totalLength - 8;
        if (payloadLength < 0 || jsonLength < 0 || jsonLength > payloadLength
            || totalLength > PreviewProtocol.MAX_MESSAGE_BYTES)
        {
            return false;
        }

        var payload = new byte[payloadLength];
        if (!FillBuffer(stream, payload))
        {
            return false;
        }

        json = JsonDocument.Parse(payload.AsMemory(0, jsonLength));
        binary = jsonLength == payloadLength ? [] : payload[jsonLength..];
        return true;
    }

    private static bool FillBuffer(NetworkStream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read <= 0)
            {
                return false;
            }
            total += read;
        }

        return true;
    }

    public void Send<TMessage>(int typeId, TMessage message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMessage> typeInfo)
    {
        var stream = _stream;
        if (stream == null)
        {
            return;
        }

        try
        {
            WriteMessage(stream, typeId, message, typeInfo);
        }
        catch
        {
            CloseConnection();
        }
    }

    public void SendFrame(FrameMessage header, ReadOnlySpan<byte> pixels)
    {
        var stream = _stream;
        if (stream == null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(header, PreviewJsonContext.Default.FrameMessage);
            lock (_writeGate)
            {
                WriteEnvelopeHeader(stream, PreviewProtocol.FRAME, json.Length, json.Length + pixels.Length);
                stream.Write(json);
                stream.Write(pixels);
                stream.Flush();
            }
        }
        catch
        {
            CloseConnection();
        }
    }

    private void WriteMessage<TMessage>(NetworkStream stream, int typeId, TMessage message,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMessage> typeInfo)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, typeInfo);
        lock (_writeGate)
        {
            WriteEnvelopeHeader(stream, typeId, json.Length, json.Length);
            stream.Write(json);
            stream.Flush();
        }
    }

    private static void WriteEnvelopeHeader(NetworkStream stream, int typeId, int jsonLength, int payloadLength)
    {
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(header, payloadLength + 8);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], typeId);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], jsonLength);
        stream.Write(header);
    }

    private void CloseConnection()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        var client = Interlocked.Exchange(ref _client, null);
        stream?.Dispose();
        client?.Dispose();
    }
}
