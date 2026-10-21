// Ported from Windows Terminal src/terminal/adapter/ut_adapter/adapterTest.cpp (MIT).
// The SGR, XTPUSHSGR and XTPOPSGR tests.

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Parser;

using static MewUI.Terminal.Test.Adapter.LegacyAttributes;

namespace MewUI.Terminal.Test.Adapter;

public sealed partial class AdapterTests
{
    public static IEnumerable<object[]> GraphicsOptionValues
    {
        get
        {
            int[] values =
            [
                0, 1, 2, 4, 7, 8, 9, 21, 22, 24, 27, 28, 29,
                30, 31, 32, 33, 34, 35, 36, 37, 39,
                40, 41, 42, 43, 44, 45, 46, 47, 49,
                53, 55,
                90, 91, 92, 93, 94, 95, 96, 97,
                100, 101, 102, 103, 104, 105, 106, 107,
            ];
            foreach (int value in values)
            {
                yield return new object[] { value };
            }
        }
    }

    private void ApplyGraphics(params int[] options)
    {
        var parameters = new VTParameter[options.Length];
        for (int index = 0; index < options.Length; index++)
        {
            parameters[index] = new VTParameter(options[index]);
        }

        _dispatch.SetGraphicsRendition(new VTParameters(parameters));
    }

    private void ApplyGraphicsWithSubParams(int option, params int[] subParamValues)
    {
        var (subParams, ranges) = TestGetSet.MakeSubParamsAndRanges(subParamValues);
        VTParameter[] options = [new VTParameter(option)];
        _dispatch.SetGraphicsRendition(new VTParameters(options, subParams, ranges));
    }

    private void PushGraphics(params int[] options)
    {
        var parameters = new VTParameter[options.Length];
        for (int index = 0; index < options.Length; index++)
        {
            parameters[index] = new VTParameter(options[index]);
        }

        _dispatch.PushGraphicsRendition(new VTParameters(parameters));
    }

    private TextAttribute CurrentAttributes => _testGetSet.ScreenBuffer.CurrentAttributes;

    [TestMethod]
    public void GraphicsBaseTests()
    {
        // Test 1: send no options.
        _testGetSet.PrepData();

        _dispatch.SetGraphicsRendition(new VTParameters(Array.Empty<VTParameter>()));
        _testGetSet.ValidateExpectedAttributes();
    }

    private static (TextAttribute Starting, TextAttribute Expected) CreateColorCase(int optionValue)
    {
        bool isForeground = optionValue is (>= 30 and <= 37) or (>= 90 and <= 97);
        bool isBright = optionValue >= 90;
        int ansiIndex = optionValue % 10;
        byte expectedIndex = (byte)(isBright ? ansiIndex + 8 : ansiIndex);

        // The starting attribute has the other two primaries of the new color set, which the new color replaces.
        int startingBits = 7 ^ TransposeIndex(ansiIndex);
        if (!isBright)
        {
            startingBits |= FOREGROUND_INTENSITY;
        }

        var starting = Create(isForeground ? startingBits : startingBits << 4);
        var expected = starting;
        if (isForeground)
        {
            expected.SetIndexedForeground(expectedIndex);
        }
        else
        {
            expected.SetIndexedBackground(expectedIndex);
        }

        return (starting, expected);
    }

