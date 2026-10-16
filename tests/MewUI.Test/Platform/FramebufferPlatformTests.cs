extern alias Framebuffer;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Framebuffer::Aprillz.MewUI.Native;
using Framebuffer::Aprillz.MewUI.Platform.Linux.Framebuffer;

using Mode = Framebuffer::Aprillz.MewUI.Platform.Linux.Framebuffer.FramebufferModeSelection.Mode;

namespace MewUI.Test.Platform;

[TestClass]
public sealed class FramebufferPlatformTests
{
    [TestMethod]
    public void SelectMode_PrefersThePreferredMode()
    {
        Mode[] modes =
        [
            new(3840, 2160, 30, IsPreferred: false),
            new(1920, 1080, 60, IsPreferred: true),
            new(1280, 720, 60, IsPreferred: false),
        ];

        Assert.AreEqual(1, FramebufferModeSelection.SelectMode(modes, requested: null));
    }

    [TestMethod]
    public void SelectMode_WithoutPreferred_TakesLargestAreaThenFastestRefresh()
    {
        Mode[] modes =
        [
            new(1280, 720, 60, IsPreferred: false),
            new(1920, 1080, 50, IsPreferred: false),
            new(1920, 1080, 60, IsPreferred: false),
        ];

        Assert.AreEqual(2, FramebufferModeSelection.SelectMode(modes, requested: null));
    }

    [TestMethod]
    public void SelectMode_RequestedSize_TakesFastestRefreshOrFails()
    {
        Mode[] modes =
        [
            new(1024, 600, 50, IsPreferred: false),
            new(1920, 1080, 60, IsPreferred: true),
            new(1024, 600, 60, IsPreferred: false),
        ];

        Assert.AreEqual(2, FramebufferModeSelection.SelectMode(modes, (1024, 600)));
        Assert.AreEqual(-1, FramebufferModeSelection.SelectMode(modes, (800, 480)));
    }

    [TestMethod]
    public void SwapRedBlue_ExchangesFirstAndThirdBytes()
    {
        byte[] source = [1, 2, 3, 4, 10, 20, 30, 40];
        byte[] destination = new byte[source.Length];

        FramebufferPixelCopy.SwapRedBlue(source, destination);

        CollectionAssert.AreEqual(new byte[] { 3, 2, 1, 4, 30, 20, 10, 40 }, destination);
    }

    [TestMethod]
    public void ToRgb565_PacksHighBitsOfEachChannel()
    {
        // BGRA: pure red, pure green, pure blue, white.
        byte[] source = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255];
        byte[] destination = new byte[8];

        FramebufferPixelCopy.ToRgb565(source, destination);

        CollectionAssert.AreEqual(new byte[] { 0x00, 0xF8, 0xE0, 0x07, 0x1F, 0x00, 0xFF, 0xFF }, destination);
    }

    [TestMethod]
    public void ArrowCursor_HasOpaqueHotSpotAndClearedRest()
    {
        const int size = 64;
        uint[] pixels = new uint[size * size];
        Array.Fill(pixels, 0xDEADBEEFu);

        FramebufferCursorImage.RenderArrow(pixels, size);

        Assert.AreEqual(0xFF000000u, pixels[0]);
        Assert.AreEqual(0xFFFFFFFFu, pixels[2 * size + 1]);
        Assert.AreEqual(0u, pixels[size - 1]);
        Assert.AreEqual(0u, pixels[size * size - 1]);
    }

    [TestMethod]
    public void SpecialKeys_MapToTheirKeysyms()
    {
        Assert.AreEqual(0xFF1BL, FramebufferKeyCodes.KeysymOfSpecialKey(1));
        Assert.AreEqual(0xFFBEL, FramebufferKeyCodes.KeysymOfSpecialKey(59));
        Assert.AreEqual(0xFFC7L, FramebufferKeyCodes.KeysymOfSpecialKey(68));
        Assert.AreEqual(0xFFC9L, FramebufferKeyCodes.KeysymOfSpecialKey(88));
        Assert.AreEqual(0L, FramebufferKeyCodes.KeysymOfSpecialKey(30));
    }

    [TestMethod]
    public void KmsAndFbdevStructs_MatchLp64HeaderLayout()
    {
        if (IntPtr.Size != 8)
        {
            Assert.Inconclusive("The framebuffer platform targets the LP64 ABI.");
            return;
        }

        // Values mirror xf86drmMode.h, xf86drm.h and linux/fb.h on LP64.
        Assert.AreEqual(68, Marshal.SizeOf<DrmModeModeInfo>());

        AssertOffset<DrmModeRes>(nameof(DrmModeRes.Connectors), 40);
        AssertOffset<DrmModeRes>(nameof(DrmModeRes.MaxHeight), 76);
        Assert.AreEqual(80, Marshal.SizeOf<DrmModeRes>());

        AssertOffset<DrmModeConnector>(nameof(DrmModeConnector.Modes), 40);
        AssertOffset<DrmModeConnector>(nameof(DrmModeConnector.CountEncoders), 72);
        AssertOffset<DrmModeConnector>(nameof(DrmModeConnector.Encoders), 80);
        Assert.AreEqual(88, Marshal.SizeOf<DrmModeConnector>());

        AssertOffset<DrmModeCrtc>(nameof(DrmModeCrtc.Mode), 28);
        Assert.AreEqual(100, Marshal.SizeOf<DrmModeCrtc>());

        Assert.AreEqual(40, Unsafe.SizeOf<DrmEventContext>());

        Assert.AreEqual(160, Marshal.SizeOf<FbVarScreenInfo>());
        AssertOffset<FbVarScreenInfo>(nameof(FbVarScreenInfo.Red), 32);
        Assert.AreEqual(80, Marshal.SizeOf<FbFixScreenInfo>());
        AssertOffset<FbFixScreenInfo>(nameof(FbFixScreenInfo.LineLength), 48);
    }

    private static void AssertOffset<T>(string field, int expected)
        => Assert.AreEqual(expected, Marshal.OffsetOf<T>(field).ToInt32(), $"{typeof(T).Name}.{field}");
}
