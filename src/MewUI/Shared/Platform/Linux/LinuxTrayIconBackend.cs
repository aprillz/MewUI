using System.Runtime.CompilerServices;
using System.Text;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Input;

using Tmds.DBus.Protocol;

namespace Aprillz.MewUI.Platform.Linux;

/// <summary>
/// A status notifier item with a D-Bus menu. Each icon has its own connection because the item's path is fixed and its
/// bus name identifies it to the watcher; closing the connection removes the item. Requests arrive on pool threads and
/// are answered on the UI thread.
/// </summary>
internal sealed class LinuxTrayIconBackend : ITrayIconBackend
{
    private const string ITEM_PATH = "/StatusNotifierItem";
    private const string MENU_PATH = "/MenuBar";
    private const string NO_MENU_PATH = "/NO_DBUSMENU";
    private const string ITEM_INTERFACE = "org.kde.StatusNotifierItem";
    private const string MENU_INTERFACE = "com.canonical.dbusmenu";
    private const string PROPERTIES = "org.freedesktop.DBus.Properties";
    private const string BUS_NAME = "org.freedesktop.DBus";
    private const string BUS_PATH = "/org/freedesktop/DBus";
    private const string LAYOUT_SIGNATURE = "(ia{sv}av)";
    private const string FAILED_ERROR = "org.freedesktop.DBus.Error.Failed";
    private const string INVALID_ARGS_ERROR = "org.freedesktop.DBus.Error.InvalidArgs";
    private const string UNKNOWN_PROPERTY_ERROR = "org.freedesktop.DBus.Error.UnknownProperty";
    private const uint NAME_DO_NOT_QUEUE = 4;
    private const uint NAME_PRIMARY_OWNER = 1;
    private const int ROOT_ID = 0;
    private const int MENU_VERSION = 3;
    private const double MENU_ICON_SIZE = 16;
    private const double STANDARD_DPI = 96;
    // Long enough for a busy UI thread; a hung one fails the panel's call instead of holding it.
    private const int TIMEOUT_MILLISECONDS = 10_000;

    private static readonly int[] _iconSizes = [16, 22, 24, 32, 48, 64];
    private static readonly string[] _itemProperties =
    [
        "Category", "Id", "Title", "Status", "WindowId", "IconName", "IconPixmap", "OverlayIconName", "OverlayIconPixmap",
        "AttentionIconName", "AttentionIconPixmap", "AttentionMovieName", "ToolTip", "ItemIsMenu", "Menu",
    ];
    private static readonly string[] _menuProperties = ["Version", "TextDirection", "Status", "IconThemePath"];
    private static readonly EnvDebugLogger _logger = new("MEWUI_TRAY_DEBUG", "[Tray]");
    private static int _lastInstance;

    private readonly TrayIcon _trayIcon;
    private readonly object _connectionGate = new();
    private readonly Dictionary<int, MenuNode> _nodes = new();
    private readonly List<MenuNode> _rootNodes = new();
    private List<(int Width, int Height, byte[] Argb)> _pixmaps = new();
    private NativeMenuSnapshot? _snapshot;
    private DBusConnection? _connection;
    private string? _busName;
    private string? _menuSignature;
    private uint _menuRevision;
    private int _lastNodeId;
    private int _generation;
    private bool _publishedMenu;
    private volatile bool _disposed;

    public LinuxTrayIconBackend(TrayIcon trayIcon)
    {
        _trayIcon = trayIcon;
        _pixmaps = ReadPixmaps();
        _publishedMenu = trayIcon.ContextMenu != null;
        LinuxStatusNotifierMonitor.Instance.WatcherAppeared += OnWatcherAppeared;
        _ = ConnectAsync(_generation);
    }

    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        _pixmaps = ReadPixmaps();
        bool hasMenu = _trayIcon.ContextMenu != null;
        if (hasMenu != _publishedMenu)
        {
            // Panels read the menu's path once, when the item registers.
            _publishedMenu = hasMenu;
            Reconnect();
            return;
        }

