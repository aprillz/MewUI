using Tmds.DBus.Protocol;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// An AT-SPI2 client for AtSpiProviderTests: it talks to the application over the accessibility bus the way a
/// screen reader does, through the bus daemon, from a thread other than the UI thread.
/// </summary>
internal sealed class AtSpiClient : IDisposable
{
    private const string ACCESSIBLE = "org.a11y.atspi.Accessible";
    private const string PROPERTIES = "org.freedesktop.DBus.Properties";

    private readonly DBusConnection _session;
    private readonly DBusConnection _bus;
    private readonly bool _wasEnabled;

    private AtSpiClient(DBusConnection session, DBusConnection bus, bool wasEnabled)
    {
        _session = session;
        _bus = bus;
        _wasEnabled = wasEnabled;
    }

    internal readonly record struct Reference(string Name, string Path);

    /// <summary>Turns accessibility on for the session (restored on dispose) and connects to the accessibility bus.</summary>
    public static async Task<AtSpiClient> ConnectAsync()
    {
        var session = new DBusConnection(DBusAddress.Session ?? throw new InvalidOperationException("No session bus."));
        await session.ConnectAsync().ConfigureAwait(false);
        bool wasEnabled = await GetEnabledAsync(session).ConfigureAwait(false);
        if (!wasEnabled)
        {
            await SetEnabledAsync(session, true).ConfigureAwait(false);
        }

        string address = await session.CallMethodAsync(Call(session, "org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress"),
            static (message, _) => message.GetBodyReader().ReadString(), null).ConfigureAwait(false);
        var bus = new DBusConnection(address);
        await bus.ConnectAsync().ConfigureAwait(false);
        return new AtSpiClient(session, bus, wasEnabled);
    }

    /// <summary>The application object of this process, once it has registered with the desktop.</summary>
    public async Task<Reference> FindApplicationAsync(string name)
    {
        var seen = new List<string>();
        for (int attempt = 0; attempt < 50; attempt++)
        {
            seen.Clear();
            foreach (var application in await ChildrenAsync(new Reference("org.a11y.atspi.Registry", "/org/a11y/atspi/accessible/root")).ConfigureAwait(false))
            {
                string applicationName = await NameAsync(application).ConfigureAwait(false);
                seen.Add(applicationName);
                if (applicationName == name)
                {
                    return application;
                }
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new AssertFailedException($"no application '{name}' on the accessibility bus; found: {string.Join(", ", seen)}");
    }

    public Task<Reference[]> ChildrenAsync(Reference element)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, ACCESSIBLE, "GetChildren"), static (message, _) =>
        {
            var reader = message.GetBodyReader();
            var children = new List<Reference>();
            var end = reader.ReadArrayStart(DBusType.Struct);
            while (reader.HasNext(end))
            {
                reader.AlignStruct();
                children.Add(new Reference(reader.ReadString(), reader.ReadObjectPathAsString()));
            }

            return children.ToArray();
        }, null);

