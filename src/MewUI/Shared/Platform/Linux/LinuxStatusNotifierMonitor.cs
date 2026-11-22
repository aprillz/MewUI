using Aprillz.MewUI.Diagnostics;

using Tmds.DBus.Protocol;

namespace Aprillz.MewUI.Platform.Linux;

/// <summary>
/// Follows the session's status notifier watcher: whether one runs, whether a panel hosts its items, and when a new
/// one appears so that items register again.
/// </summary>
internal sealed class LinuxStatusNotifierMonitor
{
    internal const string WATCHER_NAME = "org.kde.StatusNotifierWatcher";
    internal const string WATCHER_PATH = "/StatusNotifierWatcher";
    private const string BUS_NAME = "org.freedesktop.DBus";
    private const string BUS_PATH = "/org/freedesktop/DBus";
    private const string PROPERTIES = "org.freedesktop.DBus.Properties";
    // The first answer is awaited on the UI thread, so an unresponsive bus must not hold it for long.
    private const int FIRST_ANSWER_MILLISECONDS = 500;

    private static readonly EnvDebugLogger _logger = new("MEWUI_TRAY_DEBUG", "[Tray]");
    private static readonly Lazy<LinuxStatusNotifierMonitor> _instance = new(() => new LinuxStatusNotifierMonitor());

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Task _started;
    private DBusConnection? _connection;
    private string? _watcherOwner;
    private volatile bool _hasHost;

    private LinuxStatusNotifierMonitor() => _started = Task.Run(StartAsync);

    /// <summary>Raised on a pool thread when a watcher takes the name.</summary>
    public event Action? WatcherAppeared;

    public static LinuxStatusNotifierMonitor Instance => _instance.Value;

    /// <summary>Whether a watcher runs and a panel hosts its items.</summary>
    public bool IsAvailable
    {
        get
        {
            _started.Wait(FIRST_ANSWER_MILLISECONDS);
            return _hasHost;
        }
    }

    private async Task StartAsync()
    {
        string? address = DBusAddress.Session;
        if (address == null)
        {
            return;
        }

        try
        {
            var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);
            _connection = connection;

            var ownerRule = new MatchRule
            {
                Type = MessageType.Signal,
                Sender = BUS_NAME,
                Path = BUS_PATH,
                Interface = BUS_NAME,
                Member = "NameOwnerChanged",
                Arg0 = WATCHER_NAME,
            };
            await connection.AddMatchAsync(ownerRule, notification => _ = RefreshAsync(), false, ObserverFlags.None, null).ConfigureAwait(false);

            foreach (string member in new[] { "StatusNotifierHostRegistered", "StatusNotifierHostUnregistered" })
            {
                var hostRule = new MatchRule
                {
                    Type = MessageType.Signal,
                    Path = WATCHER_PATH,
                    Interface = WATCHER_NAME,
                    Member = member,
                };
                await connection.AddMatchAsync(hostRule, notification => _ = RefreshAsync(), false, ObserverFlags.None, null).ConfigureAwait(false);
            }

            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Write($"not monitoring: {exception.Message}");
        }
    }

    private async Task RefreshAsync()
    {
        var connection = _connection;
        if (connection == null)
        {
            return;
        }

        await _refreshGate.WaitAsync().ConfigureAwait(false);
        bool appeared = false;
        try
        {
            string? owner = await TryGetOwnerAsync(connection).ConfigureAwait(false);
            appeared = owner != null && owner != _watcherOwner;
            _watcherOwner = owner;
            _hasHost = owner != null && await TryGetHostRegisteredAsync(connection).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Write($"refresh failed: {exception.Message}");
        }
        finally
        {
            _refreshGate.Release();
        }

        if (appeared)
        {
            WatcherAppeared?.Invoke();
        }
    }

    private static async Task<string?> TryGetOwnerAsync(DBusConnection connection)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(BUS_NAME, BUS_PATH, BUS_NAME, "GetNameOwner", "s", MessageFlags.None);
            writer.WriteString(WATCHER_NAME);
            call = writer.CreateMessage();
        }

        try
        {
            return await connection.CallMethodAsync(call, static (message, _) => message.GetBodyReader().ReadString(), null).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            return null;
        }
    }

    private static async Task<bool> TryGetHostRegisteredAsync(DBusConnection connection)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(WATCHER_NAME, WATCHER_PATH, PROPERTIES, "Get", "ss", MessageFlags.None);
            writer.WriteString(WATCHER_NAME);
            writer.WriteString("IsStatusNotifierHostRegistered");
            call = writer.CreateMessage();
        }

        try
        {
            return await connection.CallMethodAsync(call, static (message, _) =>
            {
                var reader = message.GetBodyReader();
                reader.ReadSignature();
                return reader.ReadBool();
            }, null).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            return false;
        }
    }
}
