// Ported from Windows Terminal src/terminal/adapter/ut_adapter/adapterTest.cpp (MIT).
// The device status, attribute, mode, setting, color and presentation state reports.

using System.Text;

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Adapter;

public sealed partial class AdapterTests
{
    // The VT525 default color table; the remaining slots of the table are black in the color report tests.
    private static readonly (byte Red, byte Green, byte Blue)[] _vt525Colors =
    [
        (0, 0, 0),
        (204, 36, 36),
        (51, 204, 51),
        (204, 204, 51),
        (51, 51, 204),
        (204, 51, 204),
        (51, 204, 204),
        (120, 120, 120),
        (69, 69, 69),
        (255, 0, 0),
        (0, 255, 0),
        (255, 255, 0),
        (0, 0, 255),
        (255, 0, 255),
        (0, 255, 255),
        (255, 255, 255),
    ];

    private delegate void AttributeEditor(ref TextAttribute attribute);

    private void SetVt525ColorTable(bool fillRemainingWithBlack)
    {
        for (int index = 0; index < _vt525Colors.Length; index++)
        {
            var (red, green, blue) = _vt525Colors[index];
            _testGetSet.RenderSettings.SetColorTableEntry(index, TextColor.Rgb(red, green, blue));
        }

        if (fillRemainingWithBlack)
        {
            for (int index = 16; index < TextColor.TABLE_SIZE; index++)
            {
                _testGetSet.RenderSettings.SetColorTableEntry(index, TextColor.Rgb(0, 0, 0));
            }
        }
    }

    private void RequestSetting(string settingId = "")
    {
        var handler = _dispatch.RequestSetting()!;
        foreach (char character in settingId)
        {
            handler(character);
        }

        // The string terminator.
        handler('\u001b');
    }

    private void SetCurrentAttributes(AttributeEditor edit)
    {
        var attribute = default(TextAttribute);
        edit(ref attribute);
        _testGetSet.ScreenBuffer.CurrentAttributes = attribute;
    }

    [TestMethod]
    public void DeviceStatus_OperatingStatusTests()
    {
        // Test 1: verify a good operating condition.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.OperatingStatus, VTParameter.Omitted);

        _testGetSet.ValidateInputEvent(CSI + "0n");
    }

    [TestMethod]
    public void DeviceStatus_CursorPositionReportTests()
    {
        // Test 1: verify the normal cursor response position.
        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);

        // VT positions are relative to the viewport top and 1-based.
        var cursorPosition = _testGetSet.ScreenBuffer.Cursor.Position;
        var expected = new TilPoint(cursorPosition.X + 1, cursorPosition.Y - _testGetSet.Viewport.Top + 1);

        _dispatch.DeviceStatusReport(StatusType.CursorPositionReport, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent($"{CSI}{expected.Y};{expected.X}R");

        // Test 2: verify multiple reports with a cursor move between them.
        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);

        // Retention keeps the two responses from deleting each other.
        _testGetSet.RetainResponse = true;

        cursorPosition = _testGetSet.ScreenBuffer.Cursor.Position;
        var expectedFirst = new TilPoint(cursorPosition.X + 1, cursorPosition.Y - _testGetSet.Viewport.Top + 1);

        _dispatch.DeviceStatusReport(StatusType.CursorPositionReport, VTParameter.Omitted);

        _testGetSet.ScreenBuffer.Cursor.SetPosition(new TilPoint(cursorPosition.X + 1, cursorPosition.Y + 1));

        var expectedSecond = new TilPoint(expectedFirst.X + 1, expectedFirst.Y + 1);

        _dispatch.DeviceStatusReport(StatusType.CursorPositionReport, VTParameter.Omitted);

