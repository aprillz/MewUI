// Ported from Windows Terminal src/terminal/parser/ut_parser/OutputEngineTest.cpp (MIT).
// Left out: nothing; every TEST_METHOD targets the output engine or the state machine.
// The state machine exposes no state accessors, so the tests that inspect StateMachine::_state, _parameters,
// _subParameters, _subParameterRanges, _oscString and _oscParameter read those private fields by reflection.

using System.Reflection;
using System.Text;

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Parser;

namespace MewUI.Terminal.Test.Parser;

internal static class MachineInspector
{
    private const BindingFlags PRIVATE_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;

    private static FieldInfo Field(string name)
        => typeof(StateMachine).GetField(name, PRIVATE_INSTANCE) ?? throw new InvalidOperationException("Missing field " + name);

    public static string GetState(StateMachine machine) => Field("_state").GetValue(machine)!.ToString()!;

    public static void SetState(StateMachine machine, string stateName)
    {
        var field = Field("_state");
        field.SetValue(machine, Enum.Parse(field.FieldType, stateName));
    }

    public static void SetDcsStringHandler(StateMachine machine, StringHandler handler) => Field("_dcsStringHandler").SetValue(machine, handler);

    public static List<VTParameter> Parameters(StateMachine machine) => (List<VTParameter>)Field("_parameters").GetValue(machine)!;

    public static List<VTParameter> SubParameters(StateMachine machine) => (List<VTParameter>)Field("_subParameters").GetValue(machine)!;

    public static List<(byte Start, byte End)> SubParameterRanges(StateMachine machine)
        => (List<(byte Start, byte End)>)Field("_subParameterRanges").GetValue(machine)!;

    public static bool SubParameterLimitOverflowed(StateMachine machine) => (bool)Field("_subParameterLimitOverflowed").GetValue(machine)!;

    public static StringBuilder OscString(StateMachine machine) => (StringBuilder)Field("_oscString").GetValue(machine)!;

    public static int OscParameter(StateMachine machine) => (int)Field("_oscParameter").GetValue(machine)!;
}

internal sealed class DummyDispatch : TermDispatch
{
    public override void Print(char printable)
    {
    }

    public override void PrintString(ReadOnlySpan<char> text)
    {
    }
}

[TestClass]
public sealed class OutputEngineTests
{
    private static StateMachine CreateMachine() => new(new OutputStateMachineEngine(new DummyDispatch()));

    private static void AssertState(StateMachine machine, string expected) => Assert.AreEqual(expected, MachineInspector.GetState(machine));

    private static void Step(StateMachine machine, char character, string expectedState)
    {
        machine.ProcessCharacter(character);
        AssertState(machine, expectedState);
    }

    [TestMethod]
    [DataRow("Ground", "Escape")]
    [DataRow("Escape", "Escape")]
    [DataRow("EscapeIntermediate", "Escape")]
    [DataRow("CsiEntry", "Escape")]
    [DataRow("CsiIgnore", "Escape")]
    [DataRow("CsiParam", "Escape")]
    [DataRow("CsiIntermediate", "Escape")]
    // The OscParam and OscString states wait for the string terminator instead of escaping out.
    [DataRow("OscParam", "OscTermination")]
    [DataRow("OscString", "OscTermination")]
    [DataRow("OscTermination", "Escape")]
    [DataRow("Ss3Entry", "Escape")]
    [DataRow("Ss3Param", "Escape")]
    [DataRow("DcsEntry", "Escape")]
    [DataRow("DcsIgnore", "Escape")]
    [DataRow("DcsIntermediate", "Escape")]
    [DataRow("DcsParam", "Escape")]
    [DataRow("DcsPassThrough", "Escape")]
    [DataRow("SosPmApcString", "Escape")]
    public void TestEscapePath(string initialState, string expectedEscapeState)
    {
        var machine = CreateMachine();
        MachineInspector.SetState(machine, initialState);
        if (initialState == "DcsPassThrough")
        {
            MachineInspector.SetDcsStringHandler(machine, _ => true);
        }

        machine.ProcessCharacter(AsciiChars.ESC);
        AssertState(machine, expectedEscapeState);
    }

