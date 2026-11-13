using System.Runtime.CompilerServices;

using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Diagnostics;

using Tmds.DBus.Protocol;

namespace Aprillz.MewUI.Platform.Linux.Automation;

/// <summary>
/// Serves the application's automation tree on the AT-SPI2 accessibility bus. The application object holds one
/// child per shown window (popup windows included), each window's peers hang below it, and every peer is an
/// object under <see cref="PATH_PREFIX"/>. Requests run on the UI thread; change notifications go out as
/// AT-SPI2 event signals.
/// </summary>
internal sealed partial class AtSpiServer : IPathMethodHandler, IAutomationEventSink
{
    private const string PATH_PREFIX = "/org/a11y/atspi/accessible";
    private const string ROOT_PATH = "/org/a11y/atspi/accessible/root";
    private const string NULL_PATH = "/org/a11y/atspi/null";
    private const string ACCESSIBLE = "org.a11y.atspi.Accessible";
    private const string APPLICATION = "org.a11y.atspi.Application";
    private const string COMPONENT = "org.a11y.atspi.Component";
    private const string ACTION = "org.a11y.atspi.Action";
    private const string VALUE = "org.a11y.atspi.Value";
    private const string SELECTION = "org.a11y.atspi.Selection";
    private const string TEXT = "org.a11y.atspi.Text";
    private const string EDITABLE_TEXT = "org.a11y.atspi.EditableText";
    private const string PROPERTIES = "org.freedesktop.DBus.Properties";
    private const string EVENT_OBJECT = "org.a11y.atspi.Event.Object";
    private const string EVENT_WINDOW = "org.a11y.atspi.Event.Window";
    private const string EVENT_SIGNATURE = "siiva{sv}";
    // Long enough for a busy UI thread; a hung one fails the reader's call instead of holding it forever.
    private const int TIMEOUT_MILLISECONDS = 10_000;

    private static readonly EnvDebugLogger _logger = new("MEWUI_AUTOMATION_DEBUG", "[AT-SPI]");
    private static bool _started;
    // 1 once registration on the accessibility bus has begun.
    private static int _serving;
    // Kept open to hear accessibility being turned on.
    private static DBusConnection? _session;

    private readonly DBusConnection _connection;
    private readonly string _busName;
    private readonly ConditionalWeakTable<AutomationPeer, AtSpiNode> _nodes = new();
    private readonly Dictionary<string, WeakReference<AtSpiNode>> _byPath = new(StringComparer.Ordinal);
    private (string Name, string Path) _desktop = (string.Empty, NULL_PATH);
    private int _lastId;
    private Window? _activeSurface;

    private AtSpiServer(DBusConnection connection)
    {
        _connection = connection;
        _busName = connection.UniqueName ?? string.Empty;
    }

    public string Path => PATH_PREFIX;

    public bool HandlesChildPaths => true;

    /// <summary>
    /// Registers the application on the accessibility bus in the background, once. While accessibility is off it
    /// waits on the session bus and registers when a reader turns it on.
    /// </summary>
    internal static void Start()
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        string? sessionAddress = DBusAddress.Session;
        if (_started || dispatcher == null || sessionAddress == null)
        {
            return;
        }