    public async Task<string> NameAsync(Reference element)
    {
        var call = Call(_bus, element.Name, element.Path, PROPERTIES, "Get", "ss", ACCESSIBLE, "Name");
        return await _bus.CallMethodAsync(call, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadSignature();
            return reader.ReadString();
        }, null);
    }

    public async Task<string> AccessibleIdAsync(Reference element)
    {
        var call = Call(_bus, element.Name, element.Path, PROPERTIES, "Get", "ss", ACCESSIBLE, "AccessibleId");
        return await _bus.CallMethodAsync(call, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadSignature();
            return reader.ReadString();
        }, null);
    }

    public Task<uint> RoleAsync(Reference element)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, ACCESSIBLE, "GetRole"),
            static (message, _) => message.GetBodyReader().ReadUInt32(), null);

    public Task<ulong> StatesAsync(Reference element)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, ACCESSIBLE, "GetState"), static (message, _) =>
        {
            var reader = message.GetBodyReader();
            var end = reader.ReadArrayStart(DBusType.UInt32);
            ulong low = reader.HasNext(end) ? reader.ReadUInt32() : 0;
            ulong high = reader.HasNext(end) ? reader.ReadUInt32() : 0;
            return low | (high << 32);
        }, null);

    public Task<string[]> InterfacesAsync(Reference element)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, ACCESSIBLE, "GetInterfaces"),
            static (message, _) => message.GetBodyReader().ReadArrayOfString(), null);

    public Task<string[]> ActionsAsync(Reference element)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, "org.a11y.atspi.Action", "GetActions"), static (message, _) =>
        {
            var reader = message.GetBodyReader();
            var names = new List<string>();
            var end = reader.ReadArrayStart(DBusType.Struct);
            while (reader.HasNext(end))
            {
                reader.AlignStruct();
                names.Add(reader.ReadString());
                reader.ReadString();
                reader.ReadString();
            }

            return names.ToArray();
        }, null);

    public async Task<bool> DoActionAsync(Reference element, string action)
    {
        int index = Array.IndexOf(await ActionsAsync(element).ConfigureAwait(false), action);
        if (index < 0)
        {
            throw new AssertFailedException($"no action '{action}'");
        }

        return await _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, "org.a11y.atspi.Action", "DoAction", "i", index),
            static (message, _) => message.GetBodyReader().ReadBool(), null).ConfigureAwait(false);
    }

    public Task<bool> SetTextAsync(Reference element, string text)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, "org.a11y.atspi.EditableText", "SetTextContents", "s", text),
            static (message, _) => message.GetBodyReader().ReadBool(), null);

    public Task<string> TextAsync(Reference element)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, "org.a11y.atspi.Text", "GetText", "ii", 0, -1),
            static (message, _) => message.GetBodyReader().ReadString(), null);

    public Task<(string Text, int Start, int End)> TextAtOffsetAsync(Reference element, int offset, uint boundary)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, "org.a11y.atspi.Text", "GetTextAtOffset", "iu", offset, boundary),
            static (message, _) =>
            {
                var reader = message.GetBodyReader();
                return (reader.ReadString(), reader.ReadInt32(), reader.ReadInt32());
            }, null);

    public Task<(int Start, int End)> SelectionAsync(Reference element)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, "org.a11y.atspi.Text", "GetSelection", "i", 0),
            static (message, _) =>
            {
                var reader = message.GetBodyReader();
                return (reader.ReadInt32(), reader.ReadInt32());
            }, null);

    public async Task SetCurrentValueAsync(Reference element, double value)
    {
        MessageBuffer call;
        using (var writer = _bus.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(element.Name, element.Path, PROPERTIES, "Set", "ssv", MessageFlags.None);
            writer.WriteString("org.a11y.atspi.Value");
            writer.WriteString("CurrentValue");
            writer.WriteVariantDouble(value);
            call = writer.CreateMessage();
        }

        await _bus.CallMethodAsync(call).ConfigureAwait(false);
    }

    public Task<Reference> AccessibleAtPointAsync(Reference element, int x, int y)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, "org.a11y.atspi.Component", "GetAccessibleAtPoint", "iiu", x, y, 0u),
            static (message, _) =>
            {
                var reader = message.GetBodyReader();
                reader.AlignStruct();
                return new Reference(reader.ReadString(), reader.ReadObjectPathAsString());
            }, null);

    public Task<(int X, int Y, int Width, int Height)> ExtentsAsync(Reference element)
        => _bus.CallMethodAsync(Call(_bus, element.Name, element.Path, "org.a11y.atspi.Component", "GetExtents", "u", 0u),
            static (message, _) =>
            {
                var reader = message.GetBodyReader();
                reader.AlignStruct();
                return (reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            }, null);

    /// <summary>Collects "focused" state changes sent by <paramref name="application"/>, as object paths.</summary>
    public async Task<List<string>> WatchFocusAsync(Reference application)
    {
        var focused = new List<string>();
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Interface = "org.a11y.atspi.Event.Object",
            Member = "StateChanged",
            Sender = application.Name,
        };
        await _bus.AddMatchAsync(rule, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            string detail = reader.ReadString();
            int value = reader.ReadInt32();
            return (Path: message.PathAsString ?? string.Empty, Focused: detail == "focused" && value == 1);
        }, notification =>
        {
            if (notification.HasValue && notification.Value.Focused)
            {
                lock (focused)
                {
                    focused.Add(notification.Value.Path);
                }
            }
        }, false, ObserverFlags.None, null).ConfigureAwait(false);
        return focused;
    }

    private static MessageBuffer Call(DBusConnection connection, string destination, string path, string @interface, string member,
        string? signature = null, params object[] arguments)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, path, @interface, member, signature, MessageFlags.None);
        foreach (object argument in arguments)
        {
            switch (argument)
            {
                case string text:
                    writer.WriteString(text);
                    break;
                case int number:
                    writer.WriteInt32(number);
                    break;
                case uint number:
                    writer.WriteUInt32(number);
                    break;
                default:
                    throw new ArgumentException($"unsupported argument {argument}");
            }
        }

        return writer.CreateMessage();
    }

    private static Task<bool> GetEnabledAsync(DBusConnection session)
        => session.CallMethodAsync(Call(session, "org.a11y.Bus", "/org/a11y/bus", PROPERTIES, "Get", "ss", "org.a11y.Status", "IsEnabled"),
            static (message, _) =>
            {
                var reader = message.GetBodyReader();
                reader.ReadSignature();
                return reader.ReadBool();
            }, null);

    private static async Task SetEnabledAsync(DBusConnection session, bool enabled)
    {
        MessageBuffer call;
        using (var writer = session.GetMessageWriter())
        {
            writer.WriteMethodCallHeader("org.a11y.Bus", "/org/a11y/bus", PROPERTIES, "Set", "ssv", MessageFlags.None);
            writer.WriteString("org.a11y.Status");
            writer.WriteString("IsEnabled");
            writer.WriteVariantBool(enabled);
            call = writer.CreateMessage();
        }

        await session.CallMethodAsync(call).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!_wasEnabled)
        {
            SetEnabledAsync(_session, false).GetAwaiter().GetResult();
        }

        _bus.Dispose();
        _session.Dispose();
    }
}