    [TestMethod]
    public void TestEscapeImmediatePath()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '#', "EscapeIntermediate");
        Step(machine, '(', "EscapeIntermediate");
        Step(machine, ')', "EscapeIntermediate");
        Step(machine, '#', "EscapeIntermediate");
        Step(machine, '6', "Ground");
    }

    [TestMethod]
    public void TestEscapeThenC0Path()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");

        // The output engine executes a C0 control in the escape state without interrupting the sequence.
        Step(machine, '\u0003', "Escape");
        Step(machine, '[', "CsiEntry");
        Step(machine, '3', "CsiParam");
        Step(machine, '1', "CsiParam");
        Step(machine, 'm', "Ground");
    }

    [TestMethod]
    public void TestGroundPrint()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, 'a', "Ground");
    }

    [TestMethod]
    public void TestCsiEntry()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");
        Step(machine, 'm', "Ground");
    }

    [TestMethod]
    public void TestC1CsiEntry()
    {
        var machine = CreateMachine();
        machine.SetParserMode(StateMachine.Mode.AcceptC1, true);

        AssertState(machine, "Ground");
        Step(machine, '\u009b', "CsiEntry");
        Step(machine, 'm', "Ground");
    }

    [TestMethod]
    public void TestCsiImmediate()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");
        Step(machine, '$', "CsiIntermediate");
        Step(machine, '#', "CsiIntermediate");
        Step(machine, '%', "CsiIntermediate");
        Step(machine, 'v', "Ground");
    }

    [TestMethod]
    public void TestCsiParam()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");
        Step(machine, ';', "CsiParam");
        Step(machine, '3', "CsiParam");
        Step(machine, '2', "CsiParam");
        Step(machine, '4', "CsiParam");
        Step(machine, ';', "CsiParam");
        Step(machine, ';', "CsiParam");
        Step(machine, '8', "CsiParam");
        Step(machine, 'J', "Ground");

        var parameters = MachineInspector.Parameters(machine);
        Assert.HasCount(4, parameters);
        Assert.IsFalse(parameters[0].HasValue);
        Assert.AreEqual(324, (int)parameters[1]);
        Assert.IsFalse(parameters[2].HasValue);
        Assert.AreEqual(8, (int)parameters[3]);
    }

    [TestMethod]
    public void TestCsiMaxParamCount()
    {
        var machine = CreateMachine();

        // Output a sequence with 100 parameters.
        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");
        for (int index = 0; index < 100; index++)
        {
            if (index > 0)
            {
                Step(machine, ';', "CsiParam");
            }

            Step(machine, (char)('0' + index % 10), "CsiParam");
        }

        Step(machine, 'J', "Ground");

        // Only MAX_PARAMETER_COUNT (32) parameters should be stored.
        var parameters = MachineInspector.Parameters(machine);
        Assert.HasCount(StateMachine.MAX_PARAMETER_COUNT, parameters);
        for (int index = 0; index < StateMachine.MAX_PARAMETER_COUNT; index++)
        {
            Assert.IsTrue(parameters[index].HasValue);
            Assert.AreEqual(index % 10, parameters[index].Value);
        }
    }

    [TestMethod]
    public void TestLeadingZeroCsiParam()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");

        // Any number of leading zeros should be supported.
        for (int index = 0; index < 50; index++)
        {
            Step(machine, '0', "CsiParam");
        }

        // Only 5 digits are expected to be kept.
        for (int index = 0; index < 5; index++)
        {
            Step(machine, (char)('1' + index), "CsiParam");
        }

        Assert.AreEqual(12345, (int)MachineInspector.Parameters(machine)[^1]);
        Step(machine, 'J', "Ground");
    }

    [TestMethod]
    public void TestCsiSubParam()
    {
        var machine = CreateMachine();

        // "\e[:3;9:5::8J"
        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");
        Step(machine, ':', "CsiSubParam");
        Step(machine, '3', "CsiSubParam");
        Step(machine, ';', "CsiParam");
        Step(machine, '9', "CsiParam");
        Step(machine, ':', "CsiSubParam");
        Step(machine, '5', "CsiSubParam");
        Step(machine, ':', "CsiSubParam");
        Step(machine, ':', "CsiSubParam");
        Step(machine, '8', "CsiSubParam");
        Step(machine, 'J', "Ground");

        var parameters = MachineInspector.Parameters(machine);
        Assert.HasCount(2, parameters);
        Assert.IsFalse(parameters[0].HasValue);
        Assert.AreEqual(9, (int)parameters[1]);

        var subParameters = MachineInspector.SubParameters(machine);
        Assert.HasCount(4, subParameters);
        Assert.AreEqual(3, (int)subParameters[0]);
        Assert.AreEqual(5, (int)subParameters[1]);
        Assert.IsFalse(subParameters[2].HasValue);
        Assert.AreEqual(8, (int)subParameters[3]);

        var ranges = MachineInspector.SubParameterRanges(machine);
        Assert.AreEqual(0, (int)ranges[0].Start);
        Assert.AreEqual(1, (int)ranges[0].End);
        Assert.AreEqual(1, (int)ranges[1].Start);
        Assert.AreEqual(4, (int)ranges[1].End);

        Assert.HasCount(parameters.Count, ranges);

        // The last range ends at the last index or one past it.
        int lastEnd = ranges[^1].End;
        Assert.IsTrue(lastEnd == subParameters.Count - 1 || lastEnd == subParameters.Count);
    }

    [TestMethod]
    public void TestCsiMaxSubParamCount()
    {
        var machine = CreateMachine();

        // Output two parameters with 100 sub parameters each.
        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");
        for (int parameterIndex = 0; parameterIndex < 2; parameterIndex++)
        {
            Step(machine, '3', "CsiParam");
            for (int index = 0; index < 100; index++)
            {
                Step(machine, ':', "CsiSubParam");
                Step(machine, (char)('0' + index % 10), "CsiSubParam");
            }

            // Receiving 100 sub parameters should set the overflow flag.
            Assert.IsTrue(MachineInspector.SubParameterLimitOverflowed(machine));
            Step(machine, ';', "CsiParam");
            Assert.IsFalse(MachineInspector.SubParameterLimitOverflowed(machine));
        }

        Step(machine, 'J', "Ground");

        // Only MAX_SUBPARAMETER_COUNT (6) sub parameters should be stored for each parameter.
        var subParameters = MachineInspector.SubParameters(machine);
        Assert.HasCount(12, subParameters);

        // subParameters = { 0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5 }
        for (int index = 0; index < 12; index++)
        {
            Assert.IsTrue(subParameters[index].HasValue);
            Assert.AreEqual(index % 6, subParameters[index].Value);
        }

        var ranges = MachineInspector.SubParameterRanges(machine);
        Assert.AreEqual(0, (int)ranges[0].Start);
        Assert.AreEqual(6, (int)ranges[0].End);
        Assert.AreEqual(6, (int)ranges[1].Start);
        Assert.AreEqual(12, (int)ranges[1].End);
    }

    [TestMethod]
    public void TestLeadingZeroCsiSubParam()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");
        Step(machine, '3', "CsiParam");
        Step(machine, ':', "CsiSubParam");

        // Any number of leading zeros should be supported.
        for (int index = 0; index < 50; index++)
        {
            Step(machine, '0', "CsiSubParam");
        }

        // Only 5 digits are expected to be kept.
        for (int index = 0; index < 5; index++)
        {
            Step(machine, (char)('1' + index), "CsiSubParam");
        }

        Assert.AreEqual(12345, (int)MachineInspector.SubParameters(machine)[^1]);
        Step(machine, 'J', "Ground");
    }

    [TestMethod]
    public void TestCsiIgnore()
    {
        var machine = CreateMachine();

        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");
        Step(machine, '4', "CsiParam");
        Step(machine, ';', "CsiParam");
        Step(machine, '=', "CsiIgnore");
        Step(machine, '8', "CsiIgnore");
        Step(machine, 'J', "Ground");

        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '[', "CsiEntry");
        Step(machine, '4', "CsiParam");
        Step(machine, '#', "CsiIntermediate");
        Step(machine, ':', "CsiIgnore");
        Step(machine, '8', "CsiIgnore");
        Step(machine, 'J', "Ground");
    }

    [TestMethod]
    public void TestC1Osc()
    {
        var machine = CreateMachine();
        machine.SetParserMode(StateMachine.Mode.AcceptC1, true);

        AssertState(machine, "Ground");
        Step(machine, '\u009d', "OscParam");
        Step(machine, AsciiChars.BEL, "Ground");
    }

    [TestMethod]
    public void TestOscStringSimple()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, ']', "OscParam");
        Step(machine, '0', "OscParam");
        Step(machine, ';', "OscString");
        foreach (char character in "some text")
        {
            Step(machine, character, "OscString");
        }

        Step(machine, AsciiChars.BEL, "Ground");

        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, ']', "OscParam");
        Step(machine, '0', "OscParam");
        Step(machine, ';', "OscString");
        foreach (char character in "some text")
        {
            Step(machine, character, "OscString");
        }

        Step(machine, AsciiChars.ESC, "OscTermination");
        Step(machine, '\\', "Ground");
    }

    [TestMethod]
    public void TestLongOscString()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, ']', "OscParam");
        Step(machine, '0', "OscParam");
        machine.ProcessCharacter(';');

        // The original buffer was only 256 long, so any longer value should work.
        for (int index = 0; index < 260; index++)
        {
            Step(machine, 's', "OscString");
        }

        Assert.AreEqual(260, MachineInspector.OscString(machine).Length);
        Step(machine, AsciiChars.BEL, "Ground");
    }

    [TestMethod]
    public void NormalTestOscParam()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, ']', "OscParam");

        // Only 5 digits are expected to be kept.
        for (int index = 0; index < 5; index++)
        {
            Step(machine, (char)('1' + index), "OscParam");
        }

        Assert.AreEqual(12345, MachineInspector.OscParameter(machine));
        Step(machine, ';', "OscString");
        Step(machine, 's', "OscString");
        Step(machine, AsciiChars.BEL, "Ground");
    }

    [TestMethod]
    public void TestLeadingZeroOscParam()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, ']', "OscParam");

        // Any number of leading zeros should be supported.
        for (int index = 0; index < 50; index++)
        {
            Step(machine, '0', "OscParam");
        }

        // Only 5 digits are expected to be kept.
        for (int index = 0; index < 5; index++)
        {
            Step(machine, (char)('1' + index), "OscParam");
        }

        Assert.AreEqual(12345, MachineInspector.OscParameter(machine));
        Step(machine, ';', "OscString");
        Step(machine, 's', "OscString");
        Step(machine, AsciiChars.BEL, "Ground");
    }

    [TestMethod]
    public void TestLongOscParam()
    {
        var machine = CreateMachine();
        string maxValueText = ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, ']', "OscParam");
        foreach (char character in maxValueText)
        {
            Step(machine, character, "OscParam");
        }

        Assert.AreEqual(StateMachine.MAX_PARAMETER_VALUE, MachineInspector.OscParameter(machine));
        Step(machine, ';', "OscString");
        Step(machine, 's', "OscString");
        Step(machine, AsciiChars.BEL, "Ground");

        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, ']', "OscParam");
        foreach (char character in maxValueText)
        {
            Step(machine, character, "OscParam");
        }

        Assert.AreEqual(StateMachine.MAX_PARAMETER_VALUE, MachineInspector.OscParameter(machine));
        Step(machine, ';', "OscString");
        Step(machine, 's', "OscString");
        Step(machine, AsciiChars.BEL, "Ground");
    }

    [TestMethod]
    public void TestOscStringInvalidTermination()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, ']', "OscParam");
        Step(machine, '1', "OscParam");
        Step(machine, ';', "OscString");
        Step(machine, 's', "OscString");
        Step(machine, AsciiChars.ESC, "OscTermination");

        // This is not a string terminator.
        Step(machine, '[', "CsiEntry");
        Step(machine, '4', "CsiParam");
        Step(machine, ';', "CsiParam");
        Step(machine, 'm', "Ground");
    }

    [TestMethod]
    public void TestDcsEntry()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'P', "DcsEntry");
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('\\');
        AssertState(machine, "Ground");
    }

    [TestMethod]
    public void TestC1DcsEntry()
    {
        var machine = CreateMachine();
        machine.SetParserMode(StateMachine.Mode.AcceptC1, true);

        AssertState(machine, "Ground");
        Step(machine, '\u0090', "DcsEntry");
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('\\');
        AssertState(machine, "Ground");
    }

    [TestMethod]
    public void TestDcsImmediate()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'P', "DcsEntry");
        Step(machine, ' ', "DcsIntermediate");
        Step(machine, '#', "DcsIntermediate");
        Step(machine, '%', "DcsIntermediate");
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('\\');
        AssertState(machine, "Ground");
    }

    [TestMethod]
    public void TestDcsIgnore()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'P', "DcsEntry");
        Step(machine, ':', "DcsIgnore");
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('\\');
        AssertState(machine, "Ground");
    }

    [TestMethod]
    public void TestDcsParam()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'P', "DcsEntry");
        Step(machine, ';', "DcsParam");
        Step(machine, '3', "DcsParam");
        Step(machine, '2', "DcsParam");
        Step(machine, '4', "DcsParam");
        Step(machine, ';', "DcsParam");
        Step(machine, ';', "DcsParam");
        Step(machine, '8', "DcsParam");

        var parameters = MachineInspector.Parameters(machine);
        Assert.HasCount(4, parameters);
        Assert.IsFalse(parameters[0].HasValue);
        Assert.AreEqual(324, (int)parameters[1]);
        Assert.IsFalse(parameters[2].HasValue);
        Assert.AreEqual(8, (int)parameters[3]);

        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('\\');
        AssertState(machine, "Ground");
    }

    [TestMethod]
    public void TestDcsIntermediateAndPassThrough()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'P', "DcsEntry");
        Step(machine, ' ', "DcsIntermediate");

        // Without a dispatcher the pass through data is instead ignored.
        Step(machine, 'x', "DcsIgnore");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '\\', "Ground");
    }

    [TestMethod]
    public void TestDcsLongStringPassThrough()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'P', "DcsEntry");

        // Without a dispatcher the pass through state is instead ignored.
        Step(machine, 'q', "DcsIgnore");
        Step(machine, '#', "DcsIgnore");
        Step(machine, '1', "DcsIgnore");
        Step(machine, 'N', "DcsIgnore");
        Step(machine, 'N', "DcsIgnore");
        Step(machine, 'N', "DcsIgnore");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '\\', "Ground");
    }

    [TestMethod]
    public void TestDcsInvalidTermination()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'P', "DcsEntry");
        Step(machine, 'q', "DcsIgnore");
        Step(machine, '#', "DcsIgnore");
        Step(machine, AsciiChars.ESC, "Escape");

        // This is not a string terminator.
        Step(machine, '[', "CsiEntry");
        Step(machine, '4', "CsiParam");
        Step(machine, ';', "CsiParam");
        Step(machine, 'm', "Ground");
    }

    [TestMethod]
    public void TestSosPmApcString()
    {
        var machine = CreateMachine();

        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'X', "SosPmApcString");
        Step(machine, '1', "SosPmApcString");
        Step(machine, '2', "SosPmApcString");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '\\', "Ground");

        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '^', "SosPmApcString");
        Step(machine, '3', "SosPmApcString");
        Step(machine, '4', "SosPmApcString");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '\\', "Ground");

        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '_', "SosPmApcString");
        Step(machine, '5', "SosPmApcString");
        Step(machine, '6', "SosPmApcString");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '\\', "Ground");
    }

    [TestMethod]
    public void TestC1StringTerminator()
    {
        var machine = CreateMachine();
        machine.SetParserMode(StateMachine.Mode.AcceptC1, true);

        // C1 ST should terminate OSC string.
        AssertState(machine, "Ground");
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, ']', "OscParam");
        Step(machine, '1', "OscParam");
        Step(machine, ';', "OscString");
        Step(machine, 's', "OscString");
        Step(machine, '\u009c', "Ground");

        // C1 ST should terminate DCS passthrough string.
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'P', "DcsEntry");
        Step(machine, 'q', "DcsIgnore");
        Step(machine, '#', "DcsIgnore");
        machine.ProcessCharacter('1');
        Step(machine, '\u009c', "Ground");

        // C1 ST should terminate SOS/PM/APC string.
        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, 'X', "SosPmApcString");
        Step(machine, '1', "SosPmApcString");
        Step(machine, '\u009c', "Ground");

        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '^', "SosPmApcString");
        Step(machine, '2', "SosPmApcString");
        Step(machine, '\u009c', "Ground");

        Step(machine, AsciiChars.ESC, "Escape");
        Step(machine, '_', "SosPmApcString");
        Step(machine, '3', "SosPmApcString");
        Step(machine, '\u009c', "Ground");
    }
}

internal sealed class StatefulDispatch : TermDispatch
{
    public const int MAX_OPTIONS = 16;
    public const GraphicsOptions GRAPHICS_CLEARED = (GraphicsOptions)(-1);
    public const int XTERM_COLOR_TABLE_SIZE = 256;