        Signal(ITEM_PATH, ITEM_INTERFACE, "NewIcon");
        Signal(ITEM_PATH, ITEM_INTERFACE, "NewToolTip");
        Signal(ITEM_PATH, ITEM_INTERFACE, "NewTitle");
        if (hasMenu && RefreshMenu(raiseOpening: false))
        {
            SignalLayoutUpdated();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LinuxStatusNotifierMonitor.Instance.WatcherAppeared -= OnWatcherAppeared;
        CloseConnection();
        _nodes.Clear();
        _rootNodes.Clear();
        _snapshot = null;
    }

    private void Reconnect()
    {
        CloseConnection();
        int generation;
        lock (_connectionGate)
        {
            generation = ++_generation;
        }

        _ = ConnectAsync(generation);
    }

    private void CloseConnection()
    {
        DBusConnection? connection;
        lock (_connectionGate)
        {
            connection = _connection;
            _connection = null;
            _busName = null;
            _generation++;
        }

        connection?.Dispose();
    }

    private async Task ConnectAsync(int generation)
    {
        string? address = DBusAddress.Session;
        if (address == null)
        {
            return;
        }

        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            connection.AddMethodHandler(new PathHandler(this, ITEM_PATH));
            connection.AddMethodHandler(new PathHandler(this, MENU_PATH));

            string busName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-{Interlocked.Increment(ref _lastInstance)}";
            if (!await RequestNameAsync(connection, busName).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"The bus did not give the name {busName}.");
            }

            lock (_connectionGate)
            {
                if (_disposed || generation != _generation)
                {
                    connection.Dispose();
                    return;
                }

                _connection = connection;
                _busName = busName;
            }

