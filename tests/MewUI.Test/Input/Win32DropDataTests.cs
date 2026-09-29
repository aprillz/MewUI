using System.Runtime.InteropServices;
using System.Text;

using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;

using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using ComFormat = System.Runtime.InteropServices.ComTypes.FORMATETC;
using ComMedium = System.Runtime.InteropServices.ComTypes.STGMEDIUM;

namespace MewUI.Test.Input;

/// <summary>
/// The data of an OLE drag, read from shell data objects like the ones the file manager and other applications hand
/// over: every format listed under its clipboard name, and the standard formats derived from files, links and text.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Win32DropDataTests
{
    private static readonly Guid IID_ISHELLITEM = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IID_IDATAOBJECT = new("0000010e-0000-0000-C000-000000000046");
    private static readonly Guid BHID_DATAOBJECT = new("b8c0bd9f-ed24-455c-83e6-d5390c4fe8c4");

    [TestMethod]
    public void AFileDropListsItsPathAndFileUri()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("OLE data objects are Windows-only.");
            return;
        }

        string path = Path.Combine(Path.GetTempPath(), $"mewui-drop-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "x");
        nint source = FileDataObject(path);
        var data = new Win32DropDataObject(source);
        try
        {
            CollectionAssert.IsSubsetOf(new[] { "StorageItems", "Uris", "CF_HDROP" }, data.Formats.ToArray());
            Assert.IsTrue(data.TryGetData(DataFormats.StorageItems, out var paths));
            CollectionAssert.AreEqual(new[] { path }, paths.ToArray());
            Assert.IsTrue(data.TryGetData(DataFormats.Uris, out var uris));
            CollectionAssert.AreEqual(new[] { new Uri(path).AbsoluteUri }, uris.ToArray());
            Assert.IsTrue(data.TryGetData(DataFormats.FromPlatformName("CF_HDROP"), out var bytes));
            Assert.IsNotEmpty(bytes);
        }
        finally
        {
            data.Close();
            Marshal.Release(source);
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TextALinkAndHtmlReadAsStandardAndPlatformFormats()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("OLE data objects are Windows-only.");
            return;
        }

        byte[] html = Encoding.UTF8.GetBytes("Version:0.9\r\n<b>hi</b>");
        nint source = EmptyDataObject();
        var target = (ComDataObject)Marshal.GetObjectForIUnknown(source);
        SetGlobal(target, 13, Encoding.Unicode.GetBytes("hello text\0"));
        SetGlobal(target, RegisterClipboardFormat("UniformResourceLocatorW"), Encoding.Unicode.GetBytes("https://example.com/page\0"));
        SetGlobal(target, RegisterClipboardFormat("HTML Format"), html);

        var data = new Win32DropDataObject(source);
        try
        {
            CollectionAssert.AreEqual(
                new[] { "Uris", "Text", "CF_UNICODETEXT", "UniformResourceLocatorW", "HTML Format" },
                data.Formats.ToArray());
            Assert.IsTrue(data.TryGetData(DataFormats.Text, out var text));
            Assert.AreEqual("hello text", text);
            Assert.IsTrue(data.TryGetData(DataFormats.Uris, out var uris));
            CollectionAssert.AreEqual(new[] { "https://example.com/page" }, uris.ToArray());
            Assert.IsTrue(data.TryGetData(DataFormats.FromPlatformName("HTML Format"), out var bytes));
            CollectionAssert.AreEqual(html, bytes);

            data.Close();
            Assert.IsTrue(data.TryGetData(DataFormats.Text, out _), "a value read during the drag was dropped");
            Assert.IsFalse(data.TryGetData(DataFormats.FromPlatformName("CF_UNICODETEXT"), out _), "the source was read after the drag ended");
        }
        finally
        {
            data.Close();
            Marshal.Release(source);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static nint FileDataObject(string path)
    {
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, IID_ISHELLITEM, out var item));
        try
        {
            item.BindToHandler(0, BHID_DATAOBJECT, IID_IDATAOBJECT, out nint dataObject);
            return dataObject;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    private static nint EmptyDataObject()
    {
        Marshal.ThrowExceptionForHR(SHCreateDataObject(0, 0, 0, 0, IID_IDATAOBJECT, out nint dataObject));
        return dataObject;
    }

    private static void SetGlobal(ComDataObject target, int format, byte[] bytes)
    {
        nint memory = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, memory, bytes.Length);
        var request = new ComFormat
        {
            cfFormat = (short)format,
            dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
        };
        var medium = new ComMedium { tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL, unionmember = memory };
        target.SetData(ref request, ref medium, true);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint bindContext, in Guid handler, in Guid interfaceId, out nint result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, in Guid interfaceId, out IShellItem item);

    [DllImport("shell32.dll")]
    private static extern int SHCreateDataObject(nint folder, uint count, nint items, nint inner, in Guid interfaceId, out nint dataObject);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterClipboardFormat(string name);
}