    public string PrintedText = string.Empty;
    public int CursorDistance;
    public int Line;
    public int Column;
    public bool CursorUpCalled;
    public bool CursorDownCalled;
    public bool CursorBackwardCalled;
    public bool CursorForwardCalled;
    public bool CursorNextLineCalled;
    public bool CursorPreviousLineCalled;
    public bool CursorHorizontalPositionAbsoluteCalled;
    public bool VerticalLinePositionAbsoluteCalled;
    public bool HorizontalPositionRelativeCalled;
    public bool VerticalPositionRelativeCalled;
    public bool CursorPositionCalled;
    public bool CursorSaveCalled;
    public bool CursorLoadCalled;
    public bool EraseDisplayCalled;
    public bool EraseLineCalled;
    public bool InsertCharacterCalled;
    public bool DeleteCharacterCalled;
    public EraseType LastEraseType = (EraseType)(-1);
    public List<EraseType> EraseTypes = new();
    public bool SetGraphicsCalled;
    public StatusType LastStatusReportType = (StatusType)(-1);
    public bool DeviceStatusReportCalled;
    public bool DeviceAttributesCalled;
    public bool SecondaryDeviceAttributesCalled;
    public bool TertiaryDeviceAttributesCalled;
    public bool Vt52DeviceAttributesCalled;
    public bool RequestTerminalParametersCalled;
    public ReportingPermission LastReportingPermission = (ReportingPermission)(-1);
    public ModeParams LastModeType = (ModeParams)(-1);
    public List<ModeParams> ModeTypes = new();
    public bool ModeEnabled;
    public bool WarningBellCalled;
    public bool CarriageReturnCalled;
    public bool LineFeedCalled;
    public LineFeedType LastLineFeedType = (LineFeedType)(-1);
    public bool ReverseLineFeedCalled;
    public bool SetWindowTitleCalled;
    public string SetWindowTitleText = string.Empty;
    public bool ForwardTabCalled;
    public int NumTabs;
    public bool TabClearCalled;
    public List<TabClearType> TabClearTypes = new();
    public List<int> XtermResourcesChanged = new();
    public List<uint> XtermResourceValues = new();
    public List<int> XtermResourcesRequested = new();
    public List<int> XtermResourcesReset = new();
    public bool SetColorTableEntryCalled;
    public List<int> ColorTableEntriesRequested = new();
    public bool ResetAllColorsCalled;
    public List<int> ColorTableEntriesReset = new();
    public bool HyperlinkMode;
    public string CopyContent = string.Empty;
    public string Uri = string.Empty;
    public string CustomId = string.Empty;
    public List<GraphicsOptions> Options = new();
    public uint[] ColorTable = new uint[XTERM_COLOR_TABLE_SIZE];

    public StatefulDispatch()
    {
        ClearState();
    }

    public void ClearState()
    {
        PrintedText = string.Empty;
        CursorDistance = 0;
        Line = 0;
        Column = 0;
        CursorUpCalled = false;
        CursorDownCalled = false;
        CursorBackwardCalled = false;
        CursorForwardCalled = false;
        CursorNextLineCalled = false;
        CursorPreviousLineCalled = false;
        CursorHorizontalPositionAbsoluteCalled = false;
        VerticalLinePositionAbsoluteCalled = false;
        HorizontalPositionRelativeCalled = false;
        VerticalPositionRelativeCalled = false;
        CursorPositionCalled = false;
        CursorSaveCalled = false;
        CursorLoadCalled = false;
        EraseDisplayCalled = false;
        EraseLineCalled = false;
        InsertCharacterCalled = false;
        DeleteCharacterCalled = false;
        LastEraseType = (EraseType)(-1);
        EraseTypes.Clear();
        SetGraphicsCalled = false;
        LastStatusReportType = (StatusType)(-1);
        DeviceStatusReportCalled = false;
        DeviceAttributesCalled = false;
        SecondaryDeviceAttributesCalled = false;
        TertiaryDeviceAttributesCalled = false;
        Vt52DeviceAttributesCalled = false;
        RequestTerminalParametersCalled = false;
        LastReportingPermission = (ReportingPermission)(-1);
        LastModeType = (ModeParams)(-1);
        ModeTypes.Clear();
        ModeEnabled = false;
        WarningBellCalled = false;
        CarriageReturnCalled = false;
        LineFeedCalled = false;
        LastLineFeedType = (LineFeedType)(-1);
        ReverseLineFeedCalled = false;
        SetWindowTitleCalled = false;
        SetWindowTitleText = string.Empty;
        ForwardTabCalled = false;
        NumTabs = 0;
        TabClearCalled = false;
        TabClearTypes.Clear();
        XtermResourcesChanged.Clear();
        XtermResourceValues.Clear();
        XtermResourcesRequested.Clear();
        XtermResourcesReset.Clear();
        SetColorTableEntryCalled = false;
        ColorTableEntriesRequested.Clear();
        ResetAllColorsCalled = false;
        ColorTableEntriesReset.Clear();
        HyperlinkMode = false;
        CopyContent = string.Empty;
        Uri = string.Empty;
        CustomId = string.Empty;
        Options.Clear();
        for (int index = 0; index < MAX_OPTIONS; index++)
        {
            Options.Add(GRAPHICS_CLEARED);
        }

        Array.Clear(ColorTable);
    }

    public override void Print(char printable) => PrintedText += printable;

    public override void PrintString(ReadOnlySpan<char> text) => PrintedText += text.ToString();

    public override void CursorUp(int distance)
    {
        CursorUpCalled = true;
        CursorDistance = distance;
    }

    public override void CursorDown(int distance)
    {
        CursorDownCalled = true;
        CursorDistance = distance;
    }

    public override void CursorBackward(int distance)
    {
        CursorBackwardCalled = true;
        CursorDistance = distance;
    }

    public override void CursorForward(int distance)
    {
        CursorForwardCalled = true;
        CursorDistance = distance;
    }

    public override void CursorNextLine(int distance)
    {
        CursorNextLineCalled = true;
        CursorDistance = distance;
    }

    public override void CursorPrevLine(int distance)
    {
        CursorPreviousLineCalled = true;
        CursorDistance = distance;
    }

    public override void CursorHorizontalPositionAbsolute(int column)
    {
        CursorHorizontalPositionAbsoluteCalled = true;
        CursorDistance = column;
    }

    public override void VerticalLinePositionAbsolute(int line)
    {
        VerticalLinePositionAbsoluteCalled = true;
        CursorDistance = line;
    }

    public override void HorizontalPositionRelative(int distance)
    {
        HorizontalPositionRelativeCalled = true;
        CursorDistance = distance;
    }

    public override void VerticalPositionRelative(int distance)
    {
        VerticalPositionRelativeCalled = true;
        CursorDistance = distance;
    }

    public override void CursorPosition(int line, int column)
    {
        CursorPositionCalled = true;
        Line = line;
        Column = column;
    }

    public override void CursorSaveState() => CursorSaveCalled = true;

    public override void CursorRestoreState() => CursorLoadCalled = true;

    public override void EraseInDisplay(EraseType eraseType)
    {
        EraseDisplayCalled = true;
        LastEraseType = eraseType;
        EraseTypes.Add(eraseType);
    }

    public override void EraseInLine(EraseType eraseType)
    {
        EraseLineCalled = true;
        LastEraseType = eraseType;
        EraseTypes.Add(eraseType);
    }

    public override void InsertCharacter(int count)
    {
        InsertCharacterCalled = true;
        CursorDistance = count;
    }

    public override void DeleteCharacter(int count)
    {
        DeleteCharacterCalled = true;
        CursorDistance = count;
    }

    public override void SetGraphicsRendition(VTParameters options)
    {
        Options.Clear();
        for (int index = 0; index < options.Size; index++)
        {
            Options.Add((GraphicsOptions)options.At(index).ValueOr(0));
        }

        SetGraphicsCalled = true;
    }

    public override void DeviceStatusReport(StatusType statusType, VTParameter id)
    {
        DeviceStatusReportCalled = true;
        LastStatusReportType = statusType;
    }

    public override void DeviceAttributes() => DeviceAttributesCalled = true;

    public override void SecondaryDeviceAttributes() => SecondaryDeviceAttributesCalled = true;

    public override void TertiaryDeviceAttributes() => TertiaryDeviceAttributesCalled = true;

    public override void Vt52DeviceAttributes() => Vt52DeviceAttributesCalled = true;

    public override void RequestTerminalParameters(ReportingPermission permission)
    {
        RequestTerminalParametersCalled = true;
        LastReportingPermission = permission;
    }

    private void ModeParamsHelper(ModeParams param, bool enable)
    {
        LastModeType = param;
        ModeTypes.Add(param);
        ModeEnabled = enable;
    }

    public override void SetMode(ModeParams param) => ModeParamsHelper(param, true);

    public override void ResetMode(ModeParams param) => ModeParamsHelper(param, false);

    public override void SetAnsiMode(bool ansiMode) => ModeParamsHelper(ModeParams.DECANM_AnsiMode, ansiMode);

    public override void WarningBell() => WarningBellCalled = true;

    public override void CarriageReturn() => CarriageReturnCalled = true;

    public override void LineFeed(LineFeedType lineFeedType)
    {
        LineFeedCalled = true;
        LastLineFeedType = lineFeedType;
    }

    public override void ReverseLineFeed() => ReverseLineFeedCalled = true;

    public override void SetWindowTitle(ReadOnlySpan<char> title)
    {
        SetWindowTitleCalled = true;
        SetWindowTitleText = title.ToString();
    }

    public override void ForwardTab(int numTabs)
    {
        ForwardTabCalled = true;
        NumTabs = numTabs;
    }

    public override void TabClear(TabClearType clearType)
    {
        TabClearCalled = true;
        TabClearTypes.Add(clearType);
    }

    public override void SetColorTableEntry(int tableIndex, uint color)
    {
        SetColorTableEntryCalled = true;
        ColorTable[tableIndex] = color;
    }

    public override void RequestColorTableEntry(int tableIndex) => ColorTableEntriesRequested.Add(tableIndex);

    public override void ResetColorTable() => ResetAllColorsCalled = true;

    public override void ResetColorTableEntry(int tableIndex) => ColorTableEntriesReset.Add(tableIndex);

    public override void SetXtermColorResource(int resource, uint color)
    {
        XtermResourcesChanged.Add(resource);
        XtermResourceValues.Add(color);
    }

    public override void RequestXtermColorResource(int resource) => XtermResourcesRequested.Add(resource);

    public override void ResetXtermColorResource(int resource) => XtermResourcesReset.Add(resource);

    public override void SetClipboard(string content) => CopyContent = content;

    public override void AddHyperlink(ReadOnlySpan<char> uri, ReadOnlySpan<char> parameters)
    {
        HyperlinkMode = true;
        Uri = uri.ToString();
        if (!parameters.IsEmpty)
        {
            CustomId = parameters.ToString();
        }
    }

    public override void EndHyperlink()
    {
        HyperlinkMode = false;
        Uri = string.Empty;
        CustomId = string.Empty;
    }

    public override void DoConEmuAction(ReadOnlySpan<char> text)
    {
    }
}

[TestClass]
public sealed class StateMachineExternalTests
{
    // 9999-10000 is the classic boundary for most parsers, 16383-16384 the one for DECSR, 32767-32768 SHORT_MAX.
    private static readonly long[] _paramValues = [0, 1, 2, 1000, 9999, 10000, 16383, 16384, 32767, 32768, 50000, 999999999];

    private static StateMachine CreateMachine(out StatefulDispatch dispatch)
    {
        dispatch = new StatefulDispatch();
        return new StateMachine(new OutputStateMachineEngine(dispatch));
    }

    private static void InsertNumberToMachine(StateMachine machine, long number)
    {
        foreach (char character in number.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            machine.ProcessCharacter(character);
        }
    }

    private static void Feed(StateMachine machine, string text)
    {
        foreach (char character in text)
        {
            machine.ProcessCharacter(character);
        }
    }

    // 0 and 1 give 1, 1 to MAX_PARAMETER_VALUE give the value, larger values give MAX_PARAMETER_VALUE.
    private static long ApplyParameterBoundary(long given)
    {
        if (given <= 1)
        {
            return 1;
        }

        if (given <= StateMachine.MAX_PARAMETER_VALUE)
        {
            return given;
        }

        return StateMachine.MAX_PARAMETER_VALUE;
    }

