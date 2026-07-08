using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

using Aprillz.MewUI.Diagnostics;

namespace Aprillz.MewUI.Platform.Linux.DBus;

/// <summary>
/// Minimal D-Bus connection over Unix domain socket.
/// Supports EXTERNAL authentication and message send/receive.
/// </summary>
internal sealed class DBusConnection : IDisposable
{
    private static readonly EnvDebugLogger Logger = new("MEWUI_IME_DEBUG", "[DBus]");

    // Grown on demand up to MaxMessageSize when a message's declared size exceeds the current capacity.
    private const int INITIAL_RECV_BUFFER_SIZE = 8192;
    private const int MAX_MESSAGE_SIZE = 16 * 1024 * 1024; // sane cap well below the D-Bus spec limit (128 MiB)

    private Socket? _socket;
    private byte[] _recvBuffer = new byte[INITIAL_RECV_BUFFER_SIZE];
    private int _recvBufferUsed;
    private uint _nextSerial = 1;
    private bool _disposed;
    private readonly Queue<DBusMessage> _signalQueue = new();

    public bool IsConnected => _socket?.Connected == true;

    public uint NextSerial() => _nextSerial++;

    /// <summary>
    /// Connects to a D-Bus server at the given address string (unix:path=... or unix:abstract=...).
    /// Returns null on failure.
    /// </summary>
    public static DBusConnection? TryConnect(string address)
    {
        string? socketPath = ParseUnixPath(address);
        if (socketPath == null)
        {
            Logger.Write($"Cannot parse address: {address}");
            return null;
        }

        var conn = new DBusConnection();
        try
        {
            conn._socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            conn._socket.Connect(new UnixDomainSocketEndPoint(socketPath));

            if (!conn.Authenticate())
            {
                Logger.Write($"Auth failed for {socketPath}");
                conn.Dispose();
                return null;
            }

            if (!conn.Hello())
            {
                Logger.Write($"Hello failed for {socketPath}");
                conn.Dispose();
                return null;
            }

            Logger.Write($"Connected to {socketPath}");
            return conn;
        }
        catch (Exception ex)
        {
            Logger.Write($"Connect failed ({socketPath}): {ex.Message}");
            conn.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Connects to the session D-Bus and performs EXTERNAL authentication.
    /// Returns null on failure.
    /// </summary>
    public static DBusConnection? TryConnectSession()
    {
        string? address = GetSessionBusAddress();
        if (address == null)
        {
            Logger.Write("No session bus address found");
            return null;
        }

        // Parse unix:path=... or unix:abstract=...
        string? socketPath = ParseUnixPath(address);
        if (socketPath == null)
        {
            Logger.Write($"Cannot parse bus address: {address}");
            return null;
        }

        var conn = new DBusConnection();
        try
        {
            conn._socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            conn._socket.Connect(new UnixDomainSocketEndPoint(socketPath));

            if (!conn.Authenticate())
            {
                Logger.Write("D-Bus authentication failed");
                conn.Dispose();
                return null;
            }

            // Say Hello to get our unique name
            if (!conn.Hello())
            {
                Logger.Write("D-Bus Hello failed");
                conn.Dispose();
                return null;
            }

            Logger.Write($"Connected to session bus at {socketPath}");
            return conn;
        }
        catch (Exception ex)
        {
            Logger.Write($"Failed to connect: {ex.Message}");
            conn.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Sends raw bytes to the bus.
    /// </summary>
    public bool Send(byte[] data)
    {
        if (_socket == null || !_socket.Connected) return false;
        try
        {
            int sent = 0;
            while (sent < data.Length)
            {
                int n = _socket.Send(data, sent, data.Length - sent, SocketFlags.None);
                if (n <= 0) return false;
                sent += n;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends a method call and waits for the reply (blocking).
    /// Returns null on error or timeout.
    /// </summary>
    public DBusMessage? SendAndWaitReply(byte[] data, uint serial, int timeoutMs = 3000)
    {
        if (!Send(data)) return null;

        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var msg = TryReceiveOne(Math.Max(1, (int)(deadline - Environment.TickCount64)));
            if (msg == null) continue;
            if ((msg.Type == DBusConstants.MethodReturn || msg.Type == DBusConstants.Error)
                && msg.ReplySerial == serial)
            {
                return msg;
            }
            // Queue signals so they aren't lost during the wait
            if (msg.Type == DBusConstants.Signal)
            {
                _signalQueue.Enqueue(msg);
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to receive one message, with a timeout (ms).
    /// Returns null if no complete message is available.
    /// </summary>
    public DBusMessage? TryReceiveOne(int timeoutMs = 0)
    {
        // First try to parse from existing buffer
        var msg = TryParseFromBuffer();
        if (msg != null) return msg;

        if (_socket == null) return null;

        // Try to read more data
        try
        {
            if (timeoutMs > 0)
                _socket.ReceiveTimeout = timeoutMs;
            else
                _socket.ReceiveTimeout = 1;

            if (!EnsureRecvCapacityForPendingMessage()) return null;

            int available = _recvBuffer.Length - _recvBufferUsed;
            if (available <= 0) return null; // buffer full, shouldn't happen

            int n = _socket.Receive(_recvBuffer, _recvBufferUsed, available, SocketFlags.None);
            if (n <= 0) return null;
            _recvBufferUsed += n;
        }
        catch (SocketException)
        {
            return null;
        }

        return TryParseFromBuffer();
    }

    /// <summary>
    /// Non-blocking poll: returns any pending message, or null.
    /// Checks the signal queue first (signals queued during SendAndWaitReply).
    /// </summary>
    public DBusMessage? Poll()
    {
        // Drain queued signals first
        if (_signalQueue.Count > 0)
            return _signalQueue.Dequeue();

        // Parse from buffer
        var msg = TryParseFromBuffer();
        if (msg != null) return msg;

        if (_socket == null) return null;

        try
        {
            if (_socket.Available <= 0) return null;

            if (!EnsureRecvCapacityForPendingMessage()) return null;

            int available = _recvBuffer.Length - _recvBufferUsed;
            if (available <= 0) return null;

            _socket.ReceiveTimeout = 1;
            int n = _socket.Receive(_recvBuffer, _recvBufferUsed, available, SocketFlags.None);
            if (n <= 0) return null;
            _recvBufferUsed += n;
        }
        catch (SocketException)
        {
            return null;
        }

        return TryParseFromBuffer();
    }

    /// <summary>
    /// Grows the receive buffer to the pending message's declared total size (fixed header carries
    /// body length + header fields length) so messages larger than the buffer cannot stall the read loop.
    /// Returns false when the pending message is oversized or malformed; the connection is dropped then,
    /// since the stream cannot be resynchronized without parsing the message.
    /// </summary>
    private bool EnsureRecvCapacityForPendingMessage()
    {
        // Header not complete yet; the initial buffer always holds a full 16-byte header.
        if (_recvBufferUsed < 16) return true;

        if (_recvBuffer[0] != DBusConstants.LittleEndian)
        {
            Logger.Write("Dropping connection: unsupported endianness in message header");
            Dispose();
            return false;
        }

        uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(_recvBuffer.AsSpan(4));
        uint headerFieldsLength = BinaryPrimitives.ReadUInt32LittleEndian(_recvBuffer.AsSpan(12));

        // Same layout as DBusReader.TryParse: fixed header + fields array padded to 8, then the body.
        long totalSize = ((12L + 4 + headerFieldsLength + 7) & ~7L) + bodyLength;
        if (totalSize > MAX_MESSAGE_SIZE)
        {
            Logger.Write($"Dropping connection: message size {totalSize} exceeds cap {MAX_MESSAGE_SIZE}");
            Dispose();
            return false;
        }

        if (totalSize > _recvBuffer.Length)
        {
            Array.Resize(ref _recvBuffer, (int)totalSize);
        }

        return true;
    }

    private DBusMessage? TryParseFromBuffer()
    {
        if (_recvBufferUsed < 16) return null;

        var msg = DBusReader.TryParse(_recvBuffer, 0, _recvBufferUsed, out int consumed);
        if (msg == null) return null;

        // Shift remaining data
        int remaining = _recvBufferUsed - consumed;
        if (remaining > 0)
            Buffer.BlockCopy(_recvBuffer, consumed, _recvBuffer, 0, remaining);
        _recvBufferUsed = remaining;

        return msg;
    }

    private bool Authenticate()
    {
        if (_socket == null) return false;

        try
        {
            // Send NUL byte (credential byte)
            _socket.Send(new byte[] { 0 });

            // EXTERNAL auth with UID
            string uid = GetUid();
            string hexUid = BitConverter.ToString(Encoding.ASCII.GetBytes(uid)).Replace("-", "");
            string authCmd = $"AUTH EXTERNAL {hexUid}\r\n";
            _socket.Send(Encoding.ASCII.GetBytes(authCmd));

            // Read the full CRLF-terminated response line; it may arrive split across packets.
            _socket.ReceiveTimeout = 3000;
            byte[] buf = new byte[256];
            var responseBuilder = new StringBuilder();
            string response;
            while (true)
            {
                int bytesRead = _socket.Receive(buf);
                if (bytesRead <= 0)
                {
                    Logger.Write("Auth failed: connection closed before response line");
                    return false;
                }

                responseBuilder.Append(Encoding.ASCII.GetString(buf, 0, bytesRead));
                response = responseBuilder.ToString();
                if (response.Contains("\r\n", StringComparison.Ordinal))
                {
                    break;
                }

                if (responseBuilder.Length > 4096)
                {
                    Logger.Write("Auth failed: response line too long");
                    return false;
                }
            }

            // Anything but OK (typically REJECTED with the supported mechanisms) fails gracefully.
            if (!response.StartsWith("OK ", StringComparison.Ordinal))
            {
                Logger.Write($"Auth response: {response.TrimEnd()}");
                return false;
            }

            // Begin
            _socket.Send(Encoding.ASCII.GetBytes("BEGIN\r\n"));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Write($"Auth error: {ex.Message}");
            return false;
        }
    }

    private bool Hello()
    {
        uint serial = NextSerial();
        var msg = DBusWriter.BuildMethodCall(
            serial,
            DBusConstants.BusName,
            DBusConstants.BusPath,
            DBusConstants.BusInterface,
            "Hello");

        var reply = SendAndWaitReply(msg, serial);
        return reply != null && reply.Type == DBusConstants.MethodReturn;
    }

    private static string GetUid()
    {
        try
        {
            // getuid() via /proc
            string status = System.IO.File.ReadAllText("/proc/self/status");
            foreach (var line in status.Split('\n'))
            {
                if (line.StartsWith("Uid:", StringComparison.Ordinal))
                {
                    var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) return parts[1].Trim();
                }
            }
        }
        catch { }

        // fallback
        return "1000";
    }

    private static string? GetSessionBusAddress()
    {
        // Try DBUS_SESSION_BUS_ADDRESS first
        string? addr = null;
        try { addr = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"); } catch { }
        if (!string.IsNullOrEmpty(addr)) return addr;

        // Try XDG_RUNTIME_DIR
        string? xdgRuntime = null;
        try { xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"); } catch { }
        if (!string.IsNullOrEmpty(xdgRuntime))
        {
            string busPath = Path.Combine(xdgRuntime, "bus");
            if (File.Exists(busPath))
                return $"unix:path={busPath}";
        }

        return null;
    }

    private static string? ParseUnixPath(string address)
    {
        // Format: unix:path=/run/user/1000/bus or unix:abstract=/tmp/dbus-xxx
        foreach (var part in address.Split(';'))
        {
            if (!part.StartsWith("unix:", StringComparison.Ordinal)) continue;

            var kvPairs = part[5..].Split(',');
            foreach (var kv in kvPairs)
            {
                if (kv.StartsWith("path=", StringComparison.Ordinal))
                    return kv[5..];
                if (kv.StartsWith("abstract=", StringComparison.Ordinal))
                    return "\0" + kv[9..]; // abstract socket
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _socket?.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket?.Dispose(); } catch { }
        _socket = null;
    }
}