        _started = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var session = new DBusConnection(sessionAddress);
                await session.ConnectAsync();
                _session = session;
                var rule = new MatchRule
                {
                    Type = MessageType.Signal,
                    Path = "/org/a11y/bus",
                    Interface = PROPERTIES,
                    Member = "PropertiesChanged",
                };
                await session.AddMatchAsync(rule, notification => _ = ServeIfEnabledAsync(session, dispatcher), false, ObserverFlags.None, null);
                await ServeIfEnabledAsync(session, dispatcher);
            }
            catch (Exception exception)
            {
                _logger.Write($"not serving: {exception.Message}");
            }
        });
    }

    private static async Task ServeIfEnabledAsync(DBusConnection session, IDispatcher dispatcher)
    {
        try
        {
            if (_serving != 0 || !await IsEnabledAsync(session) || Interlocked.Exchange(ref _serving, 1) != 0)
            {
                return;
            }

            string address = await CallStringAsync(session, "org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress");
            var server = await ConnectAsync(address);
            dispatcher.BeginInvoke(() => AutomationEvents.AddSink(server));
        }
        catch (Exception exception)
        {
            _serving = 0;
            _logger.Write($"not serving: {exception.Message}");
        }
    }

    private static async Task<AtSpiServer> ConnectAsync(string accessibilityAddress)
    {
        var connection = new DBusConnection(accessibilityAddress);
        await connection.ConnectAsync();
        var server = new AtSpiServer(connection);
        connection.AddMethodHandler(server);
        server._desktop = await EmbedAsync(connection, server._busName);
        _logger.Write($"registered as {server._busName} under {server._desktop.Name}");
        return server;
    }

    private static async Task<bool> IsEnabledAsync(DBusConnection session)
    {
        MessageBuffer call;
        using (var writer = session.GetMessageWriter())
        {
            writer.WriteMethodCallHeader("org.a11y.Bus", "/org/a11y/bus", PROPERTIES, "Get", "ss", MessageFlags.None);
            writer.WriteString("org.a11y.Status");
            writer.WriteString("IsEnabled");
            call = writer.CreateMessage();
        }

        return await session.CallMethodAsync(call, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadSignature();
            return reader.ReadBool();
        }, null);
    }

    private static async Task<string> CallStringAsync(DBusConnection connection, string destination, string path, string @interface, string member)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination, path, @interface, member, null, MessageFlags.None);
            call = writer.CreateMessage();
        }

        return await connection.CallMethodAsync(call, static (message, _) => message.GetBodyReader().ReadString(), null);
    }

    /// <summary>Plugs the application into the registry's desktop and returns the desktop's reference.</summary>
    private static async Task<(string Name, string Path)> EmbedAsync(DBusConnection connection, string busName)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader("org.a11y.atspi.Registry", ROOT_PATH, "org.a11y.atspi.Socket", "Embed", "(so)", MessageFlags.None);
            writer.WriteStructureStart();
            writer.WriteString(busName);
            writer.WriteObjectPath(ROOT_PATH);
            call = writer.CreateMessage();
        }

        return await connection.CallMethodAsync(call, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.AlignStruct();
            string name = reader.ReadString();
            string path = reader.ReadObjectPathAsString();
            return (name, path);
        }, null);
    }

    public async ValueTask HandleMethodAsync(MethodContext context)
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher == null)
        {
            context.ReplyError("org.a11y.atspi.Error.Failed", "The application is not running.");
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            try
            {
                Dispatch(context);
            }
            catch (Exception exception)
            {
                if (!context.ReplySent)
                {
                    context.ReplyError("org.a11y.atspi.Error.Failed", exception.Message);
                }
            }
            finally
            {
                done.TrySetResult();
            }
        });

        try
        {
            await done.Task.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MILLISECONDS));
        }
        catch (TimeoutException)
        {
            if (!context.ReplySent)
            {
                context.ReplyError("org.a11y.atspi.Error.Failed", "The UI thread did not answer in time.");
            }
        }
    }

    /// <summary>The node of <paramref name="peer"/>, created on first use.</summary>
    private AtSpiNode NodeOf(AutomationPeer peer)
    {
        if (_nodes.TryGetValue(peer, out var node))
        {
            return node;
        }

        node = new AtSpiNode(peer, $"{PATH_PREFIX}/{++_lastId}");
        if (peer is ITextProvider text)
        {
            node.LastText = text.Text;
        }

        _nodes.Add(peer, node);
        _byPath[node.Path] = new WeakReference<AtSpiNode>(node);
        return node;
    }

    private AtSpiNode? NodeAt(string path)
        => _byPath.TryGetValue(path, out var reference) && reference.TryGetTarget(out var node) ? node : null;

    /// <summary>The windows shown on screen, popup windows included, as the application object's children.</summary>
    private static List<Window> Surfaces()
    {
        var surfaces = new List<Window>();
        foreach (var window in Application.Current.AllWindows)
        {
            if (window.Handle == 0 || !window.IsVisible || surfaces.Contains(window))
            {
                continue;
            }

            surfaces.Add(window);
            for (int index = 0; index < window.OpenPopupCount; index++)
            {
                if (window.OpenPopupAt(index).ResolveInputHostWindow() is Window surface && !ReferenceEquals(surface, window)
                    && surface.Handle != 0 && !surfaces.Contains(surface))
                {
                    surfaces.Add(surface);
                }
            }
        }

        return surfaces;
    }

    private void WriteReference(ref MessageWriter writer, AtSpiNode? node)
    {
        writer.WriteStructureStart();
        writer.WriteString(_busName);
        writer.WriteObjectPath(node?.Path ?? ROOT_PATH);
    }

    private void WriteReferenceOf(ref MessageWriter writer, AutomationPeer? peer)
    {
        if (peer == null)
        {
            writer.WriteStructureStart();
            writer.WriteString(string.Empty);
            writer.WriteObjectPath(NULL_PATH);
        }
        else
        {
            WriteReference(ref writer, NodeOf(peer));
        }
    }

    private void WriteReferenceArray(ref MessageWriter writer, IEnumerable<AutomationPeer> peers)
    {
        var array = writer.WriteArrayStart(DBusType.Struct);
        foreach (var peer in peers)
        {
            WriteReference(ref writer, NodeOf(peer));
        }

        writer.WriteArrayEnd(array);
    }

    private void Signal(string path, string @interface, string member, string detail, int detail1, int detail2, string? textData)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(null, path, @interface, member, EVENT_SIGNATURE);
        writer.WriteString(detail);
        writer.WriteInt32(detail1);
        writer.WriteInt32(detail2);
        if (textData != null)
        {
            writer.WriteVariantString(textData);
        }
        else
        {
            writer.WriteVariantInt32(0);
        }

        var properties = writer.WriteDictionaryStart();
        writer.WriteDictionaryEnd(properties);
        _connection.TrySendMessage(writer.CreateMessage());
    }

    private static bool IsShown(AutomationPeer peer) => AutomationSurface.Of(peer) is Window window && window.Handle != 0;

    public void OnFocusChanged(AutomationPeer peer)
    {
        if (!IsShown(peer))
        {
            return;
        }

        // Readers follow the active window; announce it first when focus moved into another window.
        if (AutomationSurface.Of(peer) is Window surface && !ReferenceEquals(surface, _activeSurface)
            && surface.GetAutomationPeer() is AutomationPeer surfacePeer)
        {
            if (_activeSurface?.GetAutomationPeer() is AutomationPeer previous && _nodes.TryGetValue(previous, out var previousNode))
            {
                Signal(previousNode.Path, EVENT_WINDOW, "Deactivate", string.Empty, 0, 0, _activeSurface.Title ?? string.Empty);
            }

            _activeSurface = surface;
            Signal(NodeOf(surfacePeer).Path, EVENT_WINDOW, "Activate", string.Empty, 0, 0, surface.Title ?? string.Empty);
        }

        if (peer is not WindowAutomationPeer)
        {
            Signal(NodeOf(peer).Path, EVENT_OBJECT, "StateChanged", "focused", 1, 0, null);
        }
    }

    public void OnStructureChanged(AutomationPeer peer)
    {
        if (IsShown(peer) && _nodes.TryGetValue(peer, out var node))
        {
            Signal(node.Path, EVENT_OBJECT, "ChildrenChanged", "add", -1, 0, null);
        }
    }

    public void OnEvent(AutomationPeer peer, AutomationEventKind kind)
    {
        if (!IsShown(peer))
        {
            return;
        }

        var node = NodeOf(peer);
        if (kind == AutomationEventKind.TextChanged)
        {
            AnnounceTextChange(node);
        }
        else if (kind == AutomationEventKind.TextSelectionChanged)
        {
            if (peer is ITextProvider text)
            {
                Signal(node.Path, EVENT_OBJECT, "TextCaretMoved", string.Empty, text.CaretOffset, 0, null);
            }

            Signal(node.Path, EVENT_OBJECT, "TextSelectionChanged", string.Empty, 0, 0, null);
        }
        else
        {
            bool shown = kind is AutomationEventKind.MenuOpened or AutomationEventKind.ToolTipOpened;
            Signal(node.Path, EVENT_OBJECT, "StateChanged", "showing", shown ? 1 : 0, 0, null);
        }
    }

    /// <summary>Sends the text that left and the text that arrived since the last change, found by their common ends.</summary>
    private void AnnounceTextChange(AtSpiNode node)
    {
        if (node.Peer is not ITextProvider pattern)
        {
            return;
        }

        string current = pattern.Text;
        string previous = node.LastText ?? current;
        node.LastText = current;
        int prefix = 0;
        while (prefix < previous.Length && prefix < current.Length && previous[prefix] == current[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < previous.Length - prefix && suffix < current.Length - prefix
            && previous[previous.Length - 1 - suffix] == current[current.Length - 1 - suffix])
        {
            suffix++;
        }

        int removed = previous.Length - prefix - suffix;
        int inserted = current.Length - prefix - suffix;
        if (removed > 0)
        {
            Signal(node.Path, EVENT_OBJECT, "TextChanged", "delete", prefix, removed, previous.Substring(prefix, removed));
        }

        if (inserted > 0)
        {
            Signal(node.Path, EVENT_OBJECT, "TextChanged", "insert", prefix, inserted, current.Substring(prefix, inserted));
        }
    }

    public void OnPropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
    {
        if (!IsShown(peer) || !_nodes.TryGetValue(peer, out var node))
        {
            // Nothing has asked for this element; a reader learns its state when it does.
            return;
        }

        if (property == AutomationProperty.NameProperty)
        {
            Signal(node.Path, EVENT_OBJECT, "PropertyChange", "accessible-name", 0, 0, peer.GetName());
        }
        else if (property == AutomationProperty.ValueProperty || property == AutomationProperty.RangeValueProperty)
        {
            Signal(node.Path, EVENT_OBJECT, "PropertyChange", "accessible-value", 0, 0, null);
        }
        else if (property == AutomationProperty.ToggleStateProperty)
        {
            bool on = peer is IToggleProvider toggle && toggle.ToggleState == ToggleState.On;
            Signal(node.Path, EVENT_OBJECT, "StateChanged", "checked", on ? 1 : 0, 0, null);
        }
        else if (property == AutomationProperty.IsSelectedProperty)
        {
            bool selected = newValue is true;
            Signal(node.Path, EVENT_OBJECT, "StateChanged", "selected", selected ? 1 : 0, 0, null);
            if (peer is ISelectionItemProvider item && item.SelectionContainer is AutomationPeer container
                && _nodes.TryGetValue(container, out var containerNode))
            {
                Signal(containerNode.Path, EVENT_OBJECT, "SelectionChanged", string.Empty, 0, 0, null);
            }
        }
        else if (property == AutomationProperty.ExpandCollapseStateProperty)
        {
            bool expanded = peer is IExpandCollapseProvider expander && expander.ExpandCollapseState == ExpandCollapseState.Expanded;
            Signal(node.Path, EVENT_OBJECT, "StateChanged", "expanded", expanded ? 1 : 0, 0, null);
        }
        else if (property == AutomationProperty.IsEnabledProperty)
        {
            Signal(node.Path, EVENT_OBJECT, "StateChanged", "enabled", peer.IsEnabled() ? 1 : 0, 0, null);
            Signal(node.Path, EVENT_OBJECT, "StateChanged", "sensitive", peer.IsEnabled() ? 1 : 0, 0, null);
        }
    }
}