    private static void TestCsiCursorMovement(char command, long distance, bool useDistance, bool addExtraParam, Func<bool> flag, StateMachine machine, StatefulDispatch dispatch)
    {
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('[');

        if (useDistance)
        {
            InsertNumberToMachine(machine, distance);

            // Extraneous parameters should be ignored.
            if (addExtraParam)
            {
                machine.ProcessCharacter(';');
                machine.ProcessCharacter('9');
            }
        }

        machine.ProcessCharacter(command);

        Assert.IsTrue(flag(), $"command {command}, distance {distance}");

        long expectedDistance = 1;
        if (useDistance)
        {
            expectedDistance = ApplyParameterBoundary(distance);
        }

        Assert.AreEqual(expectedDistance, (long)dispatch.CursorDistance, $"command {command}, distance {distance}");
    }

    private static void RunAllCursorMovements(long distance, bool useDistance, bool addExtraParam)
    {
        var machine = CreateMachine(out var dispatch);

        (char Command, Func<bool> Flag)[] cases =
        [
            ('A', () => dispatch.CursorUpCalled),
            ('B', () => dispatch.CursorDownCalled),
            ('C', () => dispatch.CursorForwardCalled),
            ('D', () => dispatch.CursorBackwardCalled),
            ('E', () => dispatch.CursorNextLineCalled),
            ('F', () => dispatch.CursorPreviousLineCalled),
            ('G', () => dispatch.CursorHorizontalPositionAbsoluteCalled),
            ('`', () => dispatch.CursorHorizontalPositionAbsoluteCalled),
            ('d', () => dispatch.VerticalLinePositionAbsoluteCalled),
            ('a', () => dispatch.HorizontalPositionRelativeCalled),
            ('e', () => dispatch.VerticalPositionRelativeCalled),
            ('@', () => dispatch.InsertCharacterCalled),
            ('P', () => dispatch.DeleteCharacterCalled),
        ];

        foreach (var (command, flag) in cases)
        {
            TestCsiCursorMovement(command, distance, useDistance, addExtraParam, flag, machine, dispatch);
            dispatch.ClearState();
        }
    }

    [TestMethod]
    public void TestCsiCursorMovementWithValues()
    {
        foreach (long distance in _paramValues)
        {
            RunAllCursorMovements(distance, true, false);
            RunAllCursorMovements(distance, true, true);
        }
    }

    [TestMethod]
    public void TestCsiCursorMovementWithoutValues()
    {
        // This value should be ignored with the false below.
        RunAllCursorMovements(9999, false, false);
    }

    [TestMethod]
    public void TestCsiCursorPosition()
    {
        foreach (long row in _paramValues)
        {
            foreach (long column in _paramValues)
            {
                var machine = CreateMachine(out var dispatch);

                machine.ProcessCharacter(AsciiChars.ESC);
                machine.ProcessCharacter('[');

                InsertNumberToMachine(machine, row);
                machine.ProcessCharacter(';');
                InsertNumberToMachine(machine, column);
                machine.ProcessCharacter('H');

                Assert.IsTrue(dispatch.CursorPositionCalled, $"row {row}, column {column}");
                Assert.AreEqual(ApplyParameterBoundary(row), (long)dispatch.Line, $"row {row}, column {column}");
                Assert.AreEqual(ApplyParameterBoundary(column), (long)dispatch.Column, $"row {row}, column {column}");
            }
        }
    }

    [TestMethod]
    public void TestCsiCursorPositionWithOnlyRow()
    {
        foreach (long row in _paramValues)
        {
            var machine = CreateMachine(out var dispatch);

            machine.ProcessCharacter(AsciiChars.ESC);
            machine.ProcessCharacter('[');

            InsertNumberToMachine(machine, row);
            machine.ProcessCharacter('H');

            Assert.IsTrue(dispatch.CursorPositionCalled, $"row {row}");
            Assert.AreEqual(ApplyParameterBoundary(row), (long)dispatch.Line, $"row {row}");

            // Without the second param, the column should always be the default.
            Assert.AreEqual(1, dispatch.Column, $"row {row}");
        }
    }

