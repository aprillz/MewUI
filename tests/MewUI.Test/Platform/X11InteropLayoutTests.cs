using System.Runtime.InteropServices;
using Aprillz.MewUI.Native;

namespace MewUI.Test.Platform;

[TestClass]
public sealed class X11InteropLayoutTests
{
    [TestMethod]
    public void XlibStructs_MatchLp64HeaderLayout()
    {
        if (IntPtr.Size != 8)
        {
            Assert.Inconclusive("The X11 backend currently targets the LP64 Xlib ABI.");
            return;
        }

        // Xlib Bool is a 4-byte int. These values mirror Xlib.h on LP64 and require no display.
        AssertOffset<XKeyEvent>(nameof(XKeyEvent.send_event), 16);
        AssertOffset<XKeyEvent>(nameof(XKeyEvent.same_screen), 88);
        Assert.AreEqual(96, Marshal.SizeOf<XKeyEvent>());

        AssertOffset<XMotionEvent>(nameof(XMotionEvent.send_event), 16);
        AssertOffset<XMotionEvent>(nameof(XMotionEvent.same_screen), 88);
        Assert.AreEqual(96, Marshal.SizeOf<XMotionEvent>());

        AssertOffset<XSetWindowAttributes>(nameof(XSetWindowAttributes.save_under), 64);
        AssertOffset<XSetWindowAttributes>(nameof(XSetWindowAttributes.override_redirect), 88);
        AssertOffset<XSetWindowAttributes>(nameof(XSetWindowAttributes.colormap), 96);
        Assert.AreEqual(112, Marshal.SizeOf<XSetWindowAttributes>());

        AssertOffset<XWindowAttributes>(nameof(XWindowAttributes.save_under), 72);
        AssertOffset<XWindowAttributes>(nameof(XWindowAttributes.map_installed), 88);
        AssertOffset<XWindowAttributes>(nameof(XWindowAttributes.override_redirect), 120);
        Assert.AreEqual(136, Marshal.SizeOf<XWindowAttributes>());

        AssertOffset<XIDeviceInfo>(nameof(XIDeviceInfo.enabled), 24);
        Assert.AreEqual(40, Marshal.SizeOf<XIDeviceInfo>());
        AssertOffset<XIDeviceEvent>(nameof(XIDeviceEvent.send_event), 16);

        AssertOffset<XComposeStatus>(nameof(XComposeStatus.chars_matched), 8);
        Assert.AreEqual(16, Marshal.SizeOf<XComposeStatus>());
    }

    private static void AssertOffset<T>(string field, int expected)
        => Assert.AreEqual(expected, Marshal.OffsetOf<T>(field).ToInt32(), $"{typeof(T).Name}.{field}");
}
