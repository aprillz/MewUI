using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using Tmds.DBus.Protocol;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A tray icon on Linux publishes a status notifier item and a D-Bus menu. The test plays the watcher on a private
/// session bus, so it needs <c>dbus-run-session</c> and <c>MEWUI_TRAY_TEST_BUS=1</c>.
/// </summary>
[TestClass]
public sealed class LinuxTrayIconTests
{
    private const string WATCHER = "org.kde.StatusNotifierWatcher";
    private const string WATCHER_PATH = "/StatusNotifierWatcher";
    private const string ITEM = "org.kde.StatusNotifierItem";
    private const string ITEM_PATH = "/StatusNotifierItem";
    private const string MENU = "com.canonical.dbusmenu";
    private const string MENU_PATH = "/MenuBar";
    private const string PROPERTIES = "org.freedesktop.DBus.Properties";
    private const string BUS = "org.freedesktop.DBus";
    private const string BUS_PATH = "/org/freedesktop/DBus";
    private const string APPLICATION_ID = "net.mewui.TrayTest";

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OnLinux_TheItemRegistersWithALaterWatcher_AndExportsTheModel()
    {
        if (Environment.GetEnvironmentVariable("MEWUI_TRAY_TEST_BUS") != "1")
        {
            Assert.Inconclusive("run inside dbus-run-session with MEWUI_TRAY_TEST_BUS=1");
        }

        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            typeof(Application).GetProperty(nameof(Application.ApplicationId))!.SetValue(null, APPLICATION_ID);
            using var bus = new DBusConnection(DBusAddress.Session!);
            await bus.ConnectAsync();
            Assert.IsFalse(TrayIcon.IsSupported, "no watcher runs yet");

            var open = new Command("tray.linux.open", "_Open");
            var unhandled = new Command("tray.linux.unhandled");
            var glyph = new IconTemplate(static _ => new Border { Background = Color.Black });
            int ran = 0;
            var tray = new TrayIcon()
                .Icon(IconSource.FromBytes(BuildIcon()))
                .ToolTipText("Tray test")
                .ContextMenu(new ContextMenu()
                    .Add(new MenuItem(open).Icon(glyph))
                    .Item(unhandled)
                    .Separator()
                    .Add(new MenuItem("Check").IsCheckable().IsChecked())
                    .SubMenu("More", new ContextMenu().Item("Inner")))
                .IsVisible();
            tray.Commands.Register(open, () => ran++);

            var window = new Window { Title = "Tray class hint" };
            try
            {
                window.Show();
                await Task.Delay(300);
                var (instance, className) = X11.ReadClass(window.Handle);
                Assert.AreEqual(APPLICATION_ID, className);
                Assert.IsFalse(string.IsNullOrEmpty(instance), "the window has no instance name");

                var watcher = new FakeWatcher();
                bus.AddMethodHandler(watcher);
                Assert.AreEqual(1u, await RequestNameAsync(bus, WATCHER), "the private bus already has a watcher");

                string service = await watcher.Registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                StringAssert.StartsWith(service, "org.kde.StatusNotifierItem-");
                await WaitUntilAsync(() => TrayIcon.IsSupported, "the watcher was not noticed");

                Assert.AreEqual(APPLICATION_ID, (await GetPropertyAsync(bus, service, ITEM_PATH, ITEM, "Id")).GetString());
                Assert.AreEqual(MENU_PATH, (await GetPropertyAsync(bus, service, ITEM_PATH, ITEM, "Menu")).GetObjectPathAsString());

                var (width, height, argb) = await GetFirstPixmapAsync(bus, service);
                Assert.AreEqual(16, width);
                Assert.AreEqual(16, height);
                CollectionAssert.AreEqual(new byte[] { 255, 200, 20, 10 }, argb[..4], "pixels are ARGB in network byte order");

                var root = await GetLayoutAsync(bus, service);
                Assert.HasCount(5, root.Children);
                var first = root.Children[0];
                Assert.AreEqual("_Open", first.Properties["label"].GetString());
                Assert.IsTrue(first.Properties["enabled"].GetBool());
                AssertIsOpaqueBlackPng(first.Properties["icon-data"].GetArray<byte>());
                Assert.IsFalse(root.Children[1].Properties["enabled"].GetBool(), "a command without a handler is disabled");
                Assert.AreEqual("separator", root.Children[2].Properties["type"].GetString());
                Assert.AreEqual("checkmark", root.Children[3].Properties["toggle-type"].GetString());
                Assert.AreEqual(1, root.Children[3].Properties["toggle-state"].GetInt32());
                Assert.AreEqual("submenu", root.Children[4].Properties["children-display"].GetString());
                Assert.AreEqual("Inner", root.Children[4].Children[0].Properties["label"].GetString());

                await SendEventAsync(bus, service, first.Id);
                await WaitUntilAsync(() => ran == 1, "clicking the item did not run its command");

                // A menu replaced after the layout was read leaves the old ids dead.
                tray.ContextMenu = new ContextMenu().Item(open);
                await Task.Delay(100);
                await SendEventAsync(bus, service, first.Id);
                await Task.Delay(200);
                Assert.AreEqual(1, ran, "a click on an id from an older layout ran a command");

                tray.Dispose();
                await WaitUntilAsync(() => !HasOwnerAsync(bus, service).GetAwaiter().GetResult(), "disposing left the item on the bus");
            }
            finally
            {
                tray.Dispose();
                window.Close();
                typeof(Application).GetProperty(nameof(Application.ApplicationId))!.SetValue(null, null);
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OnLinux_BeforeAnyWindow_TheMenuOpensWithoutItsIcons()
    {
        if (Environment.GetEnvironmentVariable("MEWUI_TRAY_TEST_BUS") != "1")
        {
            Assert.Inconclusive("run inside dbus-run-session with MEWUI_TRAY_TEST_BUS=1");
        }

        await RealAppSession.RunAsync(async () =>
        {
            using var bus = new DBusConnection(DBusAddress.Session!);
            await bus.ConnectAsync();
            var watcher = new FakeWatcher();
            bus.AddMethodHandler(watcher);
            Assert.AreEqual(1u, await RequestNameAsync(bus, WATCHER), "the private bus already has a watcher");

            var glyph = new IconTemplate(static _ => new Border { Background = Color.Black });
            var tray = new TrayIcon().ContextMenu(new ContextMenu().Add(new MenuItem("Plain").Icon(glyph))).IsVisible();
            try
            {
                string service = await watcher.Registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var root = await GetLayoutAsync(bus, service);
                Assert.AreEqual("Plain", root.Children[0].Properties["label"].GetString());
            }
            finally
            {
                tray.Dispose();
            }
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail(message);
    }

    private static void AssertIsOpaqueBlackPng(byte[] png)
    {
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        int position = 8;
        int width = 0;
        using var compressed = new MemoryStream();
        while (position < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));
            string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position + 8));
            }
            else if (type == "IDAT")
            {
                compressed.Write(png, position + 8, length);
            }

            position += 12 + length;
        }

