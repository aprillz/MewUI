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
    // The IDE listens for the whole session, so a listener that stays unreachable this long means the
    // IDE (or the session in it) is gone and the app must not outlive it.
    private const int UNREACHABLE_SHUTDOWN_MS = 15_000;

    private enum HandshakeResult
    {
        Accepted,
        Rejected,
        Dropped,
    }

    private readonly object _writeGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<int, JsonDocument> _onMessage;
    private readonly Action _onConnected;
    private readonly Action _onSessionLost;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private Thread? _thread;

    public PreviewChannel(Action<int, JsonDocument> onMessage, Action onConnected, Action onSessionLost)
    {
        _onMessage = onMessage;
        _onConnected = onConnected;
        _onSessionLost = onSessionLost;
    }

    public bool IsConnected => _stream != null;

    public void Start()
    {
        _thread = new Thread(ConnectionLoop) { IsBackground = true, Name = "MewUI Preview Channel" };
        _thread.Start();
    }

    public void Dispose()
    {
        // The token source is not disposed: the connection thread may still be waiting on its handle.
        _stop.Cancel();
        CloseConnection();
    }

    private void ConnectionLoop()
    {
        var stopHandle = _stop.Token.WaitHandle;
        long lastContact = Environment.TickCount64;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = new TcpClient { NoDelay = true };
                client.Connect(PreviewEnvironment.Endpoint!);
                lastContact = Environment.TickCount64;
                var stream = client.GetStream();

                var handshake = PerformHandshake(stream);
                if (handshake == HandshakeResult.Rejected)
                {
                    client.Dispose();
                    _onSessionLost();
                    return;
                }
                if (handshake == HandshakeResult.Dropped)
                {
                    client.Dispose();
                }
                else
                {
                    _client = client;
                    _stream = stream;
                    _onConnected();
                    ReadLoop(stream);
                    lastContact = Environment.TickCount64;
                }
            }
            catch
            {
                // Connection refused / dropped: fall through to the retry delay below.
            }
            finally
            {
                CloseConnection();
            }

            if (Environment.TickCount64 - lastContact >= UNREACHABLE_SHUTDOWN_MS)
            {
                _onSessionLost();
                return;
            }

            if (stopHandle.WaitOne(RECONNECT_DELAY_MS))
            {
                return;
            }
        }
    }

    private HandshakeResult PerformHandshake(NetworkStream stream)
    {
        if (!TryReadMessage(stream, out int typeId, out var json, out _))
        {
            return HandshakeResult.Dropped;
        }

        using (json)
        {
            if (typeId != PreviewProtocol.HELLO)
            {
                // A client that wrote before its Hello raced itself; a fresh connection starts clean.
                return HandshakeResult.Dropped;
            }

            var hello = json.Deserialize(PreviewJsonContext.Default.HelloMessage);
            if (hello == null || hello.ProtocolMajor != PreviewProtocol.PROTOCOL_MAJOR || !TokenMatches(hello.Token))
            {
                var reason = hello == null || TokenMatches(hello.Token)
                    ? $"unsupported protocol major (host={PreviewProtocol.PROTOCOL_MAJOR})"
                    : "invalid token";
                WriteMessage(stream, PreviewProtocol.SESSION_REJECTED,
                    new SessionRejectedMessage { Reason = reason }, PreviewJsonContext.Default.SessionRejectedMessage);
                return HandshakeResult.Rejected;
            }
        }

        WriteMessage(stream, PreviewProtocol.SESSION_STARTED, new SessionStartedMessage
        {
            SessionId = PreviewEnvironment.SessionId,
            FrameworkVersion = typeof(Application).Assembly.GetName().Version?.ToString() ?? "unknown",
            ProtocolMajor = PreviewProtocol.PROTOCOL_MAJOR,
            ProtocolMinor = PreviewProtocol.PROTOCOL_MINOR,
        }, PreviewJsonContext.Default.SessionStartedMessage);
        return HandshakeResult.Accepted;
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