            await RegisterAsync(connection, busName).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Write($"item not published: {exception.Message}");
            connection.Dispose();
        }
    }

    private void OnWatcherAppeared()
    {
        DBusConnection? connection;
        string? busName;
        lock (_connectionGate)
        {
            connection = _connection;
            busName = _busName;
        }

        if (connection != null && busName != null)
        {
            _ = RegisterAsync(connection, busName);
        }
    }

    private static async Task<bool> RequestNameAsync(DBusConnection connection, string busName)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(BUS_NAME, BUS_PATH, BUS_NAME, "RequestName", "su", MessageFlags.None);
            writer.WriteString(busName);
            writer.WriteUInt32(NAME_DO_NOT_QUEUE);
            call = writer.CreateMessage();
        }

        uint result = await connection.CallMethodAsync(call, static (message, _) => message.GetBodyReader().ReadUInt32(), null).ConfigureAwait(false);
        return result == NAME_PRIMARY_OWNER;
    }

    /// <summary>Announces the item; with no watcher running, the monitor calls this again when one appears.</summary>
    private static async Task RegisterAsync(DBusConnection connection, string busName)
    {
        try
        {
            MessageBuffer call;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(LinuxStatusNotifierMonitor.WATCHER_NAME, LinuxStatusNotifierMonitor.WATCHER_PATH,
                    LinuxStatusNotifierMonitor.WATCHER_NAME, "RegisterStatusNotifierItem", "s", MessageFlags.None);
                writer.WriteString(busName);
                call = writer.CreateMessage();
            }

            await connection.CallMethodAsync(call).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Write($"not registered: {exception.Message}");
        }
    }

    private async ValueTask HandleAsync(MethodContext context)
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (_disposed || dispatcher == null)
        {
            context.ReplyError(FAILED_ERROR, "The icon is gone.");
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            try
            {
                if (_disposed)
                {
                    context.ReplyError(FAILED_ERROR, "The icon is gone.");
                }
                else
                {
                    Dispatch(context);
                }
            }
            catch (Exception exception)
            {
                if (!context.ReplySent)
                {
                    context.ReplyError(FAILED_ERROR, exception.Message);
                }

                Application.RouteLifecycleException(exception);
            }
            finally
            {
                done.TrySetResult();
            }
        });

        try
        {
            await done.Task.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MILLISECONDS)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (!context.ReplySent)
            {
                context.ReplyError(FAILED_ERROR, "The UI thread did not answer in time.");
            }
        }
    }

    private void Dispatch(MethodContext context)
    {
        var request = context.Request;
        string path = request.PathAsString ?? string.Empty;
        string @interface = request.InterfaceAsString ?? string.Empty;
        string member = request.MemberAsString ?? string.Empty;
        var reader = request.GetBodyReader();

        if (@interface == PROPERTIES)
        {
            HandleProperties(context, path, member, ref reader);
        }
        else if (path == ITEM_PATH && @interface == ITEM_INTERFACE)
        {
            HandleItem(context, member, ref reader);
        }
        else if (path == MENU_PATH && @interface == MENU_INTERFACE)
        {
            HandleMenu(context, member, ref reader);
        }
        else
        {
            context.ReplyUnknownMethodError();
        }
    }

    private void HandleItem(MethodContext context, string member, ref Reader reader)
    {
        switch (member)
        {
            case "Activate":
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                ReplyEmpty(context);
                _trayIcon.RaiseClick(new Point(x, y), MouseButton.Left);
                break;
            case "SecondaryActivate":
            case "ContextMenu":
            case "Scroll":
                // The panel opens the exported menu itself, and the model has no action for the others.
                ReplyEmpty(context);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void HandleProperties(MethodContext context, string path, string member, ref Reader reader)
    {
        string @interface = reader.ReadString();
        var names = path == ITEM_PATH && @interface == ITEM_INTERFACE ? _itemProperties
            : path == MENU_PATH && @interface == MENU_INTERFACE ? _menuProperties
            : null;
        if (member == "Get")
        {
            string property = reader.ReadString();
            if (names == null || Array.IndexOf(names, property) < 0)
            {
                context.ReplyError(UNKNOWN_PROPERTY_ERROR, $"No property {@interface}.{property}.");
                return;
            }

            var writer = context.CreateReplyWriter("v");
            try
            {
                WriteProperty(ref writer, path, property);
                context.Reply(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }
        else if (member == "GetAll")
        {
            var writer = context.CreateReplyWriter("a{sv}");
            try
            {
                var dictionary = writer.WriteDictionaryStart();
                foreach (string property in names ?? [])
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString(property);
                    WriteProperty(ref writer, path, property);
                }

                writer.WriteDictionaryEnd(dictionary);
                context.Reply(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }
        else
        {
            context.ReplyUnknownMethodError();
        }
    }

    private void WriteProperty(ref MessageWriter writer, string path, string property)
    {
        if (path == MENU_PATH)
        {
            switch (property)
            {
                case "Version":
                    writer.WriteVariantUInt32(MENU_VERSION);
                    break;
                case "TextDirection":
                    writer.WriteVariantString("ltr");
                    break;
                case "Status":
                    writer.WriteVariantString("normal");
                    break;
                default:
                    writer.WriteSignature("as");
                    writer.WriteArray(Array.Empty<string>());
                    break;
            }

            return;
        }

        switch (property)
        {
            case "Category":
                writer.WriteVariantString("ApplicationStatus");
                break;
            case "Id":
                writer.WriteVariantString(LinuxApplicationIdentity.Id);
                break;
            case "Title":
                writer.WriteVariantString(TitleText);
                break;
            case "Status":
                writer.WriteVariantString("Active");
                break;
            case "WindowId":
                writer.WriteVariantInt32(0);
                break;
            case "IconName":
                // The desktop's icon for the application stands in when the application gives no pixels.
                writer.WriteVariantString(_pixmaps.Count == 0 ? LinuxApplicationIdentity.Id : string.Empty);
                break;
            case "IconPixmap":
                writer.WriteSignature("a(iiay)");
                WritePixmaps(ref writer, _pixmaps);
                break;
            case "OverlayIconPixmap":
            case "AttentionIconPixmap":
                writer.WriteSignature("a(iiay)");
                WritePixmaps(ref writer, []);
                break;
            case "ToolTip":
                writer.WriteSignature("(sa(iiay)ss)");
                writer.WriteStructureStart();
                writer.WriteString(string.Empty);
                WritePixmaps(ref writer, []);
                writer.WriteString(_trayIcon.ToolTipText);
                writer.WriteString(string.Empty);
                break;
            case "ItemIsMenu":
                // With nothing to run on activation, a primary click opens the menu.
                writer.WriteVariantBool(_trayIcon.ContextMenu != null && _trayIcon.Command == null && !_trayIcon.HasClickHandler);
                break;
            case "Menu":
                writer.WriteVariantObjectPath(_publishedMenu ? MENU_PATH : NO_MENU_PATH);
                break;
            default:
                writer.WriteVariantString(string.Empty);
                break;
        }
    }

    private string TitleText => string.IsNullOrEmpty(_trayIcon.ToolTipText) ? LinuxApplicationIdentity.Id : _trayIcon.ToolTipText;

    private static void WritePixmaps(ref MessageWriter writer, IReadOnlyList<(int Width, int Height, byte[] Argb)> pixmaps)
    {
        var array = writer.WriteArrayStart(DBusType.Struct);
        foreach (var (width, height, argb) in pixmaps)
        {
            writer.WriteStructureStart();
            writer.WriteInt32(width);
            writer.WriteInt32(height);
            writer.WriteArray(argb);
        }

        writer.WriteArrayEnd(array);
    }

    /// <summary>The icon at each distinct size it offers, as ARGB32 in network byte order.</summary>
    private List<(int Width, int Height, byte[] Argb)> ReadPixmaps()
    {
        var pixmaps = new List<(int Width, int Height, byte[] Argb)>();
        var source = _trayIcon.Icon ?? ApplicationWindowIcon();
        if (source == null)
        {
            return pixmaps;
        }

        foreach (int size in _iconSizes)
        {
            if (!source.TryGetPixels(size, out var pixels) || pixmaps.Exists(pixmap => pixmap.Width == pixels.WidthPx))
            {
                continue;
            }

            var argb = new byte[pixels.WidthPx * pixels.HeightPx * 4];
            for (int offset = 0; offset < argb.Length; offset += 4)
            {
                argb[offset] = pixels.Data[offset + 3];
                argb[offset + 1] = pixels.Data[offset + 2];
                argb[offset + 2] = pixels.Data[offset + 1];
                argb[offset + 3] = pixels.Data[offset];
            }

            pixmaps.Add((pixels.WidthPx, pixels.HeightPx, argb));
        }

        return pixmaps;
    }

    private static IconSource? ApplicationWindowIcon()
    {
        foreach (var window in Application.Current.AllWindows)
        {
            if (window.Icon != null)
            {
                return window.Icon;
            }
        }

        return null;
    }

    private void HandleMenu(MethodContext context, string member, ref Reader reader)
    {
        switch (member)
        {
            case "GetLayout":
                HandleGetLayout(context, ref reader);
                break;
            case "GetGroupProperties":
                HandleGetGroupProperties(context, ref reader);
                break;
            case "GetProperty":
                HandleGetProperty(context, ref reader);
                break;
            case "Event":
            {
                int id = reader.ReadInt32();
                string eventId = reader.ReadString();
                ReplyEmpty(context);
                HandleEvent(id, eventId);
                break;
            }
            case "EventGroup":
                HandleEventGroup(context, ref reader);
                break;
            case "AboutToShow":
            {
                int id = reader.ReadInt32();
                bool changed = OpenMenu(id);
                var writer = context.CreateReplyWriter("b");
                try
                {
                    writer.WriteBool(changed);
                    context.Reply(writer.CreateMessage());
                }
                finally
                {
                    writer.Dispose();
                }

                break;
            }
            case "AboutToShowGroup":
                HandleAboutToShowGroup(context, ref reader);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    /// <summary>Refreshes the model when the root opens; true when the layout the panel holds is out of date.</summary>
    private bool OpenMenu(int id)
    {
        if (id != ROOT_ID)
        {
            return false;
        }

        bool changed = RefreshMenu(raiseOpening: true);
        if (changed)
        {
            SignalLayoutUpdated();
        }

        return changed;
    }

    private void HandleGetLayout(MethodContext context, ref Reader reader)
    {
        int parent = reader.ReadInt32();
        int depth = reader.ReadInt32();
        var names = reader.ReadArrayOfString();
        if (_menuSignature == null)
        {
            RefreshMenu(raiseOpening: false);
        }

        if (parent != ROOT_ID && !_nodes.ContainsKey(parent))
        {
            context.ReplyError(INVALID_ARGS_ERROR, $"No menu item {parent}.");
            return;
        }

        var writer = context.CreateReplyWriter("u" + LAYOUT_SIGNATURE);
        try
        {
            writer.WriteUInt32(_menuRevision);
            WriteNode(ref writer, parent, depth, names);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private void WriteNode(ref MessageWriter writer, int id, int depth, string[] names)
    {
        var node = id == ROOT_ID ? null : _nodes[id];
        writer.WriteStructureStart();
        writer.WriteInt32(id);
        WriteNodeProperties(ref writer, node, names);

        var children = writer.WriteArrayStart(DBusType.Variant);
        if (depth != 0)
        {
            foreach (var child in node?.Children ?? _rootNodes)
            {
                writer.WriteSignature(LAYOUT_SIGNATURE);
                WriteNode(ref writer, child.Id, depth < 0 ? depth : depth - 1, names);
            }
        }

        writer.WriteArrayEnd(children);
    }

    private static void WriteNodeProperties(ref MessageWriter writer, MenuNode? node, string[] names)
    {
        var dictionary = writer.WriteDictionaryStart();
        foreach (string property in PropertiesOf(node))
        {
            if (names.Length > 0 && Array.IndexOf(names, property) < 0)
            {
                continue;
            }

            writer.WriteDictionaryEntryStart();
            writer.WriteString(property);
            WriteNodeProperty(ref writer, node, property);
        }

        writer.WriteDictionaryEnd(dictionary);
    }

    private static List<string> PropertiesOf(MenuNode? node)
    {
        var properties = new List<string>(8);
        if (node == null)
        {
            properties.Add("children-display");
            return properties;
        }

        var entry = node.Entry;
        if (entry.Kind == NativeMenuEntryKind.Separator)
        {
            properties.Add("type");
            return properties;
        }

        properties.Add("label");
        properties.Add("enabled");
        if (entry.Kind == NativeMenuEntryKind.SubMenu)
        {
            properties.Add("children-display");
        }

        if (entry.IsCheckable)
        {
            properties.Add("toggle-type");
            properties.Add("toggle-state");
        }

        if (entry.Icon != null)
        {
            properties.Add("icon-data");
        }

        if (entry.Gesture is KeyGesture gesture && TryGetShortcut(gesture, out _))
        {
            properties.Add("shortcut");
        }

        return properties;
    }

    private static void WriteNodeProperty(ref MessageWriter writer, MenuNode? node, string property)
    {
        var entry = node?.Entry;
        switch (property)
        {
            case "children-display":
                writer.WriteVariantString("submenu");
                break;
            case "type":
                writer.WriteVariantString("separator");
                break;
            case "label":
                writer.WriteVariantString(entry!.FormatText('_'));
                break;
            case "enabled":
                writer.WriteVariantBool(entry!.IsEnabled);
                break;
            case "toggle-type":
                writer.WriteVariantString("checkmark");
                break;
            case "toggle-state":
                writer.WriteVariantInt32(entry!.IsChecked ? 1 : 0);
                break;
            case "icon-data":
                writer.WriteSignature("ay");
                writer.WriteArray(entry!.Icon!.EncodePng());
                break;
            default:
                TryGetShortcut(entry!.Gesture!.Value, out var shortcut);
                writer.WriteSignature("aas");
                var outer = writer.WriteArrayStart(DBusType.Array);
                writer.WriteArray(shortcut);
                writer.WriteArrayEnd(outer);
                break;
        }
    }

    /// <summary>The gesture as the modifier and key names the menu protocol expects.</summary>
    private static bool TryGetShortcut(KeyGesture gesture, out string[] shortcut)
    {
        string? keyName = gesture.Key switch
        {
            >= Key.A and <= Key.Z => char.ToLowerInvariant(gesture.Key.ToString()[0]).ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (gesture.Key - Key.D0))).ToString(),
            >= Key.F1 and <= Key.F12 => gesture.Key.ToString(),
            Key.Enter => "Return",
            Key.Escape => "Escape",
            Key.Delete => "Delete",
            Key.Tab => "Tab",
            Key.Space => "space",
            _ => null,
        };
        if (keyName == null)
        {
            shortcut = [];
            return false;
        }

        var parts = new List<string>(4);
        if ((gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Primary)) != 0)
        {
            parts.Add("Control");
        }

        if ((gesture.Modifiers & ModifierKeys.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((gesture.Modifiers & ModifierKeys.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((gesture.Modifiers & ModifierKeys.Meta) != 0)
        {
            parts.Add("Super");
        }

        parts.Add(keyName);
        shortcut = parts.ToArray();
        return true;
    }

    private void HandleGetGroupProperties(MethodContext context, ref Reader reader)
    {
        var ids = reader.ReadArrayOfInt32();
        var names = reader.ReadArrayOfString();
        if (_menuSignature == null)
        {
            RefreshMenu(raiseOpening: false);
        }

        var writer = context.CreateReplyWriter("a(ia{sv})");
        try
        {
            var array = writer.WriteArrayStart(DBusType.Struct);
            foreach (int id in ids)
            {
                if (id == ROOT_ID || _nodes.ContainsKey(id))
                {
                    writer.WriteStructureStart();
                    writer.WriteInt32(id);
                    WriteNodeProperties(ref writer, id == ROOT_ID ? null : _nodes[id], names);
                }
            }

            writer.WriteArrayEnd(array);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private void HandleGetProperty(MethodContext context, ref Reader reader)
    {
        int id = reader.ReadInt32();
        string property = reader.ReadString();
        var node = id == ROOT_ID ? null : _nodes.GetValueOrDefault(id);
        if ((id != ROOT_ID && node == null) || !PropertiesOf(node).Contains(property))
        {
            context.ReplyError(INVALID_ARGS_ERROR, $"No property {property} on menu item {id}.");
            return;
        }

        var writer = context.CreateReplyWriter("v");
        try
        {
            WriteNodeProperty(ref writer, node, property);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private void HandleEventGroup(MethodContext context, ref Reader reader)
    {
        var missing = new List<int>();
        var events = new List<(int Id, string EventId)>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            int id = reader.ReadInt32();
            string eventId = reader.ReadString();
            reader.ReadVariantValue();
            reader.ReadUInt32();
            if (id != ROOT_ID && !_nodes.ContainsKey(id))
            {
                missing.Add(id);
            }
            else
            {
                events.Add((id, eventId));
            }
        }

        var writer = context.CreateReplyWriter("ai");
        try
        {
            writer.WriteArray(missing.ToArray());
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }

        foreach (var (id, eventId) in events)
        {
            HandleEvent(id, eventId);
        }
    }

    private void HandleAboutToShowGroup(MethodContext context, ref Reader reader)
    {
        var ids = reader.ReadArrayOfInt32();
        var updates = new List<int>();
        var missing = new List<int>();
        foreach (int id in ids)
        {
            if (id != ROOT_ID && !_nodes.ContainsKey(id))
            {
                missing.Add(id);
            }
            else if (OpenMenu(id))
            {
                updates.Add(id);
            }
        }

        var writer = context.CreateReplyWriter("aiai");
        try
        {
            writer.WriteArray(updates.ToArray());
            writer.WriteArray(missing.ToArray());
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private void HandleEvent(int id, string eventId)
    {
        // An id from an earlier layout is not in the table, so a stale click runs nothing.
        if (eventId == "clicked" && _snapshot != null && _nodes.TryGetValue(id, out var node)
            && node.Entry.Kind == NativeMenuEntryKind.Item && node.Entry.IsEnabled)
        {
            _snapshot.Invoke(node.Entry);
        }
    }

    /// <summary>Reads the model into the exported tree; true when what the panel shows has changed.</summary>
    private bool RefreshMenu(bool raiseOpening)
    {
        var model = raiseOpening ? _trayIcon.PrepareMenu() : _trayIcon.ContextMenu;
        if (_disposed)
        {
            return false;
        }

        double scale = Application.Current.PlatformHost.GetSystemDpi() / STANDARD_DPI;
        var snapshot = new NativeMenuSnapshot(Application.Current.CommandRouter, _trayIcon.CommandTarget, gestureWindow: null,
            MENU_ICON_SIZE, scale);
        var entries = model != null ? snapshot.Read(model.Menu) : [];

        var signature = new StringBuilder();
        AppendSignature(signature, entries);
        string text = signature.ToString();
        if (text == _menuSignature)
        {
            return false;
        }

        _menuSignature = text;
        _snapshot = snapshot;
        _nodes.Clear();
        _rootNodes.Clear();
        AddNodes(entries, _rootNodes);
        _menuRevision++;
        return true;
    }

    private void AddNodes(IReadOnlyList<NativeMenuEntry> entries, List<MenuNode> target)
    {
        foreach (var entry in entries)
        {
            // Ids are never reused, so a click sent against an older layout cannot reach a different item.
            var node = new MenuNode(++_lastNodeId, entry);
            _nodes[node.Id] = node;
            target.Add(node);
            AddNodes(entry.Children, node.Children);
        }
    }

    private static void AppendSignature(StringBuilder builder, IReadOnlyList<NativeMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            builder.Append('[').Append((int)entry.Kind).Append('|').Append(entry.Text).Append('|').Append(entry.AccessKeyIndex)
                .Append('|').Append(entry.IsEnabled).Append('|').Append(entry.IsCheckable).Append('|').Append(entry.IsChecked)
                .Append('|').Append(entry.Gesture?.ToString()).Append('|')
                .Append(entry.Item == null ? 0 : RuntimeHelpers.GetHashCode(entry.Item)).Append('|')
                .Append(entry.Icon == null ? 0 : RuntimeHelpers.GetHashCode(entry.Icon));
            AppendSignature(builder, entry.Children);
            builder.Append(']');
        }
    }

    private void Signal(string path, string @interface, string member)
    {
        DBusConnection? connection;
        lock (_connectionGate)
        {
            connection = _connection;
        }

        if (connection == null)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(null, path, @interface, member, null);
        connection.TrySendMessage(writer.CreateMessage());
    }

    private void SignalLayoutUpdated()
    {
        DBusConnection? connection;
        lock (_connectionGate)
        {
            connection = _connection;
        }

        if (connection == null)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(null, MENU_PATH, MENU_INTERFACE, "LayoutUpdated", "ui");
        writer.WriteUInt32(_menuRevision);
        writer.WriteInt32(ROOT_ID);
        connection.TrySendMessage(writer.CreateMessage());
    }

    private static void ReplyEmpty(MethodContext context)
    {
        if (!context.NoReplyExpected)
        {
            using var writer = context.CreateReplyWriter(null);
            context.Reply(writer.CreateMessage());
        }
    }

    private sealed class MenuNode(int id, NativeMenuEntry entry)
    {
        public int Id { get; } = id;

        public NativeMenuEntry Entry { get; } = entry;

        public List<MenuNode> Children { get; } = new();
    }

    private sealed class PathHandler(LinuxTrayIconBackend backend, string path) : IPathMethodHandler
    {
        public string Path { get; } = path;

        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context) => backend.HandleAsync(context);
    }
}
