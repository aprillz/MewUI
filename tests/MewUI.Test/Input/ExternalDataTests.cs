using System.Text;

using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux.X11;

using MewUI.Test.Infrastructure;

namespace MewUI.Test.Input;

/// <summary>
/// Data from another application lists the standard formats it can be read as and every format the source offered,
/// reads each value on first request, and gives a typed format the same value as its name.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ExternalDataTests
{
    private const string URI_LIST = "file:///tmp/a.txt\r\nzip:///home/user/Mod.zip/en.json\n# comment\nsmb://server/share/b.txt\r\n";

    [TestMethod]
    public void ATypedFormatReadsTheValueStoredUnderItsName()
    {
        var payload = new List<int> { 1, 2 };
        var data = new DataObject(new Dictionary<string, object>
        {
            [StandardDataFormats.Text] = "hello",
            ["application/x-test"] = payload,
        });

        Assert.IsTrue(data.TryGetData(DataFormats.Text, out var text));
        Assert.AreEqual("hello", text);
        Assert.IsTrue(data.TryGetData(DataFormats.Create<List<int>>("application/x-test"), out var list));
        Assert.AreSame(payload, list);
        Assert.IsFalse(data.TryGetData(DataFormats.Uris, out _));

        var format = DataFormats.Create<List<int>>("application/x-other");
        data.SetData(format, payload);
        Assert.AreSame(payload, data.GetData("application/x-other"), "a value set by format is not stored under its name");
    }

    [TestMethod]
    public void OfferedTypesAreListedAfterTheStandardFormatsTheyProvide()
    {
        var (data, _) = CreateXdndData();

        CollectionAssert.AreEqual(
            new[] { "StorageItems", "Uris", "Text", "text/uri-list", "text/plain", "application/x-custom" },
            data.Formats.ToArray());
    }

    [TestMethod]
    public void ValuesAreReadOnFirstRequestAndOnlyOnce()
    {
        var (data, conversions) = CreateXdndData();
        Assert.IsEmpty(conversions, "the source was asked for data before anyone read it");

        Assert.IsTrue(data.TryGetData(DataFormats.Uris, out var uris));
        Assert.IsTrue(data.TryGetData(DataFormats.StorageItems, out var paths));

        CollectionAssert.AreEqual(
            new[] { "file:///tmp/a.txt", "zip:///home/user/Mod.zip/en.json", "smb://server/share/b.txt" },
            uris.ToArray(),
            "every URI is kept as sent, whatever its scheme and line end, without the comment");
        CollectionAssert.AreEqual(new[] { "/tmp/a.txt" }, paths.ToArray(), "only local files become paths");
        CollectionAssert.AreEqual(new[] { "text/uri-list" }, conversions, "the list was converted more than once");
    }

    [TestMethod]
    public void AnOfferedTypeReadsAsTheBytesSent()
    {
        var (data, _) = CreateXdndData();

        Assert.IsTrue(data.TryGetData(DataFormats.FromPlatformName("application/x-custom"), out var bytes));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, bytes);
        Assert.IsTrue(data.TryGetData(DataFormats.Text, out var text));
        Assert.AreEqual("plain", text);
    }

    [TestMethod]
    public void AValueNotReadBeforeTheDragEndsIsGone()
    {
        var (data, conversions) = CreateXdndData();
        Assert.IsTrue(data.TryGetData(DataFormats.Text, out _));

        data.Close();

        Assert.IsTrue(data.TryGetData(DataFormats.Text, out var text), "a value read during the drag was dropped");
        Assert.AreEqual("plain", text);
        Assert.IsFalse(data.TryGetData(DataFormats.Uris, out _), "the source was read after the drag ended");
        CollectionAssert.AreEqual(new[] { "text/plain" }, conversions);
    }

    [TestMethod]
    public void AUriListAloneIsAcceptedByDefault()
    {
        var args = DragOver(new DataObject(new Dictionary<string, object> { [StandardDataFormats.Uris] = new[] { "zip:///a/b.json" } }));

        Assert.IsTrue(args.Accepted);
        Assert.AreEqual(DragDropEffects.Copy, args.Effect);
    }

    [TestMethod]
    public void OnlyOfferedTypesAreNotAcceptedByDefault()
    {
        var args = DragOver(new DataObject(new Dictionary<string, object> { ["application/x-custom"] = new byte[] { 1 } }));

        Assert.IsFalse(args.Accepted, "a drag without a standard format was accepted without a handler");
    }

    private static (X11DropDataObject Data, List<string> Conversions) CreateXdndData()
    {
        var conversions = new List<string>();
        var offered = new List<(string Name, nint Atom)> { ("text/uri-list", 1), ("text/plain", 2), ("application/x-custom", 3) };
        var payloads = new Dictionary<nint, byte[]>
        {
            [1] = Encoding.UTF8.GetBytes(URI_LIST),
            [2] = Encoding.UTF8.GetBytes("plain"),
            [3] = [1, 2, 3],
        };

        var data = new X11DropDataObject(offered, atom =>
        {
            conversions.Add(offered.Single(entry => entry.Atom == atom).Name);
            return payloads[atom];
        });
        return (data, conversions);
    }

    private static DragEventArgs DragOver(IDataObject data)
    {
        var window = HeadlessWindow.Create(400, 300);
        window.AllowDrop = true;
        window.PerformLayout();
        var args = new DragEventArgs(data, new Point(50, 50), new Point(50, 50), DragDropEffects.Copy | DragDropEffects.Move);
        WindowDragDropRouter.OnExternalDragEnter(window, args);
        return args;
    }
}
