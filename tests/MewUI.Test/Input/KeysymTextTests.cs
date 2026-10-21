using Aprillz.MewUI.Platform.Linux;

namespace MewUI.Test.Input;

/// <summary>The text an X keysym stands for, which the X11 and framebuffer hosts report as a key's layout text.</summary>
[TestClass]
public sealed class KeysymTextTests
{
    [TestMethod]
    [DataRow(0x61L, "a")]
    [DataRow(0x41L, "A")]
    [DataRow(0x3FL, "?")]
    [DataRow(0xE9L, "é")]
    [DataRow(0x010020ACL, "€")]
    [DataRow(0xFE51L, "´")]
    [DataRow(0xFE52L, "^")]
    [DataRow(0xFFBEL, null)]
    [DataRow(0xFF0DL, null)]
    [DataRow(0x0L, null)]
    public void AKeysymTypesItsText(long keysym, string? expected)
        => Assert.AreEqual(expected, KeysymText.ToText(keysym));

    [TestMethod]
    public void DeadKeysAreTheDeadKeysyms()
    {
        Assert.IsTrue(KeysymText.IsDeadKeysym(0xFE51));
        Assert.IsFalse(KeysymText.IsDeadKeysym(0x27));
    }

    [TestMethod]
    public void AnOlderScriptKeysymNeedsXkbCommon()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("libxkbcommon is a Linux library.");
        }

        // Cyrillic_ef, on the A key of a Russian layout.
        Assert.AreEqual("ф", KeysymText.ToText(0x6C6));
    }
}