    private static (TextAttribute Starting, TextAttribute Expected) CreateSingleCase(int optionValue)
    {
        var starting = Create(0);
        var expected = Create(0);

        switch ((GraphicsOptions)optionValue)
        {
            case GraphicsOptions.Off:
                // Everything but the default fill is set, so every attribute has to be cleared.
                starting = Create(0xFFFF & ~DEFAULT_FILL);
                expected = default;
                break;
            case GraphicsOptions.Intense:
                expected.SetIntense(true);
                break;
            case GraphicsOptions.RGBColorOrFaint:
                expected.SetFaint(true);
                break;
            case GraphicsOptions.Underline:
                expected.SetUnderlineStyle(UnderlineStyle.SinglyUnderlined);
                break;
            case GraphicsOptions.DoublyUnderlined:
                expected.SetUnderlineStyle(UnderlineStyle.DoublyUnderlined);
                break;
            case GraphicsOptions.Overline:
                expected = Create(COMMON_LVB_GRID_HORIZONTAL);
                break;
            case GraphicsOptions.Negative:
                expected = Create(COMMON_LVB_REVERSE_VIDEO);
                break;
            case GraphicsOptions.Invisible:
                expected.SetInvisible(true);
                break;
            case GraphicsOptions.CrossedOut:
                expected.SetCrossedOut(true);
                break;
            case GraphicsOptions.NotIntenseOrFaint:
                starting.SetIntense(true);
                starting.SetFaint(true);
                break;
            case GraphicsOptions.NoUnderline:
                starting.SetUnderlineStyle(UnderlineStyle.CurlyUnderlined);
                break;
            case GraphicsOptions.NoOverline:
                starting = Create(COMMON_LVB_GRID_HORIZONTAL);
                break;
            case GraphicsOptions.Positive:
                starting = Create(COMMON_LVB_REVERSE_VIDEO);
                break;
            case GraphicsOptions.Visible:
                starting.SetInvisible(true);
                break;
            case GraphicsOptions.NotCrossedOut:
                starting.SetCrossedOut(true);
                break;
            case GraphicsOptions.ForegroundDefault:
                // Every bit is set, so all the relevant ones have to flip.
                starting = Create(0xFFFF);
                expected = starting;
                expected.SetDefaultForeground();
                break;
            case GraphicsOptions.BackgroundDefault:
                starting = Create(0xFFFF);
                expected = starting;
                expected.SetDefaultBackground();
                break;
            default:
                return CreateColorCase(optionValue);
        }

        return (starting, expected);
    }

    [TestMethod]
    [DynamicData(nameof(GraphicsOptionValues))]
    public void GraphicsSingleTests(int optionValue)
    {
        _testGetSet.PrepData();

        var (starting, expected) = CreateSingleCase(optionValue);
        _testGetSet.ExpectedAttribute = expected;

        _testGetSet.ScreenBuffer.CurrentAttributes = starting;
        ApplyGraphics(optionValue);
        _testGetSet.ValidateExpectedAttributes();
    }

    [TestMethod]
    [DataRow((int)GraphicsOptions.Underline)]
    [DataRow((int)GraphicsOptions.ForegroundExtended)]
    [DataRow((int)GraphicsOptions.BackgroundExtended)]
    [DataRow((int)GraphicsOptions.UnderlineColor)]
    public void GraphicsSingleWithSubParamTests(int optionValue)
    {
        _testGetSet.PrepData();

        var expected = Create(0);
        switch ((GraphicsOptions)optionValue)
        {
            case GraphicsOptions.Underline:
                expected.SetUnderlineStyle(UnderlineStyle.CurlyUnderlined);
                ApplyGraphicsWithSubParamsAfterReset(optionValue, 3);
                break;
            case GraphicsOptions.ForegroundExtended:
                expected.SetIndexedForeground256(TextColor.DARK_RED);
                ApplyGraphicsWithSubParamsAfterReset(optionValue, (int)GraphicsOptions.BlinkOrXterm256Index, TextColor.DARK_RED);
                break;
            case GraphicsOptions.BackgroundExtended:
                expected.SetIndexedBackground256(TextColor.BRIGHT_WHITE);
                ApplyGraphicsWithSubParamsAfterReset(optionValue, (int)GraphicsOptions.BlinkOrXterm256Index, TextColor.BRIGHT_WHITE);
                break;
            case GraphicsOptions.UnderlineColor:
                expected.SetUnderlineColor(new TextColor(TextColor.DARK_RED, true));
                ApplyGraphicsWithSubParamsAfterReset(optionValue, (int)GraphicsOptions.BlinkOrXterm256Index, TextColor.DARK_RED);
                break;
            default:
                Assert.Fail("Test not implemented yet!");
                break;
        }

        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();
    }