        compressed.Position = 0;
        using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
        using var pixels = new MemoryStream();
        inflate.CopyTo(pixels);
        var data = pixels.ToArray();
        Assert.HasCount((1 + width * 4) * width, data);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 255 }, data[(1 + width * 2)..(1 + width * 2 + 4)]);
    }

    /// <summary>A 16-pixel icon file of opaque BGRA(10, 20, 200, 255).</summary>
    private static byte[] BuildIcon()
    {
        const int SIZE = 16;
        const int DIB_HEADER = 40;
        const int FILE_HEADER = 22;
        int maskLength = 4 * SIZE;
        int imageLength = DIB_HEADER + SIZE * SIZE * 4 + maskLength;
        var icon = new byte[FILE_HEADER + imageLength];
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(4), 1);
        icon[6] = SIZE;
        icon[7] = SIZE;
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(12), 32);
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(14), imageLength);
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(18), FILE_HEADER);

        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(FILE_HEADER), DIB_HEADER);
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(FILE_HEADER + 4), SIZE);
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(FILE_HEADER + 8), SIZE * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(FILE_HEADER + 12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(FILE_HEADER + 14), 32);
        for (int offset = FILE_HEADER + DIB_HEADER; offset < FILE_HEADER + DIB_HEADER + SIZE * SIZE * 4; offset += 4)
        {
            icon[offset] = 10;
            icon[offset + 1] = 20;
            icon[offset + 2] = 200;
            icon[offset + 3] = 255;
        }

        return icon;
    }

    private static async Task<uint> RequestNameAsync(DBusConnection connection, string name)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(BUS, BUS_PATH, BUS, "RequestName", "su", MessageFlags.None);
            writer.WriteString(name);
            writer.WriteUInt32(4);
            call = writer.CreateMessage();
        }

        return await connection.CallMethodAsync(call, static (message, _) => message.GetBodyReader().ReadUInt32(), null);
    }

    private static async Task<bool> HasOwnerAsync(DBusConnection connection, string name)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(BUS, BUS_PATH, BUS, "NameHasOwner", "s", MessageFlags.None);
            writer.WriteString(name);
            call = writer.CreateMessage();
        }

        return await connection.CallMethodAsync(call, static (message, _) => message.GetBodyReader().ReadBool(), null).ConfigureAwait(false);
    }

    private static async Task<VariantValue> GetPropertyAsync(DBusConnection connection, string service, string path, string @interface, string property)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(service, path, PROPERTIES, "Get", "ss", MessageFlags.None);
            writer.WriteString(@interface);
            writer.WriteString(property);
            call = writer.CreateMessage();
        }

        return await connection.CallMethodAsync(call, static (message, _) => message.GetBodyReader().ReadVariantValue(), null);
    }

    private static async Task<(int Width, int Height, byte[] Argb)> GetFirstPixmapAsync(DBusConnection connection, string service)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(service, ITEM_PATH, PROPERTIES, "Get", "ss", MessageFlags.None);
            writer.WriteString(ITEM);
            writer.WriteString("IconPixmap");
            call = writer.CreateMessage();
        }

        return await connection.CallMethodAsync(call, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadSignature();
            var end = reader.ReadArrayStart(DBusType.Struct);
            Assert.IsTrue(reader.HasNext(end), "the item has no pixmap");
            reader.AlignStruct();
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            return (width, height, reader.ReadArrayOfByte());
        }, null);
    }

    private static async Task<LayoutNode> GetLayoutAsync(DBusConnection connection, string service)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(service, MENU_PATH, MENU, "GetLayout", "iias", MessageFlags.None);
            writer.WriteInt32(0);
            writer.WriteInt32(-1);
            writer.WriteArray(Array.Empty<string>());
            call = writer.CreateMessage();
        }

        return await connection.CallMethodAsync(call, static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadUInt32();
            return ReadNode(ref reader);
        }, null);
    }

    private static LayoutNode ReadNode(ref Reader reader)
    {
        reader.AlignStruct();
        var node = new LayoutNode { Id = reader.ReadInt32() };
        var properties = reader.ReadArrayStart(DBusType.DictEntry);
        while (reader.HasNext(properties))
        {
            reader.AlignStruct();
            string name = reader.ReadString();
            node.Properties[name] = reader.ReadVariantValue();
        }

        var children = reader.ReadArrayStart(DBusType.Variant);
        while (reader.HasNext(children))
        {
            reader.ReadSignature();
            node.Children.Add(ReadNode(ref reader));
        }

        return node;
    }

    private static async Task SendEventAsync(DBusConnection connection, string service, int id)
    {
        MessageBuffer call;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(service, MENU_PATH, MENU, "Event", "isvu", MessageFlags.None);
            writer.WriteInt32(id);
            writer.WriteString("clicked");
            writer.WriteVariantInt32(0);
            writer.WriteUInt32(0);
            call = writer.CreateMessage();
        }

        await connection.CallMethodAsync(call);
    }

    private sealed class LayoutNode
    {
        public int Id { get; init; }

        public Dictionary<string, VariantValue> Properties { get; } = new();

        public List<LayoutNode> Children { get; } = new();
    }

    private sealed class FakeWatcher : IPathMethodHandler
    {
        public TaskCompletionSource<string> Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Path => WATCHER_PATH;

        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var request = context.Request;
            if (request.InterfaceAsString == WATCHER && request.MemberAsString == "RegisterStatusNotifierItem")
            {
                var reader = request.GetBodyReader();
                Registered.TrySetResult(reader.ReadString());
                using var reply = context.CreateReplyWriter(null);
                context.Reply(reply.CreateMessage());
            }
            else if (request.InterfaceAsString == PROPERTIES && request.MemberAsString == "Get")
            {
                using var reply = context.CreateReplyWriter("v");
                reply.WriteVariantBool(true);
                context.Reply(reply.CreateMessage());
            }
            else
            {
                context.ReplyUnknownMethodError();
            }

            return default;
        }
    }

    private static class X11
    {
        private const string LIBRARY = "libX11.so.6";

        public static (string Instance, string Class) ReadClass(nint window)
        {
            nint display = XOpenDisplay(0);
            Assert.AreNotEqual(0, display, "no second X connection");
            try
            {
                Assert.AreNotEqual(0, XGetClassHint(display, window, out var hint), "the window has no WM_CLASS");
                string instance = Marshal.PtrToStringUTF8(hint.Name) ?? string.Empty;
                string className = Marshal.PtrToStringUTF8(hint.Class) ?? string.Empty;
                XFree(hint.Name);
                XFree(hint.Class);
                return (instance, className);
            }
            finally
            {
                XCloseDisplay(display);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ClassHint
        {
            public nint Name;
            public nint Class;
        }

        [DllImport(LIBRARY)]
        private static extern nint XOpenDisplay(nint name);

        [DllImport(LIBRARY)]
        private static extern int XCloseDisplay(nint display);

        [DllImport(LIBRARY)]
        private static extern int XGetClassHint(nint display, nint window, out ClassHint hint);

        [DllImport(LIBRARY)]
        private static extern int XFree(nint data);
    }
}
