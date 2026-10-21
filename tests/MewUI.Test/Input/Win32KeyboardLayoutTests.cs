using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Platform.Win32;

namespace MewUI.Test.Input;

/// <summary>
/// The Win32 layout table gives each key the text it types, the third level under Ctrl+Alt, and a dead key's spacing
/// form. Layouts the machine does not have are loaded for the test and unloaded after it; the user's own stay.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Win32KeyboardLayoutTests
{
    private const string US = "00000409";
    private const string GERMAN = "00000407";
    private const string RUSSIAN = "00000419";
    private const string US_INTERNATIONAL = "00020409";
    private const uint KLF_NOTELLSHELL = 0x80;

    private const int VK_A = 0x41;
    private const int VK_Q = 0x51;
    private const int VK_1 = 0x31;
    private const int VK_F5 = 0x74;
    private const int VK_RETURN = 0x0D;
    private const int VK_OEM_6 = 0xDD;
    private const int VK_OEM_7 = 0xDE;
    private const int VK_SPACE = 0x20;

    private static readonly List<nint> _loadedByTest = new();
    private static readonly Dictionary<string, nint> _layouts = new();

    [ClassInitialize]
    public static void LoadLayouts(TestContext context)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var before = LayoutList();
        foreach (string id in new[] { US, GERMAN, RUSSIAN, US_INTERNATIONAL })
        {
            nint handle = LoadKeyboardLayoutW(id, KLF_NOTELLSHELL);
            _layouts[id] = handle;
            if (handle != 0 && !before.Contains(handle))
            {
                _loadedByTest.Add(handle);
            }
        }
    }

    [ClassCleanup]
    public static void UnloadLayouts()
    {
        foreach (nint handle in _loadedByTest)
        {
            UnloadKeyboardLayout(handle);
        }

        _loadedByTest.Clear();
    }

    [TestMethod]
    public void ALetterTypesItsLetterWithShiftAndCapsLock()
    {
        var layout = Layout(US);

        Assert.AreEqual("a", layout.GetCell(VK_A, shift: false, capsLock: false, controlAlt: false).Text);
        Assert.AreEqual("A", layout.GetCell(VK_A, shift: true, capsLock: false, controlAlt: false).Text);
        Assert.AreEqual("A", layout.GetCell(VK_A, shift: false, capsLock: true, controlAlt: false).Text);
        Assert.AreEqual("a", layout.GetCell(VK_A, shift: true, capsLock: true, controlAlt: false).Text);
        Assert.AreEqual("!", layout.GetCell(VK_1, shift: true, capsLock: false, controlAlt: false).Text);
    }

    [TestMethod]
    public void KeysWithoutTextHaveNone()
    {
        var layout = Layout(US);

        Assert.IsNull(layout.GetCell(VK_F5, shift: false, capsLock: false, controlAlt: false).Text);
        Assert.IsNull(layout.GetCell(VK_RETURN, shift: false, capsLock: false, controlAlt: false).Text, "Enter translates to a control character");
    }

    [TestMethod]
    public void ControlAndAltIgnoredWhereTheLayoutHasNoThirdLevel()
    {
        var symbols = Layout(US).Lookup(VK_Q, shift: false, capsLock: false, controlAlt: true);

        Assert.AreEqual("q", symbols.KeySymbol);
        Assert.AreEqual("q", symbols.BaseKeySymbol);
        Assert.IsFalse(symbols.TypesThirdLevel);
    }

    [TestMethod]
    public void ControlAndAltTypeTheThirdLevel()
    {
        var symbols = Layout(GERMAN).Lookup(VK_Q, shift: false, capsLock: false, controlAlt: true);

        Assert.AreEqual("@", symbols.KeySymbol);
        Assert.AreEqual("q", symbols.BaseKeySymbol);
        Assert.IsTrue(symbols.TypesThirdLevel);
    }

    [TestMethod]
    public void ADeadKeyHasNoKeySymbolAndItsSpacingFormAsTheBase()
    {
        var german = Layout(GERMAN).Lookup(VK_OEM_6, shift: false, capsLock: false, controlAlt: false);
        Assert.IsNull(german.KeySymbol);
        Assert.AreEqual("´", german.BaseKeySymbol);

        var international = Layout(US_INTERNATIONAL).Lookup(VK_OEM_7, shift: false, capsLock: false, controlAlt: false);
        Assert.IsNull(international.KeySymbol);
        Assert.AreEqual("'", international.BaseKeySymbol);
    }

    [TestMethod]
    public void ANonLatinLayoutGivesItsOwnLetter()
    {
        var symbols = Layout(RUSSIAN).Lookup(VK_A, shift: false, capsLock: false, controlAlt: false);

        Assert.AreEqual("ф", symbols.KeySymbol);
        Assert.AreEqual("ф", symbols.BaseKeySymbol);
    }

    [TestMethod]
    public void APendingDeadKeyIsDetectedWithoutBeingConsumed()
    {
        Layout(GERMAN);
        nint german = _layouts[GERMAN];
        Assert.IsFalse(Win32KeyboardLayout.IsDeadKeyPending(german));

        // A dead key typed into this thread's keyboard state; Space types its spacing form and clears it.
        Assert.IsLessThan(0, Translate(german, VK_OEM_6), "´ is a dead key");
        try
        {
            Assert.IsTrue(Win32KeyboardLayout.IsDeadKeyPending(german), "a table read now would hold the dead key's combinations");
            Assert.IsTrue(Win32KeyboardLayout.IsDeadKeyPending(german), "the check does not consume the dead key");
        }
        finally
        {
            Translate(german, VK_SPACE);
        }

        Assert.IsFalse(Win32KeyboardLayout.IsDeadKeyPending(german));
    }

    [TestMethod]
    [DataRow(true, true, ModifierKeys.None)]
    [DataRow(false, true, ModifierKeys.Control | ModifierKeys.Alt)]
    [DataRow(true, false, ModifierKeys.Control | ModifierKeys.Alt)]
    public void AltGrTypingACharacterReportsNoControlOrAlt(bool rightAltDown, bool typesThirdLevel, ModifierKeys expected)
    {
        var modifiers = Win32WindowBackend.FilterAltGrModifiers(ModifierKeys.Control | ModifierKeys.Alt, rightAltDown, typesThirdLevel);

        Assert.AreEqual(expected, modifiers);
    }

    [TestMethod]
    public void AltGrKeepsShift()
    {
        var modifiers = Win32WindowBackend.FilterAltGrModifiers(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, rightAltDown: true, typesThirdLevel: true);

        Assert.AreEqual(ModifierKeys.Shift, modifiers);
    }

    private static Win32KeyboardLayout Layout(string id)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Keyboard layouts are Windows-only.");
        }

        var layout = Win32KeyboardLayout.Get(_layouts[id]);
        Assert.IsNotNull(layout, $"layout {id}");
        return layout;
    }

    private static int Translate(nint layout, int virtualKey)
    {
        uint scanCode = MapVirtualKeyExW((uint)virtualKey, 0, layout);
        return ToUnicodeEx((uint)virtualKey, scanCode, new byte[256], new char[8], 8, 0, layout);
    }

    private static nint[] LayoutList()
    {
        int count = GetKeyboardLayoutList(0, null);
        var list = new nint[count];
        GetKeyboardLayoutList(count, list);
        return list;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadKeyboardLayoutW(string layoutId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnloadKeyboardLayout(nint handle);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? list);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyExW(uint code, uint mapType, nint layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState, [Out] char[] buffer, int bufferSize, uint flags, nint layout);
}