    [TestMethod]
    public void TestCursorSaveLoad()
    {
        var machine = CreateMachine(out var dispatch);

        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('7');
        Assert.IsTrue(dispatch.CursorSaveCalled);

        dispatch.ClearState();

        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('8');
        Assert.IsTrue(dispatch.CursorLoadCalled);

        dispatch.ClearState();

        // CSI s is dispatched as SetLeftRightScrollingMargins rather than CursorSaveState, so it is not tested here.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('[');
        machine.ProcessCharacter('u');
        Assert.IsTrue(dispatch.CursorLoadCalled);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestAnsiMode()
    {
        var machine = CreateMachine(out var dispatch);

        dispatch.ModeEnabled = true;
        machine.ProcessString("\u001b[?2l");
        Assert.AreEqual(ModeParams.DECANM_AnsiMode, dispatch.LastModeType);
        Assert.IsFalse(dispatch.ModeEnabled);

        dispatch.ClearState();
        machine.SetParserMode(StateMachine.Mode.Ansi, false);

        machine.ProcessString("\u001b<");
        Assert.AreEqual(ModeParams.DECANM_AnsiMode, dispatch.LastModeType);
        Assert.IsTrue(dispatch.ModeEnabled);

        dispatch.ClearState();
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(12)]
    [DataRow(25)]
    [DataRow(40)]
    [DataRow(1049)]
    public void TestPrivateModes(int modeNumber)
    {
        var modeType = DispatchTypes.DecPrivateMode(modeNumber);
        string setModeSequence = "\u001b[?" + modeNumber + "h";
        string resetModeSequence = "\u001b[?" + modeNumber + "l";

        var machine = CreateMachine(out var dispatch);

        machine.ProcessString(setModeSequence);
        Assert.AreEqual(modeType, dispatch.LastModeType);
        Assert.IsTrue(dispatch.ModeEnabled);

        dispatch.ClearState();
        dispatch.ModeEnabled = true;

        machine.ProcessString(resetModeSequence);
        Assert.AreEqual(modeType, dispatch.LastModeType);
        Assert.IsFalse(dispatch.ModeEnabled);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestMultipleModes()
    {
        var machine = CreateMachine(out var dispatch);

        var expectedModes = new[] { ModeParams.DECSCNM_ScreenMode, ModeParams.DECCKM_CursorKeysMode, ModeParams.DECOM_OriginMode };

        machine.ProcessString("\u001b[?5;1;6h");
        Assert.IsTrue(dispatch.ModeEnabled);
        CollectionAssert.AreEqual(expectedModes, dispatch.ModeTypes);

        dispatch.ClearState();
        dispatch.ModeEnabled = true;

        machine.ProcessString("\u001b[?5;1;6l");
        Assert.IsFalse(dispatch.ModeEnabled);
        CollectionAssert.AreEqual(expectedModes, dispatch.ModeTypes);

        dispatch.ClearState();
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(0, 2)]
    [DataRow(0, 10)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(1, 2)]
    [DataRow(1, 10)]
    public void TestErase(int eraseOperation, int eraseTypeValue)
    {
        var machine = CreateMachine(out var dispatch);

        char operation;
        Func<bool> operationCallback;
        if (eraseOperation == 0)
        {
            operation = 'J';
            operationCallback = () => dispatch.EraseDisplayCalled;
        }
        else
        {
            operation = 'K';
            operationCallback = () => dispatch.EraseLineCalled;
        }

        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('[');

        EraseType expectedEraseType;
        if (eraseTypeValue == 0)
        {
            expectedEraseType = EraseType.ToEnd;
            InsertNumberToMachine(machine, eraseTypeValue);
        }
        else if (eraseTypeValue == 1)
        {
            expectedEraseType = EraseType.FromBeginning;
            InsertNumberToMachine(machine, eraseTypeValue);
        }
        else if (eraseTypeValue == 2)
        {
            expectedEraseType = EraseType.All;
            InsertNumberToMachine(machine, eraseTypeValue);
        }
        else
        {
            // Do nothing. The default case of 10 should be like a 0 to the end.
            expectedEraseType = EraseType.ToEnd;
        }

        machine.ProcessCharacter(operation);

        Assert.IsTrue(operationCallback());
        Assert.AreEqual(expectedEraseType, dispatch.LastEraseType);
    }

    [TestMethod]
    public void TestMultipleErase()
    {
        var machine = CreateMachine(out var dispatch);

        machine.ProcessString("\u001b[3;2J");
        var expectedEraseTypes = new[] { EraseType.Scrollback, EraseType.All };
        Assert.IsTrue(dispatch.EraseDisplayCalled);
        CollectionAssert.AreEqual(expectedEraseTypes, dispatch.EraseTypes);

        dispatch.ClearState();

        machine.ProcessString("\u001b[0;1K");
        expectedEraseTypes = [EraseType.ToEnd, EraseType.FromBeginning];
        Assert.IsTrue(dispatch.EraseLineCalled);
        CollectionAssert.AreEqual(expectedEraseTypes, dispatch.EraseTypes);

        dispatch.ClearState();
    }

    private static void VerifyDispatchTypes(GraphicsOptions[] expectedOptions, StatefulDispatch dispatch)
    {
        Assert.HasCount(expectedOptions.Length, dispatch.Options);

        for (int index = 0; index < dispatch.Options.Count; index++)
        {
            var expectedOption = StatefulDispatch.GRAPHICS_CLEARED;
            if (index < expectedOptions.Length)
            {
                expectedOption = expectedOptions[index];
            }

            Assert.AreEqual(expectedOption, dispatch.Options[index], $"Graphics option match failed, index [{index}]");
        }
    }

    [TestMethod]
    public void TestSetGraphicsRendition()
    {
        var machine = CreateMachine(out var dispatch);

        // Test 1: Check default case.
        Feed(machine, "\u001b[m");
        Assert.IsTrue(dispatch.SetGraphicsCalled);
        VerifyDispatchTypes([GraphicsOptions.Off], dispatch);

        dispatch.ClearState();

        // Test 2: Check clear/0 case.
        Feed(machine, "\u001b[0m");
        Assert.IsTrue(dispatch.SetGraphicsCalled);
        VerifyDispatchTypes([GraphicsOptions.Off], dispatch);

        dispatch.ClearState();

        // Test 3: Check 'handful of options' case.
        Feed(machine, "\u001b[1;4;7;30;45;53m");
        Assert.IsTrue(dispatch.SetGraphicsCalled);
        VerifyDispatchTypes(
            [
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Negative,
                GraphicsOptions.ForegroundBlack,
                GraphicsOptions.BackgroundMagenta,
                GraphicsOptions.Overline,
            ],
            dispatch);

        dispatch.ClearState();

        // Test 4: Check 'many options' (>16) case.
        Feed(machine, "\u001b[1;4;1;4;1;4;1;4;1;4;1;4;1;4;1;4;1m");
        Assert.IsTrue(dispatch.SetGraphicsCalled);
        VerifyDispatchTypes(
            [
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Intense,
            ],
            dispatch);

        dispatch.ClearState();

        // Test 5.a: Test an empty param at the end of a sequence.
        machine.ProcessString("\u001b[1;m");
        Assert.IsTrue(dispatch.SetGraphicsCalled);
        VerifyDispatchTypes([GraphicsOptions.Intense, GraphicsOptions.Off], dispatch);

        dispatch.ClearState();

        // Test 5.b: Test an empty param in the middle of a sequence.
        machine.ProcessString("\u001b[1;;1m");
        Assert.IsTrue(dispatch.SetGraphicsCalled);
        VerifyDispatchTypes([GraphicsOptions.Intense, GraphicsOptions.Off, GraphicsOptions.Intense], dispatch);

        dispatch.ClearState();

        // Test 5.c: Test an empty param at the start of a sequence.
        machine.ProcessString("\u001b[;31;1m");
        Assert.IsTrue(dispatch.SetGraphicsCalled);
        VerifyDispatchTypes([GraphicsOptions.Off, GraphicsOptions.ForegroundRed, GraphicsOptions.Intense], dispatch);

        dispatch.ClearState();
    }

    private static void ProcessDeviceStatusReport(string sequence, StatusType expected)
    {
        var machine = CreateMachine(out var dispatch);
        Feed(machine, sequence);

        Assert.IsTrue(dispatch.DeviceStatusReportCalled, sequence);
        Assert.AreEqual(expected, dispatch.LastStatusReportType, sequence);
    }

    [TestMethod]
    public void TestDeviceStatusReport()
    {
        // Operating status (case 5).
        ProcessDeviceStatusReport("\u001b[5n", StatusType.OperatingStatus);

        // Cursor position report (case 6).
        ProcessDeviceStatusReport("\u001b[6n", StatusType.CursorPositionReport);

        // Extended cursor position report (case ?6).
        ProcessDeviceStatusReport("\u001b[?6n", StatusType.ExtendedCursorPositionReport);

        // Printer status (case ?15).
        ProcessDeviceStatusReport("\u001b[?15n", StatusType.PrinterStatus);

        // User-defined keys (case ?25).
        ProcessDeviceStatusReport("\u001b[?25n", StatusType.UserDefinedKeys);

        // Keyboard status / dialect (case ?26).
        ProcessDeviceStatusReport("\u001b[?26n", StatusType.KeyboardStatus);

        // Locator status (case ?55).
        ProcessDeviceStatusReport("\u001b[?55n", StatusType.LocatorStatus);

        // Locator identity (case ?56).
        ProcessDeviceStatusReport("\u001b[?56n", StatusType.LocatorIdentity);

        // Macro space report (case ?62).
        ProcessDeviceStatusReport("\u001b[?62n", StatusType.MacroSpaceReport);

        // Memory checksum (case ?63).
        ProcessDeviceStatusReport("\u001b[?63n", StatusType.MemoryChecksum);

        // Data integrity report (case ?75).
        ProcessDeviceStatusReport("\u001b[?75n", StatusType.DataIntegrity);

        // Multiple session status (case ?85).
        ProcessDeviceStatusReport("\u001b[?85n", StatusType.MultipleSessionStatus);
    }

    [TestMethod]
    public void TestDeviceAttributes()
    {
        var machine = CreateMachine(out var dispatch);

        // Test 1: Check default case, no params.
        Feed(machine, "\u001b[c");
        Assert.IsTrue(dispatch.DeviceAttributesCalled);

        dispatch.ClearState();

        // Test 2: Check default case, 0 param.
        Feed(machine, "\u001b[0c");
        Assert.IsTrue(dispatch.DeviceAttributesCalled);

        dispatch.ClearState();

        // Test 3: Check fail case, 1 (or any other) param.
        Feed(machine, "\u001b[1c");
        Assert.IsFalse(dispatch.DeviceAttributesCalled);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestSecondaryDeviceAttributes()
    {
        var machine = CreateMachine(out var dispatch);

        // Test 1: Check default case, no params.
        Feed(machine, "\u001b[>c");
        Assert.IsTrue(dispatch.SecondaryDeviceAttributesCalled);

        dispatch.ClearState();

        // Test 2: Check default case, 0 param.
        Feed(machine, "\u001b[>0c");
        Assert.IsTrue(dispatch.SecondaryDeviceAttributesCalled);

        dispatch.ClearState();

        // Test 3: Check fail case, 1 (or any other) param.
        Feed(machine, "\u001b[>1c");
        Assert.IsFalse(dispatch.SecondaryDeviceAttributesCalled);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestTertiaryDeviceAttributes()
    {
        var machine = CreateMachine(out var dispatch);

        // Test 1: Check default case, no params.
        Feed(machine, "\u001b[=c");
        Assert.IsTrue(dispatch.TertiaryDeviceAttributesCalled);

        dispatch.ClearState();

        // Test 2: Check default case, 0 param.
        Feed(machine, "\u001b[=0c");
        Assert.IsTrue(dispatch.TertiaryDeviceAttributesCalled);

        dispatch.ClearState();

        // Test 3: Check fail case, 1 (or any other) param.
        Feed(machine, "\u001b[=1c");
        Assert.IsFalse(dispatch.TertiaryDeviceAttributesCalled);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestRequestTerminalParameters()
    {
        var machine = CreateMachine(out var dispatch);

        // Test 1: Check default case, no params.
        Feed(machine, "\u001b[x");
        Assert.IsTrue(dispatch.RequestTerminalParametersCalled);
        Assert.AreEqual(ReportingPermission.Unsolicited, dispatch.LastReportingPermission);

        dispatch.ClearState();

        // Test 2: Check unsolicited permission, 0 param.
        Feed(machine, "\u001b[0x");
        Assert.IsTrue(dispatch.RequestTerminalParametersCalled);
        Assert.AreEqual(ReportingPermission.Unsolicited, dispatch.LastReportingPermission);

        // Test 3: Check solicited permission, 1 param.
        Feed(machine, "\u001b[1x");
        Assert.IsTrue(dispatch.RequestTerminalParametersCalled);
        Assert.AreEqual(ReportingPermission.Solicited, dispatch.LastReportingPermission);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestStrings()
    {
        var machine = CreateMachine(out var dispatch);

        // Test 1: Basic String processing. One sequence in a string.
        machine.ProcessString("\u001b[0m");
        Assert.IsTrue(dispatch.SetGraphicsCalled);

        dispatch.ClearState();

        // Test 2: A couple of sequences all in one string.
        machine.ProcessString("\u001b[1;4;7;30;45;53m\u001b[2J");
        Assert.IsTrue(dispatch.SetGraphicsCalled);
        Assert.IsTrue(dispatch.EraseDisplayCalled);

        VerifyDispatchTypes(
            [
                GraphicsOptions.Intense,
                GraphicsOptions.Underline,
                GraphicsOptions.Negative,
                GraphicsOptions.ForegroundBlack,
                GraphicsOptions.BackgroundMagenta,
                GraphicsOptions.Overline,
            ],
            dispatch);
        Assert.AreEqual(EraseType.All, dispatch.LastEraseType);

        dispatch.ClearState();

        // Test 3: Two sequences separated by a non-sequence of characters.
        machine.ProcessString("\u001b[1;30mHello World\u001b[2J");

        Assert.IsTrue(dispatch.SetGraphicsCalled);
        Assert.IsTrue(dispatch.EraseDisplayCalled);

        VerifyDispatchTypes([GraphicsOptions.Intense, GraphicsOptions.ForegroundBlack], dispatch);
        Assert.AreEqual(EraseType.All, dispatch.LastEraseType);

        dispatch.ClearState();

        // Test 4: An entire sequence broke into multiple strings.
        machine.ProcessString("\u001b[1;");
        Assert.IsFalse(dispatch.SetGraphicsCalled);
        Assert.IsFalse(dispatch.EraseDisplayCalled);

        machine.ProcessString("30mHello World\u001b[2J");

        Assert.IsTrue(dispatch.SetGraphicsCalled);
        Assert.IsTrue(dispatch.EraseDisplayCalled);

        VerifyDispatchTypes([GraphicsOptions.Intense, GraphicsOptions.ForegroundBlack], dispatch);
        Assert.AreEqual(EraseType.All, dispatch.LastEraseType);

        dispatch.ClearState();

        // Test 5: A sequence with mixed ProcessCharacter and ProcessString calls.
        machine.ProcessString("\u001b[1;");
        Assert.IsFalse(dispatch.SetGraphicsCalled);
        Assert.IsFalse(dispatch.EraseDisplayCalled);

        machine.ProcessCharacter('3');
        Assert.IsFalse(dispatch.SetGraphicsCalled);
        Assert.IsFalse(dispatch.EraseDisplayCalled);

        machine.ProcessCharacter('0');
        Assert.IsFalse(dispatch.SetGraphicsCalled);
        Assert.IsFalse(dispatch.EraseDisplayCalled);

        machine.ProcessCharacter('m');

        Assert.IsTrue(dispatch.SetGraphicsCalled);
        Assert.IsFalse(dispatch.EraseDisplayCalled);
        VerifyDispatchTypes([GraphicsOptions.Intense, GraphicsOptions.ForegroundBlack], dispatch);

        machine.ProcessString("Hello World\u001b[2J");

        Assert.IsTrue(dispatch.EraseDisplayCalled);
        Assert.AreEqual(EraseType.All, dispatch.LastEraseType);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestLineFeed()
    {
        var machine = CreateMachine(out var dispatch);

        // IND (Index) escape sequence.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('D');

        Assert.IsTrue(dispatch.LineFeedCalled);
        Assert.AreEqual(LineFeedType.WithoutReturn, dispatch.LastLineFeedType);

        dispatch.ClearState();

        // NEL (Next Line) escape sequence.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('E');

        Assert.IsTrue(dispatch.LineFeedCalled);
        Assert.AreEqual(LineFeedType.WithReturn, dispatch.LastLineFeedType);

        dispatch.ClearState();

        // LF (Line Feed) control code.
        machine.ProcessCharacter(AsciiChars.LF);

        Assert.IsTrue(dispatch.LineFeedCalled);
        Assert.AreEqual(LineFeedType.DependsOnMode, dispatch.LastLineFeedType);

        dispatch.ClearState();

        // FF (Form Feed) control code.
        machine.ProcessCharacter(AsciiChars.FF);

        Assert.IsTrue(dispatch.LineFeedCalled);
        Assert.AreEqual(LineFeedType.DependsOnMode, dispatch.LastLineFeedType);

        dispatch.ClearState();

        // VT (Vertical Tab) control code.
        machine.ProcessCharacter(AsciiChars.VT);

        Assert.IsTrue(dispatch.LineFeedCalled);
        Assert.AreEqual(LineFeedType.DependsOnMode, dispatch.LastLineFeedType);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestControlCharacters()
    {
        var machine = CreateMachine(out var dispatch);

        // BEL (Warning Bell) control character.
        machine.ProcessCharacter(AsciiChars.BEL);
        Assert.IsTrue(dispatch.WarningBellCalled);

        dispatch.ClearState();

        // BS (Back Space) control character.
        machine.ProcessCharacter(AsciiChars.BS);
        Assert.IsTrue(dispatch.CursorBackwardCalled);
        Assert.AreEqual(1, dispatch.CursorDistance);

        dispatch.ClearState();

        // CR (Carriage Return) control character.
        machine.ProcessCharacter(AsciiChars.CR);
        Assert.IsTrue(dispatch.CarriageReturnCalled);

        dispatch.ClearState();

        // HT (Horizontal Tab) control character.
        machine.ProcessCharacter(AsciiChars.TAB);
        Assert.IsTrue(dispatch.ForwardTabCalled);
        Assert.AreEqual(1, dispatch.NumTabs);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestTabClear()
    {
        var machine = CreateMachine(out var dispatch);

        machine.ProcessString("\u001b[g");
        var expectedClearTypes = new[] { TabClearType.ClearCurrentColumn };
        Assert.IsTrue(dispatch.TabClearCalled);
        CollectionAssert.AreEqual(expectedClearTypes, dispatch.TabClearTypes);

        dispatch.ClearState();

        machine.ProcessString("\u001b[3g");
        expectedClearTypes = [TabClearType.ClearAllColumns];
        Assert.IsTrue(dispatch.TabClearCalled);
        CollectionAssert.AreEqual(expectedClearTypes, dispatch.TabClearTypes);

        dispatch.ClearState();

        machine.ProcessString("\u001b[0;3g");
        expectedClearTypes = [TabClearType.ClearCurrentColumn, TabClearType.ClearAllColumns];
        Assert.IsTrue(dispatch.TabClearCalled);
        CollectionAssert.AreEqual(expectedClearTypes, dispatch.TabClearTypes);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestVt52Sequences()
    {
        var machine = CreateMachine(out var dispatch);

        // ANSI mode must be reset for VT52 sequences to be recognized.
        machine.SetParserMode(StateMachine.Mode.Ansi, false);

        // Cursor Up.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('A');
        Assert.IsTrue(dispatch.CursorUpCalled);
        Assert.AreEqual(1, dispatch.CursorDistance);

        dispatch.ClearState();

        // Cursor Down.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('B');
        Assert.IsTrue(dispatch.CursorDownCalled);
        Assert.AreEqual(1, dispatch.CursorDistance);

        dispatch.ClearState();

        // Cursor Right.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('C');
        Assert.IsTrue(dispatch.CursorForwardCalled);
        Assert.AreEqual(1, dispatch.CursorDistance);

        dispatch.ClearState();

        // Cursor Left.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('D');
        Assert.IsTrue(dispatch.CursorBackwardCalled);
        Assert.AreEqual(1, dispatch.CursorDistance);

        dispatch.ClearState();

        // Cursor to Home.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('H');
        Assert.IsTrue(dispatch.CursorPositionCalled);
        Assert.AreEqual(1, dispatch.Line);
        Assert.AreEqual(1, dispatch.Column);

        dispatch.ClearState();

        // Reverse Line Feed.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('I');
        Assert.IsTrue(dispatch.ReverseLineFeedCalled);

        dispatch.ClearState();

        // Erase to End of Screen.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('J');
        Assert.IsTrue(dispatch.EraseDisplayCalled);
        Assert.AreEqual(EraseType.ToEnd, dispatch.LastEraseType);

        dispatch.ClearState();

        // Erase to End of Line.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('K');
        Assert.IsTrue(dispatch.EraseLineCalled);
        Assert.AreEqual(EraseType.ToEnd, dispatch.LastEraseType);

        dispatch.ClearState();

        // Direct Cursor Address.
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('Y');
        machine.ProcessCharacter((char)(' ' + 3)); // Coordinates must be printable ASCII values,
        machine.ProcessCharacter((char)(' ' + 5)); // so are relative to 0x20 (the space character).
        Assert.IsTrue(dispatch.CursorPositionCalled);
        Assert.AreEqual(3, dispatch.Line - 1); // CursorPosition coordinates are 1-based,
        Assert.AreEqual(5, dispatch.Column - 1); // so are 1 more than the expected values.

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestIdentifyDeviceReport()
    {
        var machine = CreateMachine(out var dispatch);

        // Identify Device in VT52 mode.
        machine.SetParserMode(StateMachine.Mode.Ansi, false);
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('Z');
        Assert.IsTrue(dispatch.Vt52DeviceAttributesCalled);
        Assert.IsFalse(dispatch.DeviceAttributesCalled);

        dispatch.ClearState();

        // Identify Device in ANSI mode.
        machine.SetParserMode(StateMachine.Mode.Ansi, true);
        machine.ProcessCharacter(AsciiChars.ESC);
        machine.ProcessCharacter('Z');
        Assert.IsTrue(dispatch.DeviceAttributesCalled);
        Assert.IsFalse(dispatch.Vt52DeviceAttributesCalled);

        dispatch.ClearState();
    }

    // Shared by the default foreground and background tests, which differ only by the first resource number.
    private static void AssertResourceSequence(StateMachine machine, StatefulDispatch dispatch, string sequence, int[] expectedResources, uint[] expectedValues)
    {
        machine.ProcessString(sequence);
        Assert.HasCount(expectedResources.Length, dispatch.XtermResourcesChanged, sequence);
        for (int index = 0; index < expectedResources.Length; index++)
        {
            Assert.AreEqual(expectedResources[index], dispatch.XtermResourcesChanged[index], sequence);
        }

        for (int index = 0; index < expectedValues.Length; index++)
        {
            Assert.AreEqual(expectedValues[index], dispatch.XtermResourceValues[index], sequence);
        }

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestOscSetDefaultForeground()
    {
        var machine = CreateMachine(out var dispatch);
        uint gray11 = TextColor.Rgb(0x11, 0x11, 0x11);
        uint gray10 = TextColor.Rgb(0x10, 0x10, 0x10);
        uint gray22 = TextColor.Rgb(0x22, 0x22, 0x22);
        uint orange = TextColor.Rgb(255, 140, 0);

        // Single param
        AssertResourceSequence(machine, dispatch, "\u001b]10;rgb:1/1/1\u001b\\", [10], [gray11]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;rgb:12/34/56\u001b\\", [10], [TextColor.Rgb(0x12, 0x34, 0x56)]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;#111\u001b\\", [10], [gray10]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;#123456\u001b\\", [10], [TextColor.Rgb(0x12, 0x34, 0x56)]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;DarkOrange\u001b\\", [10], [orange]);

        // Multiple params
        AssertResourceSequence(machine, dispatch, "\u001b]10;#111;rgb:2/2/2\u001b\\", [10, 11], [gray10, gray22]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;#111;DarkOrange\u001b\\", [10, 11], [gray10, orange]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;#111;DarkOrange;rgb:2/2/2\u001b\\", [10, 11, 12], [gray10, orange, gray22]);

        // Partially valid multi-param sequences.
        AssertResourceSequence(machine, dispatch, "\u001b]10;#111;\u001b\\", [10], [gray10]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;#111;rgb:\u001b\\", [10], [gray10]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;#111;#2\u001b\\", [10], [gray10]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;;rgb:1/1/1\u001b\\", [11], [gray11]);
        AssertResourceSequence(machine, dispatch, "\u001b]10;#1;rgb:1/1/1\u001b\\", [11], [gray11]);

        // Invalid sequences.
        AssertResourceSequence(machine, dispatch, "\u001b]10;rgb:1/1/\u001b\\", [], []);
        AssertResourceSequence(machine, dispatch, "\u001b]10;#1\u001b\\", [], []);
    }

    [TestMethod]
    public void TestOscSetDefaultBackground()
    {
        var machine = CreateMachine(out var dispatch);
        uint gray11 = TextColor.Rgb(0x11, 0x11, 0x11);
        uint gray10 = TextColor.Rgb(0x10, 0x10, 0x10);
        uint gray22 = TextColor.Rgb(0x22, 0x22, 0x22);
        uint orange = TextColor.Rgb(255, 140, 0);

        AssertResourceSequence(machine, dispatch, "\u001b]11;rgb:1/1/1\u001b\\", [11], [gray11]);

        // Single param
        AssertResourceSequence(machine, dispatch, "\u001b]11;rgb:12/34/56\u001b\\", [11], [TextColor.Rgb(0x12, 0x34, 0x56)]);
        AssertResourceSequence(machine, dispatch, "\u001b]11;#111\u001b\\", [11], [gray10]);
        AssertResourceSequence(machine, dispatch, "\u001b]11;#123456\u001b\\", [11], [TextColor.Rgb(0x12, 0x34, 0x56)]);
        AssertResourceSequence(machine, dispatch, "\u001b]11;DarkOrange\u001b\\", [11], [orange]);

        // Multiple params
        AssertResourceSequence(machine, dispatch, "\u001b]11;#111;rgb:2/2/2\u001b\\", [11, 12], [gray10, gray22]);
        AssertResourceSequence(machine, dispatch, "\u001b]11;#111;DarkOrange\u001b\\", [11, 12], [gray10, orange]);

        // The third param is out of range, but it is technically still in compliance to set it.
        AssertResourceSequence(machine, dispatch, "\u001b]11;#111;DarkOrange;rgb:2/2/2\u001b\\", [11, 12, 13], [gray10, orange]);

        // Partially valid multi-param sequences.
        AssertResourceSequence(machine, dispatch, "\u001b]11;#111;\u001b\\", [11], [gray10]);
        AssertResourceSequence(machine, dispatch, "\u001b]11;#111;rgb:\u001b\\", [11], [gray10]);
        AssertResourceSequence(machine, dispatch, "\u001b]11;#111;#2\u001b\\", [11], [gray10]);
        AssertResourceSequence(machine, dispatch, "\u001b]11;;rgb:1/1/1\u001b\\", [12], [gray11]);
        AssertResourceSequence(machine, dispatch, "\u001b]11;#1;rgb:1/1/1\u001b\\", [12], [gray11]);

        // Invalid sequences.
        AssertResourceSequence(machine, dispatch, "\u001b]11;rgb:1/1/\u001b\\", [], []);
        AssertResourceSequence(machine, dispatch, "\u001b]11;#1\u001b\\", [], []);
    }

    [TestMethod]
    public void TestOscSetColorTableEntry()
    {
        var machine = CreateMachine(out var dispatch);
        uint black = TextColor.Rgb(0, 0, 0);
        uint gray11 = TextColor.Rgb(0x11, 0x11, 0x11);
        uint gray10 = TextColor.Rgb(0x10, 0x10, 0x10);
        uint gray22 = TextColor.Rgb(0x22, 0x22, 0x22);
        uint orange = TextColor.Rgb(255, 165, 0);

        machine.ProcessString("\u001b]4;0;rgb:1/1/1\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(gray11, dispatch.ColorTable[0]);

        dispatch.ClearState();

        machine.ProcessString("\u001b]4;16;rgb:11/11/11\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(gray11, dispatch.ColorTable[16]);

        dispatch.ClearState();

        machine.ProcessString("\u001b]4;64;#111\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(gray10, dispatch.ColorTable[64]);

        dispatch.ClearState();

        machine.ProcessString("\u001b]4;128;orange\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(orange, dispatch.ColorTable[128]);

        dispatch.ClearState();

        // Invalid sequences.
        string[] invalidSequences =
        [
            "\u001b]4;\u001b\\",
            "\u001b]4;;\u001b\\",
            "\u001b]4;0\u001b\\",
            "\u001b]4;111\u001b\\",
            "\u001b]4;#111\u001b\\",
            "\u001b]4;1;111\u001b\\",
            "\u001b]4;1;rgb:\u001b\\",
        ];
        foreach (string invalidSequence in invalidSequences)
        {
            machine.ProcessString(invalidSequence);
            Assert.IsFalse(dispatch.SetColorTableEntryCalled, invalidSequence);

            dispatch.ClearState();
        }

        // Multiple params.
        machine.ProcessString("\u001b]4;0;rgb:1/1/1;16;rgb:2/2/2\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(gray11, dispatch.ColorTable[0]);
        Assert.AreEqual(gray22, dispatch.ColorTable[16]);

        dispatch.ClearState();

        machine.ProcessString("\u001b]4;0;rgb:1/1/1;16;rgb:2/2/2;64;#111\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(gray11, dispatch.ColorTable[0]);
        Assert.AreEqual(gray22, dispatch.ColorTable[16]);
        Assert.AreEqual(gray10, dispatch.ColorTable[64]);

        dispatch.ClearState();

        machine.ProcessString("\u001b]4;0;rgb:1/1/1;16;rgb:2/2/2;64;#111;128;orange\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(gray11, dispatch.ColorTable[0]);
        Assert.AreEqual(gray22, dispatch.ColorTable[16]);
        Assert.AreEqual(gray10, dispatch.ColorTable[64]);
        Assert.AreEqual(orange, dispatch.ColorTable[128]);

        dispatch.ClearState();

        // Partially valid sequences. Valid colors should not be affected by invalid colors.
        machine.ProcessString("\u001b]4;0;rgb:11;1;rgb:2/2/2;2;#111;3;orange;4;#111\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(black, dispatch.ColorTable[0]);
        Assert.AreEqual(gray22, dispatch.ColorTable[1]);
        Assert.AreEqual(gray10, dispatch.ColorTable[2]);
        Assert.AreEqual(orange, dispatch.ColorTable[3]);
        Assert.AreEqual(gray10, dispatch.ColorTable[4]);

        dispatch.ClearState();

        machine.ProcessString("\u001b]4;0;rgb:1/1/1;1;rgb:2/2/2;2;#111;3;orange;4;111\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(gray11, dispatch.ColorTable[0]);
        Assert.AreEqual(gray22, dispatch.ColorTable[1]);
        Assert.AreEqual(gray10, dispatch.ColorTable[2]);
        Assert.AreEqual(orange, dispatch.ColorTable[3]);
        Assert.AreEqual(black, dispatch.ColorTable[4]);

        dispatch.ClearState();

        machine.ProcessString("\u001b]4;0;rgb:1/1/1;1;rgb:2;2;#111;3;orange;4;#222\u001b\\");
        Assert.IsTrue(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(gray11, dispatch.ColorTable[0]);
        Assert.AreEqual(black, dispatch.ColorTable[1]);
        Assert.AreEqual(gray10, dispatch.ColorTable[2]);
        Assert.AreEqual(orange, dispatch.ColorTable[3]);
        Assert.AreEqual(TextColor.Rgb(0x20, 0x20, 0x20), dispatch.ColorTable[4]);

        dispatch.ClearState();

        // Invalid multi-param sequences.
        machine.ProcessString("\u001b]4;0;;1;;\u001b\\");
        Assert.IsFalse(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(black, dispatch.ColorTable[0]);
        Assert.AreEqual(black, dispatch.ColorTable[1]);

        dispatch.ClearState();

        machine.ProcessString("\u001b]4;0;;;;;1;;;;;\u001b\\");
        Assert.IsFalse(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(black, dispatch.ColorTable[0]);
        Assert.AreEqual(black, dispatch.ColorTable[1]);

        dispatch.ClearState();

        machine.ProcessString("\u001b]4;0;rgb:1/1/;16;rgb:2/2/;64;#11\u001b\\");
        Assert.IsFalse(dispatch.SetColorTableEntryCalled);
        Assert.AreEqual(black, dispatch.ColorTable[0]);
        Assert.AreEqual(black, dispatch.ColorTable[16]);
        Assert.AreEqual(black, dispatch.ColorTable[64]);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestOscGetColorTableEntry()
    {
        var machine = CreateMachine(out var dispatch);

        // Inquire about 0-4 skipping 2.
        machine.ProcessString("\u001b]4;0;?;1;?;2;;3;?;4;?\u001b\\");
        CollectionAssert.AreEqual(new[] { 0, 1, 3, 4 }, dispatch.ColorTableEntriesRequested);
        dispatch.ClearState();

        // Set 0-4, except 2 (inquire).
        machine.ProcessString("\u001b]4;0;rgb:00/00/00;1;rgb:00/00/01;2;?;3;rgb:00/00/03;4;rgb:00/00/04\u001b\\");
        Assert.AreEqual(TextColor.Rgb(0, 0, 0), dispatch.ColorTable[0]);
        Assert.AreEqual(TextColor.Rgb(0, 0, 1), dispatch.ColorTable[1]);
        Assert.AreEqual(TextColor.Rgb(0, 0, 3), dispatch.ColorTable[3]);
        Assert.AreEqual(TextColor.Rgb(0, 0, 4), dispatch.ColorTable[4]);
        Assert.HasCount(1, dispatch.ColorTableEntriesRequested);
        Assert.AreEqual(2, dispatch.ColorTableEntriesRequested[0]);
        dispatch.ClearState();

        dispatch.ColorTable[3] = TextColor.Rgb(0xfe, 0xed, 0xfa);

        // Set 0, inquire 1, truncated 3.
        machine.ProcessString("\u001b]4;0;rgb:f0/00/00;1;?;3\u001b\\");
        Assert.AreEqual(TextColor.Rgb(0xf0, 0, 0), dispatch.ColorTable[0]);

        // Unchanged from before ProcessString.
        Assert.AreEqual(TextColor.Rgb(0xfe, 0xed, 0xfa), dispatch.ColorTable[3]);
        Assert.HasCount(1, dispatch.ColorTableEntriesRequested);
        Assert.AreEqual(1, dispatch.ColorTableEntriesRequested[0]);
        dispatch.ClearState();
    }

    [TestMethod]
    public void TestOscXtermResourceReport()
    {
        var machine = CreateMachine(out var dispatch);

        machine.ProcessString("\u001b]10;?\u001b\\");
        Assert.IsEmpty(dispatch.XtermResourcesChanged);
        Assert.HasCount(1, dispatch.XtermResourcesRequested);
        Assert.AreEqual(10, dispatch.XtermResourcesRequested[0]);
        dispatch.ClearState();

        // Two params, skip first.
        machine.ProcessString("\u001b]10;;?\u001b\\");
        Assert.IsEmpty(dispatch.XtermResourcesChanged);
        Assert.HasCount(1, dispatch.XtermResourcesRequested);
        Assert.AreEqual(11, dispatch.XtermResourcesRequested[0]);
        dispatch.ClearState();

        // Two params, set first.
        machine.ProcessString("\u001b]10;rgb:11/22/33;?\u001b\\");
        Assert.HasCount(1, dispatch.XtermResourcesChanged);
        Assert.AreEqual(10, dispatch.XtermResourcesChanged[0]);
        Assert.AreEqual(TextColor.Rgb(0x11, 0x22, 0x33), dispatch.XtermResourceValues[0]);
        Assert.HasCount(1, dispatch.XtermResourcesRequested);
        Assert.AreEqual(11, dispatch.XtermResourcesRequested[0]);
        dispatch.ClearState();

        // Two params, set first, starting at 12.
        machine.ProcessString("\u001b]12;rgb:11/22/33;?\u001b\\");
        Assert.HasCount(1, dispatch.XtermResourcesChanged);
        Assert.AreEqual(12, dispatch.XtermResourcesChanged[0]);
        Assert.AreEqual(TextColor.Rgb(0x11, 0x22, 0x33), dispatch.XtermResourceValues[0]);
        Assert.HasCount(1, dispatch.XtermResourcesRequested);
        Assert.AreEqual(13, dispatch.XtermResourcesRequested[0]);
        dispatch.ClearState();

        // Request all 10.
        machine.ProcessString("\u001b]10;?;?;?;?;?;?;?;?;?;?\u001b\\");
        Assert.IsEmpty(dispatch.XtermResourcesChanged);
        CollectionAssert.AreEqual(new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 }, dispatch.XtermResourcesRequested);
        dispatch.ClearState();

        // Request out of range.
        machine.ProcessString("\u001b]10;;?\u001b\\");
        Assert.IsEmpty(dispatch.XtermResourcesChanged);
        Assert.HasCount(1, dispatch.XtermResourcesRequested);
        Assert.AreEqual(11, dispatch.XtermResourcesRequested[0]);
        dispatch.ClearState();
    }

    [TestMethod]
    public void TestOscXtermResourceReset()
    {
        var machine = CreateMachine(out var dispatch);

        machine.ProcessString("\u001b]110\u001b\\");
        Assert.IsEmpty(dispatch.XtermResourcesChanged);
        Assert.IsEmpty(dispatch.XtermResourcesRequested);
        Assert.HasCount(1, dispatch.XtermResourcesReset);
        Assert.AreEqual(10, dispatch.XtermResourcesReset[0]);
        dispatch.ClearState();

        // Dangling ;
        machine.ProcessString("\u001b]111;\u001b\\");
        Assert.IsEmpty(dispatch.XtermResourcesChanged);
        Assert.IsEmpty(dispatch.XtermResourcesRequested);
        Assert.HasCount(1, dispatch.XtermResourcesReset);
        Assert.AreEqual(11, dispatch.XtermResourcesReset[0]);
        dispatch.ClearState();

        // This is xterm behavior: ignore the entire sequence if any params exist.
        machine.ProcessString("\u001b]111;110\u001b\\");
        Assert.IsEmpty(dispatch.XtermResourcesChanged);
        Assert.IsEmpty(dispatch.XtermResourcesRequested);
        Assert.IsEmpty(dispatch.XtermResourcesReset);
        dispatch.ClearState();
    }

    [TestMethod]
    public void TestOscColorTableReset()
    {
        var machine = CreateMachine(out var dispatch);

        machine.ProcessString("\u001b]104\u001b\\");
        Assert.IsTrue(dispatch.ResetAllColorsCalled);
        Assert.IsEmpty(dispatch.ColorTableEntriesReset);
        Assert.IsEmpty(dispatch.ColorTableEntriesRequested);
        Assert.IsEmpty(dispatch.XtermResourcesReset);
        dispatch.ClearState();

        machine.ProcessString("\u001b]104;1;3;5;7;9\u001b\\");
        Assert.IsFalse(dispatch.ResetAllColorsCalled);
        Assert.HasCount(5, dispatch.ColorTableEntriesReset);
        Assert.IsEmpty(dispatch.ColorTableEntriesRequested);
        Assert.IsEmpty(dispatch.XtermResourcesReset);
        Assert.AreEqual(1, dispatch.ColorTableEntriesReset[0]);
        Assert.AreEqual(3, dispatch.ColorTableEntriesReset[1]);
        Assert.AreEqual(5, dispatch.ColorTableEntriesReset[2]);
        Assert.AreEqual(7, dispatch.ColorTableEntriesReset[3]);
        Assert.AreEqual(9, dispatch.ColorTableEntriesReset[4]);
        dispatch.ClearState();

        // This is xterm behavior: stop after the first failed parse.
        machine.ProcessString("\u001b]104;1;a;3\u001b\\");
        Assert.IsFalse(dispatch.ResetAllColorsCalled);
        Assert.IsFalse(dispatch.SetColorTableEntryCalled);
        Assert.HasCount(1, dispatch.ColorTableEntriesReset);
        Assert.IsEmpty(dispatch.ColorTableEntriesRequested);
        Assert.IsEmpty(dispatch.XtermResourcesReset);
        Assert.AreEqual(1, dispatch.ColorTableEntriesReset[0]);
        dispatch.ClearState();

        machine.ProcessString("\u001b]104;;;\u001b\\");
        Assert.IsFalse(dispatch.SetColorTableEntryCalled);
        Assert.IsEmpty(dispatch.ColorTableEntriesReset);
        Assert.IsEmpty(dispatch.ColorTableEntriesRequested);
        Assert.IsEmpty(dispatch.XtermResourcesReset);
        dispatch.ClearState();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(21)]
    public void TestOscSetWindowTitle(int oscNumber)
    {
        string oscPrefix = "\u001b]" + oscNumber;
        const string STRING_TERMINATOR = "\u001b\\";

        var machine = CreateMachine(out var dispatch);

        machine.ProcessString(oscPrefix);
        machine.ProcessString(";Title Text");
        machine.ProcessString(STRING_TERMINATOR);
        Assert.IsTrue(dispatch.SetWindowTitleCalled);
        Assert.AreEqual("Title Text", dispatch.SetWindowTitleText);

        dispatch.ClearState();

        // Make sure this is cleared.
        dispatch.SetWindowTitleText = "****";

        machine.ProcessString(oscPrefix);
        machine.ProcessString(";");
        machine.ProcessString(STRING_TERMINATOR);
        Assert.IsTrue(dispatch.SetWindowTitleCalled);
        Assert.AreEqual(string.Empty, dispatch.SetWindowTitleText);

        dispatch.ClearState();
        dispatch.SetWindowTitleText = "****";

        machine.ProcessString(oscPrefix);
        machine.ProcessString(STRING_TERMINATOR);
        Assert.IsTrue(dispatch.SetWindowTitleCalled);
        Assert.AreEqual(string.Empty, dispatch.SetWindowTitleText);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestSetClipboard()
    {
        var machine = CreateMachine(out var dispatch);

        // Passing an empty `Pc` param and a base64-encoded simple text `Pd` param works.
        machine.ProcessString("\u001b]52;;Zm9v\u0007");
        Assert.AreEqual("foo", dispatch.CopyContent);

        dispatch.ClearState();

        // Passing an empty `Pc` param and a base64-encoded multi-lines text `Pd` works.
        machine.ProcessString("\u001b]52;;Zm9vDQpiYXI=\u0007");
        Assert.AreEqual("foo\r\nbar", dispatch.CopyContent);

        dispatch.ClearState();

        // Passing an empty `Pc` param and a base64-encoded multibyte text `Pd` works.
        machine.ProcessString("\u001b]52;;44Gr44G744KT44GU5rGJ6K+t7ZWc6rWt\u0007");
        Assert.AreEqual("にほんご汉语한국", dispatch.CopyContent);

        dispatch.ClearState();

        // Passing an empty `Pc` param and a base64-encoded multibyte text w/ emoji sequences `Pd` works.
        machine.ProcessString("\u001b]52;;8J+RjfCfkY3wn4+78J+RjfCfj7zwn5GN8J+PvfCfkY3wn4++8J+RjfCfj78=\u0007");
        Assert.AreEqual(
            "👍👍🏻👍🏼👍🏽👍🏾👍🏿",
            dispatch.CopyContent);

        dispatch.ClearState();

        // Passing a non-empty `Pc` param (`s0` is ignored) and a valid `Pd` param works.
        machine.ProcessString("\u001b]52;s0;Zm9v\u0007");
        Assert.AreEqual("foo", dispatch.CopyContent);

        dispatch.ClearState();

        // Each of these is illegal and must not change the content.
        string[] unchangedSequences =
        [
            // Passing only base64 `Pd` param.
            "\u001b]52;Zm9v\u0007",

            // Passing a non-base64 `Pd` param.
            "\u001b]52;;???\u0007",

            // Passing a valid `Pc;Pd` with one more extra param.
            "\u001b]52;;;Zm9v\u0007",

            // Passing a query character.
            "\u001b]52;;?\u0007",

            // Passing a query character without `Pc` param.
            "\u001b]52;?\u0007",

            // Passing a query character with one more extra param.
            "\u001b]52;;;?\u0007",
        ];
        foreach (string sequence in unchangedSequences)
        {
            dispatch.CopyContent = "UNCHANGED";
            machine.ProcessString(sequence);
            Assert.AreEqual("UNCHANGED", dispatch.CopyContent, sequence);

            dispatch.ClearState();
        }
    }

    [TestMethod]
    public void TestAddHyperlink()
    {
        var machine = CreateMachine(out var dispatch);
        const string CLOSE_SEQUENCE = "\u001b]8;;\u001b\\";

        // First we test with no custom id. Process the opening osc 8 sequence.
        machine.ProcessString("\u001b]8;;test.url\u001b\\");
        Assert.IsTrue(dispatch.HyperlinkMode);
        Assert.AreEqual("test.url", dispatch.Uri);
        Assert.AreEqual(string.Empty, dispatch.CustomId);

        // Process the closing osc 8 sequences.
        machine.ProcessString(CLOSE_SEQUENCE);
        Assert.IsFalse(dispatch.HyperlinkMode);
        Assert.AreEqual(string.Empty, dispatch.Uri);

        // Next we test with a custom id. Process the opening osc 8 sequence.
        machine.ProcessString("\u001b]8;id=testId;test2.url\u001b\\");
        Assert.IsTrue(dispatch.HyperlinkMode);
        Assert.AreEqual("test2.url", dispatch.Uri);
        Assert.AreEqual("testId", dispatch.CustomId);

        // Process the closing osc 8 sequence.
        machine.ProcessString(CLOSE_SEQUENCE);
        Assert.IsFalse(dispatch.HyperlinkMode);
        Assert.AreEqual(string.Empty, dispatch.Uri);

        // Let's try more complicated params and URLs.
        machine.ProcessString("\u001b]8;id=testId;https://example.com\u001b\\");
        Assert.IsTrue(dispatch.HyperlinkMode);
        Assert.AreEqual("https://example.com", dispatch.Uri);
        Assert.AreEqual("testId", dispatch.CustomId);

        machine.ProcessString(CLOSE_SEQUENCE);
        Assert.IsFalse(dispatch.HyperlinkMode);
        Assert.AreEqual(string.Empty, dispatch.Uri);

        // Multiple params.
        machine.ProcessString("\u001b]8;id=testId:foo=bar;https://example.com\u001b\\");
        Assert.IsTrue(dispatch.HyperlinkMode);
        Assert.AreEqual("https://example.com", dispatch.Uri);
        Assert.AreEqual("testId", dispatch.CustomId);

        machine.ProcessString(CLOSE_SEQUENCE);
        Assert.IsFalse(dispatch.HyperlinkMode);
        Assert.AreEqual(string.Empty, dispatch.Uri);

        machine.ProcessString("\u001b]8;foo=bar:id=testId;https://example.com\u001b\\");
        Assert.IsTrue(dispatch.HyperlinkMode);
        Assert.AreEqual("https://example.com", dispatch.Uri);
        Assert.AreEqual("testId", dispatch.CustomId);

        machine.ProcessString(CLOSE_SEQUENCE);
        Assert.IsFalse(dispatch.HyperlinkMode);
        Assert.AreEqual(string.Empty, dispatch.Uri);

        // URIs with query strings.
        machine.ProcessString("\u001b]8;id=testId;https://example.com?query1=value1\u001b\\");
        Assert.IsTrue(dispatch.HyperlinkMode);
        Assert.AreEqual("https://example.com?query1=value1", dispatch.Uri);
        Assert.AreEqual("testId", dispatch.CustomId);

        machine.ProcessString(CLOSE_SEQUENCE);
        Assert.IsFalse(dispatch.HyperlinkMode);
        Assert.AreEqual(string.Empty, dispatch.Uri);

        machine.ProcessString("\u001b]8;id=testId;https://example.com?query1=value1;value2;value3\u001b\\");
        Assert.IsTrue(dispatch.HyperlinkMode);
        Assert.AreEqual("https://example.com?query1=value1;value2;value3", dispatch.Uri);
        Assert.AreEqual("testId", dispatch.CustomId);

        machine.ProcessString(CLOSE_SEQUENCE);
        Assert.IsFalse(dispatch.HyperlinkMode);
        Assert.AreEqual(string.Empty, dispatch.Uri);

        dispatch.ClearState();
    }

    [TestMethod]
    public void TestC1ParserMode()
    {
        var machine = CreateMachine(out var dispatch);

        // C1 parsing disabled: CSI control ignored and rest of sequence printed.
        machine.SetParserMode(StateMachine.Mode.AcceptC1, false);
        machine.ProcessString("\u009b" + "123A");
        Assert.IsFalse(dispatch.CursorUpCalled);
        Assert.AreEqual("123A", dispatch.PrintedText);

        dispatch.ClearState();

        // C1 parsing enabled: CSI interpreted and CUP sequence executed.
        machine.SetParserMode(StateMachine.Mode.AcceptC1, true);
        machine.ProcessString("\u009b" + "123A");
        Assert.IsTrue(dispatch.CursorUpCalled);
        Assert.AreEqual(123, dispatch.CursorDistance);

        dispatch.ClearState();

        // C1 parsing disabled: NEL has no effect within a sequence.
        machine.SetParserMode(StateMachine.Mode.AcceptC1, false);
        machine.ProcessString("\u001b[12" + "\u0085" + ";34H");
        Assert.IsFalse(dispatch.LineFeedCalled);
        Assert.IsTrue(dispatch.CursorPositionCalled);
        Assert.AreEqual(12, dispatch.Line);
        Assert.AreEqual(34, dispatch.Column);
        Assert.AreEqual(string.Empty, dispatch.PrintedText);

        dispatch.ClearState();

        // C1 parsing enabled: NEL aborts sequence and executes line feed.
        machine.SetParserMode(StateMachine.Mode.AcceptC1, true);
        machine.ProcessString("\u001b[12" + "\u0085" + ";34H");
        Assert.IsTrue(dispatch.LineFeedCalled);
        Assert.AreEqual(LineFeedType.WithReturn, dispatch.LastLineFeedType);
        Assert.IsFalse(dispatch.CursorPositionCalled);
        Assert.AreEqual(";34H", dispatch.PrintedText);

        dispatch.ClearState();
    }
}