        _testGetSet.ValidateInputEvent($"{CSI}{expectedFirst.Y};{expectedFirst.X}R{CSI}{expectedSecond.Y};{expectedSecond.X}R");
    }

    [TestMethod]
    public void DeviceStatus_ExtendedCursorPositionReportTests()
    {
        // Test 1: verify the extended cursor position report.
        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);

        var cursorPosition = _testGetSet.ScreenBuffer.Cursor.Position;
        var expected = new TilPoint(cursorPosition.X + 1, cursorPosition.Y - _testGetSet.Viewport.Top + 1);

        // The initial page number is 1.
        int expectedPage = 1;

        _dispatch.DeviceStatusReport(StatusType.ExtendedCursorPositionReport, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent($"{CSI}?{expected.Y};{expected.X};{expectedPage}R");

        // Now test with the page number set to 3.
        expectedPage = 3;
        _dispatch.PagePositionAbsolute(expectedPage);

        _dispatch.DeviceStatusReport(StatusType.ExtendedCursorPositionReport, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent($"{CSI}?{expected.Y};{expected.X};{expectedPage}R");
    }

    [TestMethod]
    public void DeviceStatus_MacroSpaceReportTest()
    {
        // Space is measured in blocks of 16 bytes; macros are not supported, so the space never shrinks.
        const int AVAILABLE_SPACE = 0x40000 / 16;

        // Test 1: verify the maximum space available.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.MacroSpaceReport, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent($"{CSI}{AVAILABLE_SPACE}*{{");

        // Test 3: verify the space after a reset.
        _testGetSet.PrepData();
        _dispatch.HardReset(true);
        _dispatch.DeviceStatusReport(StatusType.MacroSpaceReport, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent($"{CSI}{AVAILABLE_SPACE}*{{");
    }

    [TestMethod]
    public void DeviceStatus_MemoryChecksumReportTest()
    {
        // Test 1: verify the initial checksum is 0.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.MemoryChecksum, 12);
        _testGetSet.ValidateInputEvent(DCS + "12!~0000" + ST);

        // Test 3: verify the checksum is 0 after a reset.
        _testGetSet.PrepData();
        _dispatch.HardReset(true);
        _dispatch.DeviceStatusReport(StatusType.MemoryChecksum, 56);
        _testGetSet.ValidateInputEvent(DCS + "56!~0000" + ST);
    }

    [TestMethod]
    public void DeviceStatus_PrivateStatusTests()
    {
        // Test 1: verify the printer is not connected.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.PrinterStatus, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent(CSI + "?13n");

        // Test 2: verify UDKs are not supported.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.UserDefinedKeys, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent(CSI + "?23n");

        // Test 3: verify a PC keyboard with an unknown dialect.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.KeyboardStatus, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent(CSI + "?27;0;0;5n");

        // Test 4: verify the locator is not connected.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.LocatorStatus, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent(CSI + "?53n");

        // Test 5: verify the locator type is unknown.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.LocatorIdentity, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent(CSI + "?57;0n");

        // Test 6: verify the terminal is ready.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.DataIntegrity, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent(CSI + "?70n");

        // Test 7: verify multiple sessions are not supported.
        _testGetSet.PrepData();
        _dispatch.DeviceStatusReport(StatusType.MultipleSessionStatus, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent(CSI + "?83n");
    }

    [TestMethod]
    public void DeviceAttributesTests()
    {
        // Test 1: verify the normal response.
        _testGetSet.PrepData();
        _dispatch.DeviceAttributes();
        _testGetSet.ValidateInputEvent(CSI + "?61;4;6;7;14;21;22;23;24;28;32;42;52c");

        // Test 2: verify the response with the clipboard disabled.
        _testGetSet.PrepData();
        _dispatch.SetOptionalFeatures(TermDispatch.OptionalFeature.None);
        _dispatch.DeviceAttributes();
        _testGetSet.ValidateInputEvent(CSI + "?61;4;6;7;14;21;22;23;24;28;32;42c");

        // Test 3: verify the failure when ReturnResponse does not work.
        _testGetSet.PrepData();
        _testGetSet.ReturnResponseResult = false;

        Assert.Throws<InvalidOperationException>(() => _dispatch.DeviceAttributes());
    }

    [TestMethod]
    public void SecondaryDeviceAttributesTests()
    {
        // Test 1: verify the normal response.
        _testGetSet.PrepData();
        _dispatch.SecondaryDeviceAttributes();
        _testGetSet.ValidateInputEvent(CSI + ">0;10;1c");

        // Test 2: verify the failure when ReturnResponse does not work.
        _testGetSet.PrepData();
        _testGetSet.ReturnResponseResult = false;

        Assert.Throws<InvalidOperationException>(() => _dispatch.SecondaryDeviceAttributes());
    }

    [TestMethod]
    public void TertiaryDeviceAttributesTests()
    {
        // Test 1: verify the normal response.
        _testGetSet.PrepData();
        _dispatch.TertiaryDeviceAttributes();
        _testGetSet.ValidateInputEvent(DCS + "!|00000000" + ST);

        // Test 2: verify the failure when ReturnResponse does not work.
        _testGetSet.PrepData();
        _testGetSet.ReturnResponseResult = false;

        Assert.Throws<InvalidOperationException>(() => _dispatch.TertiaryDeviceAttributes());
    }

    [TestMethod]
    public void RequestDisplayedExtentTests()
    {
        // Test 1: verify the DECRQDE response in the home position.
        _testGetSet.PrepData();
        _testGetSet.Viewport.Left = 0;
        _testGetSet.Viewport.Right = 80;
        _testGetSet.Viewport.Top = 0;
        _testGetSet.Viewport.Bottom = 24;
        _dispatch.RequestDisplayedExtent();
        _testGetSet.ValidateInputEvent(CSI + "24;80;1;1;1\"w");

        // Test 2: verify the DECRQDE response when panned horizontally.
        _testGetSet.Viewport.Left += 5;
        _testGetSet.Viewport.Right += 5;
        _dispatch.RequestDisplayedExtent();
        _testGetSet.ValidateInputEvent(CSI + "24;80;6;1;1\"w");

        // Test 3: verify the DECRQDE response on page 3.
        _dispatch.PagePositionAbsolute(3);
        _dispatch.RequestDisplayedExtent();
        _testGetSet.ValidateInputEvent(CSI + "24;80;6;1;3\"w");

        // Test 3 (second): verify the DECRQDE response when the active page is not visible.
        _dispatch.ResetMode(ModeParams.DECPCCM_PageCursorCouplingMode);
        _dispatch.PagePositionAbsolute(1);
        _dispatch.RequestDisplayedExtent();
        _testGetSet.ValidateInputEvent(CSI + "24;80;6;1;3\"w");

        // Test 4: verify the DECRQDE response when page 1 is visible again.
        _dispatch.SetMode(ModeParams.DECPCCM_PageCursorCouplingMode);
        _dispatch.RequestDisplayedExtent();
        _testGetSet.ValidateInputEvent(CSI + "24;80;6;1;1\"w");
    }

    [TestMethod]
    public void RequestTerminalParametersTests()
    {
        // Test 1: verify the response for unsolicited permission.
        _testGetSet.PrepData();
        _dispatch.RequestTerminalParameters(ReportingPermission.Unsolicited);
        _testGetSet.ValidateInputEvent(CSI + "2;1;1;128;128;1;0x");

        // Test 2: verify the response for solicited permission.
        _testGetSet.PrepData();
        _dispatch.RequestTerminalParameters(ReportingPermission.Solicited);
        _testGetSet.ValidateInputEvent(CSI + "3;1;1;128;128;1;0x");

        // Test 3: verify the failure when ReturnResponse does not work.
        _testGetSet.PrepData();
        _testGetSet.ReturnResponseResult = false;
        Assert.Throws<InvalidOperationException>(() => _dispatch.RequestTerminalParameters(ReportingPermission.Unsolicited));
    }

    [TestMethod]
    public void RequestSettingsTests()
    {
        // DECSTBM margins (5 to 10).
        _testGetSet.PrepData();
        _dispatch.SetTopBottomScrollingMargins(5, 10);
        RequestSetting("r");
        _testGetSet.ValidateInputEvent(DCS + "1$r5;10r" + ST);

        // DECSTBM margins (full screen).
        _testGetSet.PrepData();

        // The screen height of 25 is the expected margin range.
        _testGetSet.Viewport.Bottom = _testGetSet.Viewport.Top + 25;
        _dispatch.SetTopBottomScrollingMargins(0, 0);
        RequestSetting("r");
        _testGetSet.ValidateInputEvent(DCS + "1$r1;25r" + ST);

        // DECSLRM margins (5 to 10).
        _testGetSet.PrepData();

        // Horizontal margins only work with DECLRMM enabled.
        _dispatch.SetMode(ModeParams.DECLRMM_LeftRightMarginMode);
        _dispatch.SetLeftRightScrollingMargins(5, 10);
        RequestSetting("s");
        _testGetSet.ValidateInputEvent(DCS + "1$r5;10s" + ST);

        // DECSLRM margins (full width).
        _testGetSet.PrepData();
        _dispatch.SetLeftRightScrollingMargins(0, 0);
        RequestSetting("s");
        _testGetSet.ValidateInputEvent(DCS + "1$r1;100s" + ST);
        _dispatch.ResetMode(ModeParams.DECLRMM_LeftRightMarginMode);

        // SGR attributes (default).
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.CurrentAttributes = default;
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0m" + ST);

        // SGR attributes (intense, underlined, reversed).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) =>
        {
            attribute.SetIntense(true);
            attribute.SetUnderlineStyle(UnderlineStyle.SinglyUnderlined);
            attribute.SetReverseVideo(true);
        });
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0;1;4;7m" + ST);

        // SGR attributes (extended underline style).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) => attribute.SetUnderlineStyle(UnderlineStyle.CurlyUnderlined));
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0;4:3m" + ST);

        // SGR attributes (faint, blinking, invisible).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) =>
        {
            attribute.SetFaint(true);
            attribute.SetBlinking(true);
            attribute.SetInvisible(true);
        });
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0;2;5;8m" + ST);

        // SGR attributes (italic, crossed-out).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) =>
        {
            attribute.SetItalic(true);
            attribute.SetCrossedOut(true);
        });
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0;3;9m" + ST);

        // SGR attributes (doubly underlined, overlined).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) =>
        {
            attribute.SetUnderlineStyle(UnderlineStyle.DoublyUnderlined);
            attribute.SetOverlined(true);
        });
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0;21;53m" + ST);

        // SGR attributes (standard colors).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) =>
        {
            attribute.SetIndexedForeground(TextColor.DARK_YELLOW);
            attribute.SetIndexedBackground(TextColor.DARK_CYAN);
        });
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0;33;46m" + ST);

        // SGR attributes (AIX colors).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) =>
        {
            attribute.SetIndexedForeground(TextColor.BRIGHT_CYAN);
            attribute.SetIndexedBackground(TextColor.BRIGHT_YELLOW);
        });
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0;96;103m" + ST);

        // SGR attributes (ITU indexed colors).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) =>
        {
            attribute.SetIndexedForeground256(123);
            attribute.SetIndexedBackground256(45);
            attribute.SetUnderlineColor(new TextColor(128, true));
        });
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0;38:5:123;48:5:45;58:5:128m" + ST);

        // SGR attributes (ITU RGB colors).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) =>
        {
            attribute.SetForeground(TextColor.Rgb(12, 34, 56));
            attribute.SetBackground(TextColor.Rgb(65, 43, 21));
            attribute.SetUnderlineColor(new TextColor(TextColor.Rgb(128, 222, 45)));
        });
        RequestSetting("m");
        _testGetSet.ValidateInputEvent(DCS + "1$r0;38:2::12:34:56;48:2::65:43:21;58:2::128:222:45m" + ST);

        // DECSCUSR style (blinking block).
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.Cursor.SetIsBlinking(true);
        _testGetSet.ScreenBuffer.Cursor.SetType(CursorType.FullBox);
        RequestSetting(" q");
        _testGetSet.ValidateInputEvent(DCS + "1$r1 q" + ST);

        // DECSCUSR style (steady block).
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.Cursor.SetIsBlinking(false);
        _testGetSet.ScreenBuffer.Cursor.SetType(CursorType.FullBox);
        RequestSetting(" q");
        _testGetSet.ValidateInputEvent(DCS + "1$r2 q" + ST);

        // DECSCUSR style (blinking underline).
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.Cursor.SetIsBlinking(true);
        _testGetSet.ScreenBuffer.Cursor.SetType(CursorType.Underscore);
        RequestSetting(" q");
        _testGetSet.ValidateInputEvent(DCS + "1$r3 q" + ST);

        // DECSCUSR style (steady underline).
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.Cursor.SetIsBlinking(false);
        _testGetSet.ScreenBuffer.Cursor.SetType(CursorType.Underscore);
        RequestSetting(" q");
        _testGetSet.ValidateInputEvent(DCS + "1$r4 q" + ST);

        // DECSCUSR style (blinking bar).
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.Cursor.SetIsBlinking(true);
        _testGetSet.ScreenBuffer.Cursor.SetType(CursorType.VerticalBar);
        RequestSetting(" q");
        _testGetSet.ValidateInputEvent(DCS + "1$r5 q" + ST);

        // DECSCUSR style (steady bar).
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.Cursor.SetIsBlinking(false);
        _testGetSet.ScreenBuffer.Cursor.SetType(CursorType.VerticalBar);
        RequestSetting(" q");
        _testGetSet.ValidateInputEvent(DCS + "1$r6 q" + ST);

        // DECSCUSR style (non-standard).
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.Cursor.SetIsBlinking(true);
        _testGetSet.ScreenBuffer.Cursor.SetType(CursorType.Legacy);
        RequestSetting(" q");
        _testGetSet.ValidateInputEvent(DCS + "1$r0 q" + ST);

        // DECSCA attributes (unprotected).
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.CurrentAttributes = default;
        RequestSetting("\"q");
        _testGetSet.ValidateInputEvent(DCS + "1$r0\"q" + ST);

        // DECSCA attributes (protected).
        _testGetSet.PrepData();
        SetCurrentAttributes((ref TextAttribute attribute) => attribute.SetProtected(true));
        RequestSetting("\"q");
        _testGetSet.ValidateInputEvent(DCS + "1$r1\"q" + ST);

        // Initialize the color alias indices for the DECAC tests below.
        _testGetSet.PrepData();
        var renderSettings = _testGetSet.RenderSettings;
        renderSettings.SetColorAliasIndex(ColorAlias.DefaultForeground, 3);
        renderSettings.SetColorAliasIndex(ColorAlias.DefaultBackground, 5);
        renderSettings.SetColorAliasIndex(ColorAlias.FrameForeground, 4);
        renderSettings.SetColorAliasIndex(ColorAlias.FrameBackground, 6);

        // DECAC colors (default).
        RequestSetting(",|");
        _testGetSet.ValidateInputEvent(DCS + "1$r1;3;5,|" + ST);

        // DECAC colors (normal text).
        RequestSetting("1,|");
        _testGetSet.ValidateInputEvent(DCS + "1$r1;3;5,|" + ST);

        // DECAC colors (window frame).
        RequestSetting("2,|");
        _testGetSet.ValidateInputEvent(DCS + "1$r2;4;6,|" + ST);

        // DECAC colors (invalid item).
        RequestSetting("3,|");
        _testGetSet.ValidateInputEvent(DCS + "0$r" + ST);

        // An unsupported setting.
        _testGetSet.PrepData();
        RequestSetting("x");
        _testGetSet.ValidateInputEvent(DCS + "0$r" + ST);
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(20)]
    public void RequestStandardModeTests(int modeNumber)
    {
        var mode = DispatchTypes.AnsiStandardMode(modeNumber);

        _testGetSet.PrepData();
        _dispatch.SetMode(mode);
        _dispatch.RequestMode(mode);
        _testGetSet.ValidateInputEvent($"{CSI}{modeNumber};1$y");

        _testGetSet.PrepData();
        _dispatch.ResetMode(mode);
        _dispatch.RequestMode(mode);
        _testGetSet.ValidateInputEvent($"{CSI}{modeNumber};2$y");
    }

    // Mode 2 (ANSI) is left out, since resetting it enters VT52 mode, where DECRQM does not apply; mode 9001 is
    // left out, since the extension does not implement win32-input-mode.
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(12)]
    [DataRow(25)]
    [DataRow(40)]
    [DataRow(66)]
    [DataRow(67)]
    [DataRow(69)]
    [DataRow(117)]
    [DataRow(1000)]
    [DataRow(1002)]
    [DataRow(1003)]
    [DataRow(1004)]
    [DataRow(1005)]
    [DataRow(1006)]
    [DataRow(1007)]
    [DataRow(1049)]
    [DataRow(2004)]
    public void RequestPrivateModeTests(int modeNumber)
    {
        var mode = DispatchTypes.DecPrivateMode(modeNumber);

        if (mode == ModeParams.DECCOLM_SetNumberOfColumns)
        {
            // DECCOLM has to be allowed.
            _dispatch.SetMode(ModeParams.XTERM_EnableDECCOLMSupport);
        }

        _testGetSet.PrepData();
        _dispatch.SetMode(mode);
        _dispatch.RequestMode(mode);
        _testGetSet.ValidateInputEvent($"{CSI}?{modeNumber};1$y");

        _testGetSet.PrepData();
        _dispatch.ResetMode(mode);
        _dispatch.RequestMode(mode);
        _testGetSet.ValidateInputEvent($"{CSI}?{modeNumber};2$y");
    }

    [TestMethod]
    [DataRow(2027)]
    public void RequestPermanentModeTests(int modeNumber)
    {
        var mode = DispatchTypes.DecPrivateMode(modeNumber);

        _testGetSet.PrepData();

        // A reset checks that the mode stays permanently enabled (3).
        _dispatch.ResetMode(mode);
        _dispatch.RequestMode(mode);
        _testGetSet.ValidateInputEvent($"{CSI}?{modeNumber};3$y");
    }

    [TestMethod]
    public void RequestChecksumReportTests()
    {
        void RequestChecksumReport(int length)
            => _stateMachine.ProcessString($"{CSI}99;1;1;1;1;{length}*y");

        void VerifyChecksumReport(string checksum)
            => _testGetSet.ValidateInputEvent($"{DCS}99!~{checksum}{ST}");

        void OutputText(string text)
        {
            _testGetSet.PrepData();
            _dispatch.PrintString(text);
            RequestChecksumReport(text.Length);
        }

        void OutputTextWithAttributes(string text, AttributeEditor edit)
        {
            _testGetSet.PrepData();
            SetCurrentAttributes(edit);
            _dispatch.PrintString(text);
            RequestChecksumReport(text.Length);
        }

        _dispatch.SetOptionalFeatures(TermDispatch.OptionalFeature.ChecksumReport);

        // Test 1: ASCII characters.
        OutputText("A");
        VerifyChecksumReport("FF4F");
        OutputText(" ");
        VerifyChecksumReport("FF70");
        OutputText("~");
        VerifyChecksumReport("FF12");
        OutputText("ABC");
        VerifyChecksumReport("FDEA");

        // Test 2: Latin-1 characters.
        OutputText("Á");
        VerifyChecksumReport("FECF");
        OutputText("¡");
        VerifyChecksumReport("FEEF");
        OutputText("ÿ");
        VerifyChecksumReport("FE91");
        OutputText("ÁÂÃ");
        VerifyChecksumReport("FC6A");

        // Test 3: rendition attributes.
        OutputTextWithAttributes("A", (ref TextAttribute attribute) => attribute.SetIntense(true));
        VerifyChecksumReport("FECF");
        OutputTextWithAttributes("A", (ref TextAttribute attribute) => attribute.SetUnderlineStyle(UnderlineStyle.SinglyUnderlined));
        VerifyChecksumReport("FF3F");
        OutputTextWithAttributes("A", (ref TextAttribute attribute) => attribute.SetBlinking(true));
        VerifyChecksumReport("FF0F");
        OutputTextWithAttributes("A", (ref TextAttribute attribute) => attribute.SetReverseVideo(true));
        VerifyChecksumReport("FF2F");
        OutputTextWithAttributes("A", (ref TextAttribute attribute) => attribute.SetInvisible(true));
        VerifyChecksumReport("FF47");
        OutputTextWithAttributes("A", (ref TextAttribute attribute) =>
        {
            attribute.SetIntense(true);
            attribute.SetUnderlineStyle(UnderlineStyle.SinglyUnderlined);
            attribute.SetReverseVideo(true);
        });
        VerifyChecksumReport("FE9F");

        // Test 4: selective erase.
        OutputTextWithAttributes("A", (ref TextAttribute attribute) => attribute.SetProtected(true));
        VerifyChecksumReport("FF4B");
        OutputTextWithAttributes("B", (ref TextAttribute attribute) => attribute.SetProtected(true));
        VerifyChecksumReport("FF4A");

        // Test 5: color attributes.
        OutputTextWithAttributes("A", (ref TextAttribute attribute) => attribute.SetIndexedForeground(TextColor.DARK_RED));
        VerifyChecksumReport("FFAF");
        OutputTextWithAttributes("A", (ref TextAttribute attribute) => attribute.SetIndexedBackground(TextColor.DARK_GREEN));
        VerifyChecksumReport("FF4D");
        OutputTextWithAttributes("A", (ref TextAttribute attribute) =>
        {
            attribute.SetIndexedForeground(TextColor.DARK_YELLOW);
            attribute.SetIndexedBackground(TextColor.DARK_BLUE);
        });
        VerifyChecksumReport("FF8B");
    }

    private static string BuildColorTableReport(string[] firstSixteen, string remainingValues, int model)
    {
        var report = new StringBuilder(DCS + "2$s");
        report.Append(string.Join('/', firstSixteen));
        for (int index = 16; index < TextColor.TABLE_SIZE; index++)
        {
            report.Append('/').Append(index).Append(';').Append(model).Append(';').Append(remainingValues);
        }

        report.Append(ST);
        return report.ToString();
    }

    [TestMethod]
    public void ColorTableReportTests()
    {
        _testGetSet.PrepData();
        SetVt525ColorTable(true);

        // HLS color model; for example RGB(204,36,36) is HLS(120,47%,70%).
        string[] hlsFirstSixteen =
        [
            "0;1;0;0;0",
            "1;1;120;47;70",
            "2;1;240;50;60",
            "3;1;180;50;60",
            "4;1;0;50;60",
            "5;1;60;50;60",
            "6;1;300;50;60",
            "7;1;0;47;0",
            "8;1;0;27;0",
            "9;1;120;50;100",
            "10;1;240;50;100",
            "11;1;180;50;100",
            "12;1;0;50;100",
            "13;1;60;50;100",
            "14;1;300;50;100",
            "15;1;0;100;0",
        ];

        _dispatch.RequestTerminalStateReport(ReportFormat.ColorTableReport, (int)ColorModel.HLS);
        _testGetSet.ValidateInputEvent(BuildColorTableReport(hlsFirstSixteen, "0;0;0", (int)ColorModel.HLS));

        // RGB color model; for example RGB(204,36,36) is RGB(80%,14%,14%).
        string[] rgbFirstSixteen =
        [
            "0;2;0;0;0",
            "1;2;80;14;14",
            "2;2;20;80;20",
            "3;2;80;80;20",
            "4;2;20;20;80",
            "5;2;80;20;80",
            "6;2;20;80;80",
            "7;2;47;47;47",
            "8;2;27;27;27",
            "9;2;100;0;0",
            "10;2;0;100;0",
            "11;2;100;100;0",
            "12;2;0;0;100",
            "13;2;100;0;100",
            "14;2;0;100;100",
            "15;2;100;100;100",
        ];

        _dispatch.RequestTerminalStateReport(ReportFormat.ColorTableReport, (int)ColorModel.RGB);
        _testGetSet.ValidateInputEvent(BuildColorTableReport(rgbFirstSixteen, "0;0;0", (int)ColorModel.RGB));
    }

    [TestMethod]
    public void Osc4ColorPaletteReportTests()
    {
        _testGetSet.PrepData();
        SetVt525ColorTable(true);

        string[] expectedColors =
        [
            "rgb:0000/0000/0000",
            "rgb:cccc/2424/2424",
            "rgb:3333/cccc/3333",
            "rgb:cccc/cccc/3333",
            "rgb:3333/3333/cccc",
            "rgb:cccc/3333/cccc",
            "rgb:3333/cccc/cccc",
            "rgb:7878/7878/7878",
            "rgb:4545/4545/4545",
            "rgb:ffff/0000/0000",
            "rgb:0000/ffff/0000",
            "rgb:ffff/ffff/0000",
            "rgb:0000/0000/ffff",
            "rgb:ffff/0000/ffff",
            "rgb:0000/ffff/ffff",
            "rgb:ffff/ffff/ffff",
        ];

        for (int index = 0; index < expectedColors.Length; index++)
        {
            _dispatch.RequestColorTableEntry(index);
            _testGetSet.ValidateInputEvent($"{OSC}4;{index};{expectedColors[index]}{ST}");
        }
    }

    [TestMethod]
    public void XtermColorResourceReportTests()
    {
        _testGetSet.PrepData();
        SetVt525ColorTable(false);

        var renderSettings = _testGetSet.RenderSettings;
        renderSettings.SetColorTableEntry(TextColor.DEFAULT_FOREGROUND, TextColor.Rgb(190, 190, 190));
        renderSettings.SetColorTableEntry(TextColor.DEFAULT_BACKGROUND, TextColor.Rgb(12, 12, 12));
        renderSettings.SetColorTableEntry(TextColor.CURSOR_COLOR, TextColor.Rgb(255, 0, 0));

        // The foreground is mapped to DARK_WHITE.
        _dispatch.RequestXtermColorResource(10);
        _testGetSet.ValidateInputEvent($"{OSC}10;rgb:7878/7878/7878{ST}");

        // The foreground is mapped to an independent foreground color.
        renderSettings.SetColorAliasIndex(ColorAlias.DefaultForeground, TextColor.DEFAULT_FOREGROUND);
        _dispatch.RequestXtermColorResource(10);
        _testGetSet.ValidateInputEvent($"{OSC}10;rgb:bebe/bebe/bebe{ST}");

        // The background is mapped to DARK_BLACK.
        _dispatch.RequestXtermColorResource(11);
        _testGetSet.ValidateInputEvent($"{OSC}11;rgb:0000/0000/0000{ST}");

        // The background is mapped to an independent background color.
        renderSettings.SetColorAliasIndex(ColorAlias.DefaultBackground, TextColor.DEFAULT_BACKGROUND);
        _dispatch.RequestXtermColorResource(11);
        _testGetSet.ValidateInputEvent($"{OSC}11;rgb:0c0c/0c0c/0c0c{ST}");

        // The foreground and background are mapped to different indices (for example through DECAC).
        _testGetSet.Response = string.Empty;
        _testGetSet.RetainResponse = true;
        renderSettings.SetColorAliasIndex(ColorAlias.DefaultForeground, TextColor.DARK_RED);
        renderSettings.SetColorAliasIndex(ColorAlias.DefaultBackground, TextColor.BRIGHT_GREEN);
        _dispatch.RequestXtermColorResource(10);
        _dispatch.RequestXtermColorResource(11);
        _testGetSet.ValidateInputEvent($"{OSC}10;rgb:cccc/2424/2424{ST}{OSC}11;rgb:0000/ffff/0000{ST}");
        _testGetSet.RetainResponse = false;

        _dispatch.RequestXtermColorResource(12);
        _testGetSet.ValidateInputEvent($"{OSC}12;rgb:ffff/0000/0000{ST}");

        // A resource set to an unrepresentable color gets no response.
        _testGetSet.Response = string.Empty;
        renderSettings.SetColorTableEntry(TextColor.CURSOR_COLOR, TextAttribute.INVALID_COLOR);
        _dispatch.RequestXtermColorResource(12);
        _testGetSet.ValidateInputEvent(string.Empty);

        // An unsupported resource has no mapped color, so it gets no response either.
        _testGetSet.Response = string.Empty;
        _dispatch.RequestXtermColorResource(13);
        _testGetSet.ValidateInputEvent(string.Empty);
    }

    [TestMethod]
    public void TabulationStopReportTests()
    {
        _testGetSet.PrepData();
        var textBuffer = _testGetSet.ScreenBuffer;

        // Default tab stops in 80-column mode.
        textBuffer.ResizeTraditional(new TilSize(80, 600));
        _dispatch.RequestPresentationStateReport(PresentationReportFormat.TabulationStopReport);
        _testGetSet.ValidateInputEvent(DCS + "2$u9/17/25/33/41/49/57/65/73" + ST);

        // Default tab stops in 132-column mode.
        textBuffer.ResizeTraditional(new TilSize(132, 600));
        _dispatch.RequestPresentationStateReport(PresentationReportFormat.TabulationStopReport);
        _testGetSet.ValidateInputEvent(DCS + "2$u9/17/25/33/41/49/57/65/73/81/89/97/105/113/121/129" + ST);

        // Custom tab stops in 80 columns.
        textBuffer.ResizeTraditional(new TilSize(80, 600));
        _stateMachine.ProcessString(DCS + "2$t30/60/120/240" + ST);
        _dispatch.RequestPresentationStateReport(PresentationReportFormat.TabulationStopReport);
        _testGetSet.ValidateInputEvent(DCS + "2$u30/60" + ST);

        // After expanding the width to 132 columns.
        textBuffer.ResizeTraditional(new TilSize(132, 600));
        _dispatch.RequestPresentationStateReport(PresentationReportFormat.TabulationStopReport);
        _testGetSet.ValidateInputEvent(DCS + "2$u30/60/120" + ST);
        textBuffer.ResizeTraditional(new TilSize(80, 600));

        // Out of order tab stops.
        _stateMachine.ProcessString(DCS + "2$t44/22/66" + ST);
        _dispatch.RequestPresentationStateReport(PresentationReportFormat.TabulationStopReport);
        _testGetSet.ValidateInputEvent(DCS + "2$u22/44/66" + ST);

        // Empty tab stops are ignored.
        _stateMachine.ProcessString(DCS + "2$t3//7" + ST);
        _dispatch.RequestPresentationStateReport(PresentationReportFormat.TabulationStopReport);
        _testGetSet.ValidateInputEvent(DCS + "2$u3/7" + ST);

        // '0' tab stops are ignored.
        _stateMachine.ProcessString(DCS + "2$t0/5/10" + ST);
        _dispatch.RequestPresentationStateReport(PresentationReportFormat.TabulationStopReport);
        _testGetSet.ValidateInputEvent(DCS + "2$u5/10" + ST);

        // '1' tab stops are ignored.
        _stateMachine.ProcessString(DCS + "2$t1/8/18" + ST);
        _dispatch.RequestPresentationStateReport(PresentationReportFormat.TabulationStopReport);
        _testGetSet.ValidateInputEvent(DCS + "2$u8/18" + ST);

        // Clear the tab stops.
        _dispatch.TabClear(TabClearType.ClearAllColumns);
        _dispatch.RequestPresentationStateReport(PresentationReportFormat.TabulationStopReport);
        _testGetSet.ValidateInputEvent(DCS + "2$u" + ST);
    }

    [TestMethod]
    public void CursorInformationReportTests()
    {
        _testGetSet.PrepData();
        var textBuffer = _testGetSet.ScreenBuffer;
        var termOutput = DispatchInspector.TermOutput(_dispatch);
        int viewportTop = _testGetSet.Viewport.Top;
        var attributes = default(TextAttribute);

        void VerifyReport(string expected)
        {
            _dispatch.RequestPresentationStateReport(PresentationReportFormat.CursorInformationReport);
            _testGetSet.ValidateInputEvent(DCS + "1$u" + expected + ST);
        }

        // Initial state.
        VerifyReport("1;1;1;@;@;@;0;2;@;BBBB");

        // Cursor position.
        textBuffer.Cursor.SetPosition(new TilPoint(3, viewportTop + 2));
        VerifyReport("3;4;1;@;@;@;0;2;@;BBBB");

        // Intense rendition.
        attributes.SetIntense(true);
        textBuffer.CurrentAttributes = attributes;
        VerifyReport("3;4;1;A;@;@;0;2;@;BBBB");

        // Underlined rendition.
        attributes.SetUnderlineStyle(UnderlineStyle.SinglyUnderlined);
        textBuffer.CurrentAttributes = attributes;
        VerifyReport("3;4;1;C;@;@;0;2;@;BBBB");

        // Blinking rendition.
        attributes.SetBlinking(true);
        textBuffer.CurrentAttributes = attributes;
        VerifyReport("3;4;1;G;@;@;0;2;@;BBBB");

        // Reverse video rendition.
        attributes.SetReverseVideo(true);
        textBuffer.CurrentAttributes = attributes;
        VerifyReport("3;4;1;O;@;@;0;2;@;BBBB");

        // Invisible rendition.
        attributes.SetInvisible(true);
        textBuffer.CurrentAttributes = attributes;
        VerifyReport("3;4;1;_;@;@;0;2;@;BBBB");

        // Protected attribute.
        attributes.SetProtected(true);
        textBuffer.CurrentAttributes = attributes;
        VerifyReport("3;4;1;_;A;@;0;2;@;BBBB");

        // Origin mode.
        _dispatch.SetMode(ModeParams.DECOM_OriginMode);
        VerifyReport("1;1;1;_;A;A;0;2;@;BBBB");

        // Single shift 2.
        _dispatch.SingleShift(2);
        VerifyReport("1;1;1;_;A;C;0;2;@;BBBB");

        // Single shift 3.
        _dispatch.SingleShift(3);
        VerifyReport("1;1;1;_;A;E;0;2;@;BBBB");

        // Delayed EOL wrap.
        _dispatch.CursorForward(999);
        _dispatch.Print('*');
        VerifyReport("1;100;1;_;A;I;0;2;@;BBBB");

        // Locking shifts.
        _dispatch.LockingShift(1);
        _dispatch.LockingShiftRight(3);
        VerifyReport("1;100;1;_;A;I;1;3;@;BBBB");

        // 94 charset designations.
        termOutput.AssignUserPreferenceCharset(VTID.FromString("%5"), false);
        _dispatch.Designate94Charset(0, VTID.FromString("%5"));
        _dispatch.Designate94Charset(1, VTID.FromString("<"));
        _dispatch.Designate94Charset(2, VTID.FromString("0"));
        _dispatch.Designate94Charset(3, VTID.FromString("K"));
        VerifyReport("1;100;1;_;A;I;1;3;@;%5<0K");

        // 96 charset designation (G1).
        _dispatch.Designate96Charset(1, VTID.FromString("H"));
        VerifyReport("1;100;1;_;A;I;1;3;B;%5H0K");

        // 96 charset designation (G2).
        _dispatch.Designate96Charset(2, VTID.FromString("M"));
        VerifyReport("1;100;1;_;A;I;1;3;F;%5HMK");

        // 96 charset designation (G3).
        _dispatch.Designate96Charset(3, VTID.FromString("B"));
        VerifyReport("1;100;1;_;A;I;1;3;N;%5HMB");

        // Restore the cursor position.
        _stateMachine.ProcessString(DCS + "1$t3;4;1;@;@;@;0;2;@;BBBB" + ST);
        Assert.AreEqual(new TilPoint(3, viewportTop + 2), textBuffer.Cursor.Position);

        // Restore the rendition attributes; U (0101 0101) is the bold, blinking and invisible attributes.
        _stateMachine.ProcessString(DCS + "1$t1;1;1;U;@;@;0;2;@;BBBB" + ST);
        attributes = default;
        attributes.SetIntense(true);
        attributes.SetBlinking(true);
        attributes.SetInvisible(true);
        Assert.AreEqual(attributes, textBuffer.CurrentAttributes);

        // J (0100 1010) is the underline and reverse video attributes.
        _stateMachine.ProcessString(DCS + "1$t1;1;1;J;A;@;0;2;@;BBBB" + ST);
        attributes = default;
        attributes.SetUnderlineStyle(UnderlineStyle.SinglyUnderlined);
        attributes.SetReverseVideo(true);
        attributes.SetProtected(true);
        Assert.AreEqual(attributes, textBuffer.CurrentAttributes);

        // Restore the flags.
        _stateMachine.ProcessString(DCS + "1$t1;1;1;@;@;E;0;2;@;BBBB" + ST);
        Assert.IsTrue(DispatchInspector.HasMode(_dispatch, "Origin"));
        Assert.IsFalse(termOutput.IsSingleShiftPending(2));
        Assert.IsTrue(termOutput.IsSingleShiftPending(3));
        Assert.IsFalse(textBuffer.Cursor.DelayEolWrap.HasValue);
        _stateMachine.ProcessString(DCS + "1$t1;1;1;@;@;J;0;2;@;BBBB" + ST);
        Assert.IsFalse(DispatchInspector.HasMode(_dispatch, "Origin"));
        Assert.IsTrue(termOutput.IsSingleShiftPending(2));
        Assert.IsFalse(termOutput.IsSingleShiftPending(3));
        Assert.IsTrue(textBuffer.Cursor.DelayEolWrap.HasValue);

        // Restore the charset configuration.
        _stateMachine.ProcessString(DCS + "1$t1;1;1;@;@;@;3;1;H;ABCF" + ST);
        Assert.AreEqual(3, termOutput.LeftSetNumber);
        Assert.AreEqual(1, termOutput.RightSetNumber);
        Assert.AreEqual(94, termOutput.GetCharsetSize(0));
        Assert.AreEqual(94, termOutput.GetCharsetSize(1));
        Assert.AreEqual(94, termOutput.GetCharsetSize(2));
        Assert.AreEqual(96, termOutput.GetCharsetSize(3));
        Assert.AreEqual(VTID.FromString("A"), termOutput.GetCharsetId(0));
        Assert.AreEqual(VTID.FromString("B"), termOutput.GetCharsetId(1));
        Assert.AreEqual(VTID.FromString("C"), termOutput.GetCharsetId(2));
        Assert.AreEqual(VTID.FromString("F"), termOutput.GetCharsetId(3));
    }

    [TestMethod]
    public void RequestUserPreferenceCharsets()
    {
        var termOutput = DispatchInspector.TermOutput(_dispatch);

        // DECRQUPSS for the 94 character sets (DEC Supplemental, Greek, Hebrew, Turkish and Cyrillic).
        foreach (string charsetId in new[] { "%5", "\"?", "\"4", "%0", "&4" })
        {
            _testGetSet.PrepData();
            termOutput.AssignUserPreferenceCharset(VTID.FromString(charsetId), false);
            _dispatch.RequestUserPreferenceCharset();
            _testGetSet.ValidateInputEvent($"{DCS}0!u{charsetId}{ST}");
        }

        // DECRQUPSS for the 96 character sets (ISO Latin-1, Latin-2, Greek, Hebrew, Cyrillic and Latin-5).
        foreach (string charsetId in new[] { "A", "B", "F", "H", "L", "M" })
        {
            _testGetSet.PrepData();
            termOutput.AssignUserPreferenceCharset(VTID.FromString(charsetId), true);
            _dispatch.RequestUserPreferenceCharset();
            _testGetSet.ValidateInputEvent($"{DCS}1!u{charsetId}{ST}");
        }
    }

    [TestMethod]
    public void WindowManipulationTypeTests()
    {
        // The pixel size reports are based on a virtual cell size of 10x20 pixels, for compatibility with the VT240 and VT340.
        var cellSize = new TilSize(10, 20);

        _testGetSet.PrepData();
        var (textBuffer, viewport, _) = _testGetSet.GetBufferAndViewport();

        _dispatch.WindowManipulation(WindowManipulationType.ReportTextSizeInCharacters, VTParameter.Omitted, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent($"{CSI}8;{viewport.Height};{textBuffer.Size.Width}t");

        _dispatch.WindowManipulation(WindowManipulationType.ReportTextSizeInPixels, VTParameter.Omitted, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent($"{CSI}4;{viewport.Height * cellSize.Height};{textBuffer.Size.Width * cellSize.Width}t");

        _dispatch.WindowManipulation(WindowManipulationType.ReportCharacterCellSize, VTParameter.Omitted, VTParameter.Omitted);
        _testGetSet.ValidateInputEvent($"{CSI}6;{cellSize.Height};{cellSize.Width}t");
    }

    [TestMethod]
    public void MenuCompletionsTests()
    {
        _testGetSet.PrepData();

        // Garbage and not enough parameters get no completions call.
        _dispatch.DoVsCodeAction("garbage");

        _dispatch.DoVsCodeAction("Completions");
        _dispatch.DoVsCodeAction("Completions;");
        _dispatch.DoVsCodeAction("Completions;10;");
        _dispatch.DoVsCodeAction("Completions;10;20");
        _dispatch.DoVsCodeAction("Completions;10;20;");

        // No trailing semicolon.
        _dispatch.DoVsCodeAction("Completions;10;20;3");

        // The normal, good case.
        _testGetSet.ExpectedMenuJson = "{ \"foo\": 1, \"bar\": 2 }";
        _testGetSet.ExpectedReplaceLength = 2;
        _dispatch.DoVsCodeAction("Completions;1;2;3;{ \"foo\": 1, \"bar\": 2 }");

        // The JSON has a semicolon in it.
        _testGetSet.ExpectedMenuJson = "{ \"foo\": \"what;ever\", \"bar\": 2 }";
        _testGetSet.ExpectedReplaceLength = 20;
        _dispatch.DoVsCodeAction("Completions;10;20;30;{ \"foo\": \"what;ever\", \"bar\": 2 }");
    }

    [TestMethod]
    public void PageMovementTests()
    {
        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);
        var pages = DispatchInspector.Pages(_dispatch);
        var startPosition = pages.ActivePage().Cursor.Position;
        var homePosition = new TilPoint(0, pages.ActivePage().Top);

        // Testing PPA (page position absolute).
        Assert.AreEqual(1, pages.ActivePage().Number, "Initial page is 1");
        _dispatch.PagePositionAbsolute(3);
        Assert.AreEqual(3, pages.ActivePage().Number, "PPA 3 moves to page 3");
        _dispatch.PagePositionAbsolute(VTParameter.Omitted);
        Assert.AreEqual(1, pages.ActivePage().Number, "PPA with an omitted page moves to 1");
        _dispatch.PagePositionAbsolute(9999);
        Assert.AreEqual(6, pages.ActivePage().Number, "PPA is clamped at page 6");
        Assert.AreEqual(startPosition, pages.ActivePage().Cursor.Position, "Cursor position never changes");

        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);

        // Reset to page 1.
        _dispatch.PagePositionAbsolute(1);

        // Testing PPR (page position relative).
        Assert.AreEqual(1, pages.ActivePage().Number, "Initial page is 1");
        _dispatch.PagePositionRelative(2);
        Assert.AreEqual(3, pages.ActivePage().Number, "PPR 2 moves forward 2 pages");
        _dispatch.PagePositionRelative(VTParameter.Omitted);
        Assert.AreEqual(4, pages.ActivePage().Number, "PPR with an omitted count moves forward 1");
        _dispatch.PagePositionRelative(9999);
        Assert.AreEqual(6, pages.ActivePage().Number, "PPR is clamped at page 6");
        Assert.AreEqual(startPosition, pages.ActivePage().Cursor.Position, "Cursor position never changes");

        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);

        // Testing PPB (page position back).
        Assert.AreEqual(6, pages.ActivePage().Number, "Initial page is 6");
        _dispatch.PagePositionBack(2);
        Assert.AreEqual(4, pages.ActivePage().Number, "PPB 2 moves back 2 pages");
        _dispatch.PagePositionBack(VTParameter.Omitted);
        Assert.AreEqual(3, pages.ActivePage().Number, "PPB with an omitted count moves back 1");
        _dispatch.PagePositionBack(9999);
        Assert.AreEqual(1, pages.ActivePage().Number, "PPB is clamped at page 1");
        Assert.AreEqual(startPosition, pages.ActivePage().Cursor.Position, "Cursor position never changes");

        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);

        // Testing NP (next page).
        Assert.AreEqual(1, pages.ActivePage().Number, "Initial page is 1");
        _dispatch.NextPage(2);
        Assert.AreEqual(3, pages.ActivePage().Number, "NP 2 moves forward 2 pages");
        _dispatch.NextPage(VTParameter.Omitted);
        Assert.AreEqual(4, pages.ActivePage().Number, "NP with an omitted count moves forward 1");
        _dispatch.NextPage(9999);
        Assert.AreEqual(6, pages.ActivePage().Number, "NP is clamped at page 6");
        Assert.AreEqual(homePosition, pages.ActivePage().Cursor.Position, "Cursor position is reset to home");

        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);

        // Testing PP (preceding page).
        Assert.AreEqual(6, pages.ActivePage().Number, "Initial page is 6");
        _dispatch.PrecedingPage(2);
        Assert.AreEqual(4, pages.ActivePage().Number, "PP 2 moves back 2 pages");
        _dispatch.PrecedingPage(VTParameter.Omitted);
        Assert.AreEqual(3, pages.ActivePage().Number, "PP with an omitted count moves back 1");
        _dispatch.PrecedingPage(9999);
        Assert.AreEqual(1, pages.ActivePage().Number, "PP is clamped at page 1");
        Assert.AreEqual(homePosition, pages.ActivePage().Cursor.Position, "Cursor position is reset to home");

        // Testing DECPCCM (page cursor coupling mode).
        _dispatch.SetMode(ModeParams.DECPCCM_PageCursorCouplingMode);
        _dispatch.PagePositionAbsolute(2);
        Assert.AreEqual(2, pages.ActivePage().Number);
        Assert.AreEqual(2, pages.VisiblePage().Number, "Visible page should follow active if DECPCCM set");
        _dispatch.ResetMode(ModeParams.DECPCCM_PageCursorCouplingMode);
        _dispatch.PagePositionAbsolute(4);
        Assert.AreEqual(4, pages.ActivePage().Number);
        Assert.AreEqual(2, pages.VisiblePage().Number, "Visible page should not change if DECPCCM reset");
        _dispatch.SetMode(ModeParams.DECPCCM_PageCursorCouplingMode);
        Assert.AreEqual(4, pages.ActivePage().Number);
        Assert.AreEqual(4, pages.VisiblePage().Number, "Active page should become visible when DECPCCM set");

        // Reset to page 1.
        _dispatch.PagePositionAbsolute(1);
    }

    [TestMethod]
    public void SendC1ControlTest()
    {
        const string S7C1T = "\u001b F";
        const string S8C1T = "\u001b G";

        _testGetSet.PrepData();

        // Generating reports with C1 control sequences.
        _stateMachine.ProcessString(S8C1T);

        _dispatch.SecondaryDeviceAttributes();
        _testGetSet.ValidateInputEvent("\u009b>0;10;1c");

        _dispatch.TertiaryDeviceAttributes();
        _testGetSet.ValidateInputEvent("\u0090!|00000000\u009c");

        _dispatch.RequestColorTableEntry(0);
        _testGetSet.ValidateInputEvent("\u009d4;0;rgb:0c0c/0c0c/0c0c\u009c");

        // Generating reports with 7-bit escape sequences.
        _stateMachine.ProcessString(S7C1T);

        _dispatch.SecondaryDeviceAttributes();
        _testGetSet.ValidateInputEvent(CSI + ">0;10;1c");

        _dispatch.TertiaryDeviceAttributes();
        _testGetSet.ValidateInputEvent(DCS + "!|00000000" + ST);

        _dispatch.RequestColorTableEntry(0);
        _testGetSet.ValidateInputEvent(OSC + "4;0;rgb:0c0c/0c0c/0c0c" + ST);
    }
}