    // Starts from the Create(0) attribute that the sub-parameter cases expect.
    private void ApplyGraphicsWithSubParamsAfterReset(int option, params int[] subParamValues)
    {
        _testGetSet.ScreenBuffer.CurrentAttributes = Create(0);
        ApplyGraphicsWithSubParams(option, subParamValues);
    }

    [TestMethod]
    public void GraphicsPushPopTests()
    {
        _testGetSet.PrepData();

        TextAttribute expected;

        // Test 1: basic push and pop.
        ApplyGraphics((int)GraphicsOptions.Off);
        _testGetSet.ExpectedAttribute = default;
        _testGetSet.ValidateExpectedAttributes();

        PushGraphics();

        _dispatch.PopGraphicsRendition();
        _testGetSet.ValidateExpectedAttributes();

        // Test 2: push, change the color, pop.
        PushGraphics();

        ApplyGraphics((int)GraphicsOptions.ForegroundCyan);
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_CYAN);
        expected.SetDefaultBackground();
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        _testGetSet.ExpectedAttribute = default;
        _dispatch.PopGraphicsRendition();
        _testGetSet.ValidateExpectedAttributes();

        // Test 3: two pushes (nested) and pops.
        PushGraphics();

        ApplyGraphics((int)GraphicsOptions.ForegroundRed);
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_RED);
        expected.SetDefaultBackground();
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        PushGraphics();

        ApplyGraphics((int)GraphicsOptions.ForegroundGreen);
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_GREEN);
        expected.SetDefaultBackground();
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // First pop.
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_RED);
        expected.SetDefaultBackground();
        _testGetSet.ExpectedAttribute = expected;
        _dispatch.PopGraphicsRendition();
        _testGetSet.ValidateExpectedAttributes();

        // Second pop.
        _testGetSet.ExpectedAttribute = default;
        _dispatch.PopGraphicsRendition();
        _testGetSet.ValidateExpectedAttributes();

        // Test 4: save and restore partial attributes.
        ApplyGraphics((int)GraphicsOptions.ForegroundGreen);
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_GREEN);
        expected.SetDefaultBackground();
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.Intense);
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_GREEN);
        expected.SetIntense(true);
        expected.SetDefaultBackground();
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.BackgroundBlue);
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_GREEN);
        expected.SetIndexedBackground(TextColor.DARK_BLUE);
        expected.SetIntense(true);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Push, saving only the background, the intensity and the double underline.
        PushGraphics(
            (int)SgrSaveRestoreStackOptions.Intense,
            (int)SgrSaveRestoreStackOptions.SaveBackgroundColor,
            (int)SgrSaveRestoreStackOptions.DoublyUnderlined);

        // Now change everything.
        ApplyGraphics((int)GraphicsOptions.BackgroundGreen, (int)GraphicsOptions.DoublyUnderlined);
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_GREEN);
        expected.SetIndexedBackground(TextColor.DARK_GREEN);
        expected.SetIntense(true);
        expected.SetUnderlineStyle(UnderlineStyle.DoublyUnderlined);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.ForegroundRed);
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_RED);
        expected.SetIndexedBackground(TextColor.DARK_GREEN);
        expected.SetIntense(true);
        expected.SetUnderlineStyle(UnderlineStyle.DoublyUnderlined);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.NotIntenseOrFaint);
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_RED);
        expected.SetIndexedBackground(TextColor.DARK_GREEN);
        expected.SetUnderlineStyle(UnderlineStyle.DoublyUnderlined);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // And then restore.
        expected = default;
        expected.SetIndexedForeground(TextColor.DARK_RED);
        expected.SetIndexedBackground(TextColor.DARK_BLUE);
        expected.SetIntense(true);
        _testGetSet.ExpectedAttribute = expected;
        _dispatch.PopGraphicsRendition();
        _testGetSet.ValidateExpectedAttributes();

        // Test 5: save the 'no singly underline' state, set singly underlined, and pop; singly underlined is off after the pop.
        expected = _testGetSet.ExpectedAttribute;
        ApplyGraphics((int)GraphicsOptions.NoUnderline);
        expected.SetUnderlineStyle(UnderlineStyle.NoUnderline);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        PushGraphics((int)SgrSaveRestoreStackOptions.Underline);

        ApplyGraphics((int)GraphicsOptions.Underline);
        expected.SetUnderlineStyle(UnderlineStyle.SinglyUnderlined);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        expected.SetUnderlineStyle(UnderlineStyle.NoUnderline);
        _testGetSet.ExpectedAttribute = expected;
        _dispatch.PopGraphicsRendition();
        _testGetSet.ValidateExpectedAttributes();

        // Test 6: save the 'no singly underlined' state, set doubly underlined, and pop; doubly underlined is retained.
        ApplyGraphics((int)GraphicsOptions.NoUnderline);
        expected.SetUnderlineStyle(UnderlineStyle.NoUnderline);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        PushGraphics((int)SgrSaveRestoreStackOptions.Underline);

        ApplyGraphics((int)GraphicsOptions.DoublyUnderlined);
        expected.SetUnderlineStyle(UnderlineStyle.DoublyUnderlined);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        _dispatch.PopGraphicsRendition();
        _testGetSet.ValidateExpectedAttributes();

        // Test 7: save the 'curly underlined' state, set doubly underlined, and pop; curly underlined is restored.
        ApplyGraphicsWithSubParams((int)GraphicsOptions.Underline, 3);
        expected.SetUnderlineStyle(UnderlineStyle.CurlyUnderlined);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        PushGraphics((int)SgrSaveRestoreStackOptions.Underline);

        ApplyGraphics((int)GraphicsOptions.DoublyUnderlined);
        expected.SetUnderlineStyle(UnderlineStyle.DoublyUnderlined);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        expected.SetUnderlineStyle(UnderlineStyle.CurlyUnderlined);
        _testGetSet.ExpectedAttribute = expected;
        _dispatch.PopGraphicsRendition();
        _testGetSet.ValidateExpectedAttributes();
    }

    [TestMethod]
    public void GraphicsPersistBrightnessTests()
    {
        _testGetSet.PrepData();

        TextAttribute expected;

        // Test 1: basic brightness test.
        ApplyGraphics((int)GraphicsOptions.Off);
        _testGetSet.ExpectedAttribute = default;
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.ForegroundBlue);
        expected = _testGetSet.ExpectedAttribute;
        expected.SetIndexedForeground(TextColor.DARK_BLUE);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Enabling brightness.
        ApplyGraphics((int)GraphicsOptions.Intense);
        expected.SetIntense(true);
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsTrue(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        // Foreground green, with brightness.
        ApplyGraphics((int)GraphicsOptions.ForegroundGreen);
        expected.SetIndexedForeground(TextColor.DARK_GREEN);
        _testGetSet.ExpectedAttribute = expected;
        Assert.AreEqual(new TextColor(TextColor.DARK_GREEN, false), CurrentAttributes.Foreground);
        Assert.IsTrue(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        // Test 2: disable brightness, use a bright color, and the next normal call remains not bright.
        ApplyGraphics((int)GraphicsOptions.Off);
        expected = default;
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsFalse(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.BrightForegroundBlue);
        expected.SetIndexedForeground(TextColor.BRIGHT_BLUE);
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsFalse(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        // The brightness of the 9x series does not persist.
        ApplyGraphics((int)GraphicsOptions.ForegroundBlue);
        expected.SetIndexedForeground(TextColor.DARK_BLUE);
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsFalse(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        // Test 3: enable brightness, use a bright color, and the brightness persists to the next normal call.
        ApplyGraphics((int)GraphicsOptions.Off);
        expected = default;
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsFalse(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.ForegroundBlue);
        expected.SetIndexedForeground(TextColor.DARK_BLUE);
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsFalse(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.Intense);
        expected.SetIntense(true);
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsTrue(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.BrightForegroundBlue);
        expected.SetIndexedForeground(TextColor.BRIGHT_BLUE);
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsTrue(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        // The brightness of the 9x series does not affect the brightness.
        ApplyGraphics((int)GraphicsOptions.ForegroundBlue);
        expected.SetIndexedForeground(TextColor.DARK_BLUE);
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsTrue(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();

        ApplyGraphics((int)GraphicsOptions.ForegroundGreen);
        expected.SetIndexedForeground(TextColor.DARK_GREEN);
        _testGetSet.ExpectedAttribute = expected;
        Assert.IsTrue(CurrentAttributes.IsIntense());
        _testGetSet.ValidateExpectedAttributes();
    }

    [TestMethod]
    public void Xterm256ColorTest()
    {
        _testGetSet.PrepData();

        var expected = CurrentAttributes;

        // Test 1: change the foreground.
        ApplyGraphics((int)GraphicsOptions.ForegroundExtended, (int)GraphicsOptions.BlinkOrXterm256Index, 2);
        expected.SetIndexedForeground256(TextColor.DARK_GREEN);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 2: change the background to a bright color.
        ApplyGraphics((int)GraphicsOptions.BackgroundExtended, (int)GraphicsOptions.BlinkOrXterm256Index, 9);
        expected.SetIndexedBackground256(TextColor.BRIGHT_RED);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 3: change the foreground to an arbitrary color.
        ApplyGraphics((int)GraphicsOptions.ForegroundExtended, (int)GraphicsOptions.BlinkOrXterm256Index, 42);
        expected.SetIndexedForeground256(42);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 4: change the background to an arbitrary color.
        ApplyGraphics((int)GraphicsOptions.BackgroundExtended, (int)GraphicsOptions.BlinkOrXterm256Index, 142);
        expected.SetIndexedBackground256(142);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 5: change the foreground to a legacy color while the background is an indexed color.
        ApplyGraphics((int)GraphicsOptions.ForegroundExtended, (int)GraphicsOptions.BlinkOrXterm256Index, 9);
        expected.SetIndexedForeground256(TextColor.BRIGHT_RED);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();
    }

    [TestMethod]
    public void XtermExtendedColorDefaultParameterTest()
    {
        _testGetSet.PrepData();

        var expected = CurrentAttributes;
        const int FOREGROUND_EXTENDED = (int)GraphicsOptions.ForegroundExtended;
        const int BACKGROUND_EXTENDED = (int)GraphicsOptions.BackgroundExtended;
        const int INDEXED = (int)GraphicsOptions.BlinkOrXterm256Index;
        const int RGB_COLOR = (int)GraphicsOptions.RGBColorOrFaint;
        const int OMITTED = TestGetSet.OMITTED;

        // Test 1: change the indexed foreground without the index parameter.
        ApplyGraphics(FOREGROUND_EXTENDED, INDEXED);
        expected.SetIndexedForeground256(TextColor.DARK_BLACK);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 2: change the indexed background with a default index parameter.
        ApplyGraphics(BACKGROUND_EXTENDED, INDEXED, OMITTED);
        expected.SetIndexedBackground256(TextColor.DARK_BLACK);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 3: change the RGB foreground with all RGB parameters missing.
        ApplyGraphics(FOREGROUND_EXTENDED, RGB_COLOR);
        expected.SetForeground(TextColor.Rgb(0, 0, 0));
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 4: change the RGB background with some RGB parameters missing.
        ApplyGraphics(BACKGROUND_EXTENDED, RGB_COLOR, 123);
        expected.SetBackground(TextColor.Rgb(123, 0, 0));
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 5: change the RGB foreground with some default RGB parameters.
        ApplyGraphics(FOREGROUND_EXTENDED, RGB_COLOR, OMITTED, OMITTED, 123);
        expected.SetForeground(TextColor.Rgb(0, 0, 123));
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 6: an RGB color is ignored when R, G or B is out of range (> 255).
        _testGetSet.PrepData();
        ApplyGraphics(FOREGROUND_EXTENDED, RGB_COLOR, 283, 182, 123);
        _testGetSet.ExpectedAttribute = Create(DEFAULT_FILL);
        _testGetSet.ValidateExpectedAttributes();

        // Test 7: an indexed color is ignored when the index is out of range (> 255).
        _testGetSet.PrepData();
        ApplyGraphics(FOREGROUND_EXTENDED, INDEXED, 283);
        _testGetSet.ExpectedAttribute = Create(DEFAULT_FILL);
        _testGetSet.ValidateExpectedAttributes();
    }

    [TestMethod]
    public void XtermExtendedSubParameterColorTest()
    {
        _testGetSet.PrepData();

        var expected = CurrentAttributes;
        const int FOREGROUND_EXTENDED = (int)GraphicsOptions.ForegroundExtended;
        const int BACKGROUND_EXTENDED = (int)GraphicsOptions.BackgroundExtended;
        const int OMITTED = TestGetSet.OMITTED;

        // Test 1: change the indexed foreground without the index sub-parameter.
        ApplyGraphicsWithSubParams(FOREGROUND_EXTENDED, 5);
        expected.SetIndexedForeground256(TextColor.DARK_BLACK);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 2: change the indexed background with a default index sub-parameter.
        ApplyGraphicsWithSubParams(BACKGROUND_EXTENDED, 5, OMITTED);
        expected.SetIndexedBackground256(TextColor.DARK_BLACK);
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 3: change the RGB foreground with all RGB sub-parameters missing.
        ApplyGraphicsWithSubParams(FOREGROUND_EXTENDED, 2);
        expected.SetForeground(TextColor.Rgb(0, 0, 0));
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 4: change the RGB background with some RGB sub-parameters missing.
        ApplyGraphicsWithSubParams(BACKGROUND_EXTENDED, 2, OMITTED, 123);
        expected.SetBackground(TextColor.Rgb(123, 0, 0));
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 5: change the RGB foreground with some default RGB sub-parameters.
        ApplyGraphicsWithSubParams(FOREGROUND_EXTENDED, 2, OMITTED, OMITTED, OMITTED, 123);
        expected.SetForeground(TextColor.Rgb(0, 0, 123));
        _testGetSet.ExpectedAttribute = expected;
        _testGetSet.ValidateExpectedAttributes();

        // Test 6: the color is ignored when the color space ID is not empty.
        _testGetSet.PrepData();
        ApplyGraphicsWithSubParams(FOREGROUND_EXTENDED, 2, 7, 182, 182, 123);
        _testGetSet.ExpectedAttribute = Create(DEFAULT_FILL);
        _testGetSet.ValidateExpectedAttributes();

        // Test 7: an RGB color is ignored when R, G or B is out of range (> 255).
        _testGetSet.PrepData();
        ApplyGraphicsWithSubParams(BACKGROUND_EXTENDED, 2, OMITTED, 128, 283, 155);
        _testGetSet.ExpectedAttribute = Create(DEFAULT_FILL);
        _testGetSet.ValidateExpectedAttributes();

        // Test 8: an indexed color is ignored when the index is out of range (> 255).
        _testGetSet.PrepData();
        ApplyGraphicsWithSubParams(FOREGROUND_EXTENDED, 5, 283);
        _testGetSet.ExpectedAttribute = Create(DEFAULT_FILL);
        _testGetSet.ValidateExpectedAttributes();
    }
}
